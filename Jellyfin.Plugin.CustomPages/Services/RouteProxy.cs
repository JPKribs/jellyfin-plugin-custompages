using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.CustomPages.Models;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Forwards a page's server side route to its configured target. The target URL is fixed in the route
/// definition and never comes from the caller, so a page can only reach the resources an administrator
/// wired up for it.
/// </summary>
public static class RouteProxy
{
    /// <summary>The named <see cref="HttpClient"/> the forwarder resolves.</summary>
    public const string HttpClientName = "CustomPages.RouteProxy";

    /// <summary>
    /// The largest request body forwarded to a target. A route on an Anyone page is reachable without
    /// signing in, so the body is capped before it is buffered rather than after.
    /// </summary>
    public const int MaxRequestBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The largest response body relayed back from a target. The response is buffered in memory before
    /// it is returned, so a target answering with an endless stream cannot exhaust the server.
    /// </summary>
    public const int MaxResponseBytes = 16 * 1024 * 1024;

    // Response headers relayed back to the page. Everything else is dropped: the route answers on the
    // Jellyfin origin, so a target's Set-Cookie, CORS, CSP, framing, caching, or front end proxy directive
    // (X-Accel-Redirect and friends) would otherwise be applied to Jellyfin's origin as if Jellyfin had
    // sent it. Only headers that describe the payload itself, or that a client acts on for this one
    // response, make the trip.
    private static readonly HashSet<string> ResponseAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "ETag", "Last-Modified", "Content-Disposition", "Content-Language", "Content-Encoding",
        "Content-Range", "Accept-Ranges", "Location", "Retry-After", "Link", "Vary"
    };

    // Custom X- headers are relayed too, since that is where a target hands back a session or CSRF token
    // a page has to echo, minus the ones a browser, Jellyfin, or a proxy in front of it would act on.
    private static readonly HashSet<string> ExtensionDenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-Frame-Options", "X-Content-Type-Options", "X-XSS-Protection", "X-Robots-Tag",
        "X-Permitted-Cross-Domain-Policies", "X-DNS-Prefetch-Control", "X-Download-Options",
        "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version", "X-Sendfile", "X-Sendfile2",
        "X-Lighttpd-Send-File", "X-Reproxy-URL", "X-Real-IP",
        "X-Emby-Authorization", "X-Emby-Token", "X-MediaBrowser-Token", "X-UA-Compatible"
    };

    // Prefixes of X- headers that are never relayed, for families with open ended members.
    private static readonly string[] ExtensionDenyPrefixes =
    {
        "X-Accel-", "X-Forwarded-", "X-LiteSpeed-", "X-Emby-", "X-MediaBrowser-", "X-Jellyfin-"
    };

    // Incoming request headers not to pass through to the target. The transport headers would corrupt
    // the forwarded request, and the auth headers are the viewer's Jellyfin session, which must never be
    // handed to a route target. Content-Type is applied to the content, and the route's own Basic auth
    // replaces any Authorization. Everything else is forwarded, so whatever protocol header a page sets
    // for its own target, such as a session or CSRF token the target hands back, reaches it.
    private static readonly HashSet<string> RequestDenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Authorization", "Cookie", "Content-Length", "Content-Type", "Connection",
        "Keep-Alive", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade",
        "X-Emby-Authorization", "X-Emby-Token", "X-MediaBrowser-Token", "Accept-Encoding"
    };

    /// <summary>
    /// Forwards one request to a route's target and returns the target's response.
    /// </summary>
    /// <remarks>
    /// A target that cannot be reached, answers too slowly, or answers with more than
    /// <see cref="MaxResponseBytes"/> comes back as a result carrying <see cref="ForwardResult.Error"/>
    /// rather than as an exception, so the caller can answer with a gateway status. Cancellation of
    /// <paramref name="cancellationToken"/> itself, which means the viewer went away, still propagates.
    /// </remarks>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="route">The route being called.</param>
    /// <param name="method">The incoming HTTP method.</param>
    /// <param name="body">The incoming request body, or <c>null</c> for a bodyless method.</param>
    /// <param name="contentType">The incoming content type, when a body is present.</param>
    /// <param name="requestHeaders">The incoming request headers, forwarded minus the deny list.</param>
    /// <param name="password">The route's decrypted password, resolved by the caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The forwarded response, or <c>null</c> when the route URL is not a valid absolute URL.</returns>
    public static async Task<ForwardResult?> ForwardAsync(
        IHttpClientFactory httpClientFactory,
        PageApiRoute route,
        string method,
        byte[]? body,
        string? contentType,
        IEnumerable<KeyValuePair<string, string>>? requestHeaders,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(route);

        if (!Uri.TryCreate(route.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (body is not null && body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
            if (!string.IsNullOrEmpty(contentType)
                && MediaTypeHeaderValue.TryParse(contentType, out var parsed))
            {
                request.Content.Headers.ContentType = parsed;
            }
        }

        if (requestHeaders is not null)
        {
            foreach (var header in requestHeaders)
            {
                if (RequestDenyList.Contains(header.Key))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (!string.IsNullOrEmpty(route.Username) || !string.IsNullOrEmpty(password))
        {
            var raw = Encoding.UTF8.GetBytes(route.Username + ":" + password);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }

        // The client's own timeout stops covering the exchange once the headers arrive, because the body
        // is read separately so it can be capped. One deadline spanning both keeps a target that trickles
        // its body from holding the request open indefinitely.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
        {
            deadline.CancelAfter(client.Timeout);
        }

        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);

            var bytes = await ReadCappedAsync(response.Content, deadline.Token).ConfigureAwait(false);
            if (bytes is null)
            {
                return ForwardResult.Failed(ForwardError.ResponseTooLarge);
            }

            var result = new ForwardResult
            {
                StatusCode = (int)response.StatusCode,
                Body = bytes,
                ContentType = response.Content.Headers.ContentType?.ToString()
            };

            Relay(response.Headers, result.Headers);
            Relay(response.Content.Headers, result.Headers);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The viewer is still waiting, so this was the deadline rather than the viewer leaving.
            return ForwardResult.Failed(ForwardError.TimedOut);
        }
        catch (Exception ex) when ((ex is HttpRequestException or IOException) && !cancellationToken.IsCancellationRequested)
        {
            return ForwardResult.Failed(ForwardError.Unreachable);
        }
    }

    /// <summary>
    /// Reports whether a target response header may be relayed to the caller.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns><c>true</c> when the header is on the allow list or is a custom header outside the deny list.</returns>
    public static bool IsRelayable(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (ResponseAllowList.Contains(name))
        {
            return true;
        }

        if (!name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) || ExtensionDenyList.Contains(name))
        {
            return false;
        }

        foreach (var prefix in ExtensionDenyPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reports whether a relayed response of this content type must be sandboxed. A browser opening the
    /// route directly would render these as a document on the Jellyfin origin, where any script in them
    /// would run with the viewer's session: HTML, XHTML, XML and SVG, plus anything with no type or one
    /// that does not parse, since a browser may sniff those. Everything else, such as a PDF, an image, or
    /// an attachment, is left alone, because a sandboxed document cannot host the browser's own viewer.
    /// </summary>
    /// <param name="contentType">The response content type, as relayed.</param>
    /// <returns><c>true</c> when the response should carry <c>Content-Security-Policy: sandbox</c>.</returns>
    public static bool NeedsSandbox(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)
            || !MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            || string.IsNullOrEmpty(parsed.MediaType))
        {
            return true;
        }

        var media = parsed.MediaType;
        return string.Equals(media, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media, "text/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media, "text/xsl", StringComparison.OrdinalIgnoreCase)
            || media.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || !media.Contains('/', StringComparison.Ordinal)
            || media.Contains('*', StringComparison.Ordinal);
    }

    // Reads a response body up to the cap. Returns null when the body is larger, checking the declared
    // length first so an honest oversized response is refused without reading any of it.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
        {
            return null;
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return buffer.ToArray();
        }
    }

    private static void Relay(HttpHeaders headers, Dictionary<string, string> into)
    {
        foreach (var header in headers)
        {
            if (IsRelayable(header.Key))
            {
                into[header.Key] = string.Join(", ", header.Value);
            }
        }
    }
}

