using System;
using System.Collections.Generic;
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

    // Response headers that describe the transport rather than the payload. Relaying them corrupts the
    // response the browser reconstructs, so they are dropped and the framework sets its own.
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length"
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

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var result = new ForwardResult
        {
            StatusCode = (int)response.StatusCode,
            Body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
            ContentType = response.Content.Headers.ContentType?.ToString()
        };

        Relay(response.Headers, result.Headers);
        Relay(response.Content.Headers, result.Headers);
        return result;
    }

    private static void Relay(HttpHeaders headers, Dictionary<string, string> into)
    {
        foreach (var header in headers)
        {
            if (HopByHop.Contains(header.Key)
                || string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            into[header.Key] = string.Join(", ", header.Value);
        }
    }
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

    /// <summary>Gets the response headers worth relaying to the caller, minus the hop by hop set.</summary>
    public System.Collections.Generic.Dictionary<string, string> Headers { get; }
        = new(System.StringComparer.OrdinalIgnoreCase);
}