/// <summary>Why a route could not be forwarded, when it could not.</summary>
public enum ForwardError
{
    /// <summary>The target answered and its response is relayed.</summary>
    None,

    /// <summary>The target could not be reached, or the connection failed mid response.</summary>
    Unreachable,

    /// <summary>The target did not answer within the route timeout.</summary>
    TimedOut,

    /// <summary>The target answered with more than <see cref="RouteProxy.MaxResponseBytes"/>.</summary>
    ResponseTooLarge
}

/// <summary>The outcome of forwarding a route: the target's status, body, and relayable headers.</summary>
public sealed class ForwardResult
{
    /// <summary>Gets or sets the status code returned by the target.</summary>
    public int StatusCode { get; set; }

#pragma warning disable CA1819 // The forwarded body is opaque bytes handed straight to a file result.
    /// <summary>Gets or sets the response body bytes.</summary>
    public byte[] Body { get; set; } = System.Array.Empty<byte>();
#pragma warning restore CA1819

    /// <summary>Gets or sets the response content type, when the target supplied one.</summary>
    public string? ContentType { get; set; }

    /// <summary>Gets or sets why forwarding failed, or <see cref="ForwardError.None"/> when the target answered.</summary>
    public ForwardError Error { get; set; }

    /// <summary>Gets the response headers worth relaying to the caller, limited to the relay allow list.</summary>
    public System.Collections.Generic.Dictionary<string, string> Headers { get; }
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a result for a failed forward, carrying the gateway status the caller should answer with.
    /// </summary>
    /// <param name="error">Why forwarding failed.</param>
    /// <returns>The failed result.</returns>
    public static ForwardResult Failed(ForwardError error) => new()
    {
        Error = error,
        StatusCode = error == ForwardError.TimedOut ? 504 : 502
    };
}
