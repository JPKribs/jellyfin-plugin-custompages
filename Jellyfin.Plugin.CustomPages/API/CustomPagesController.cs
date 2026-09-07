using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CustomPages.Api;

/// <summary>
/// Serves custom pages at <c>/pages/{slug}</c>, gated by each page's visibility tier.
/// </summary>
[ApiController]
[Route("pages")]
public class CustomPagesController : ControllerBase
{
    private const string AdminPolicy = "RequiresElevation";

    private const string HtmlContentType = "text/html; charset=utf-8";

    // ONE policy for every served page. The anonymous path and the auth shell must not diverge: a
    // srcdoc frame inherits its embedder's policy, so whatever is withheld here is withheld from author
    // content, and two policies would make the same page behave differently depending on its tier.
    //
    // The boundary that actually protects the viewer is the sandboxed, opaque-origin iframe in
    // PageService.Render, not this header. Content that escaped that sandbox could exfiltrate by simply
    // navigating, which no CSP directive governs, so tightening the fetch directives buys almost no
    // security while silently breaking ordinary pages (CDN scripts, web fonts, embeds, form posts).
    // The fetch directives are therefore permissive, and the directives that protect the framing
    // document itself stay locked down:
    //   object-src 'none'    - no legacy plugin content.
    //   base-uri 'none'      - a <base> tag cannot repoint the wrapper's relative URLs, which is what
    //                          keeps author `asset/{name}` references resolving to /pages/asset/{name}.
    //   frame-ancestors 'self' - embeddable by the Jellyfin origin (a dashboard or another page), not
    //                          by third-party sites.
    // form-action is deliberately unset, so pages may post forms; it is omitted rather than set to a
    // value so both paths agree by construction.
    private const string PageCsp =
        "default-src * data: blob: 'unsafe-inline' 'unsafe-eval'; "
        + "object-src 'none'; base-uri 'none'; frame-ancestors 'self'";

    // Store responses are serialized here rather than through the server's MVC options, so a page
    // author gets the same camel cased shape no matter how the host has its own API configured.
    private static readonly JsonSerializerOptions StoreResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IPageService _pages;
    private readonly IStoreService _stores;
    private readonly IAuthorizationContext _authorization;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPagesController"/> class.
    /// </summary>
    /// <param name="pages">The page service.</param>
    /// <param name="stores">The record store service.</param>
    /// <param name="authorization">Resolves the calling user, for pages restricted to named users.</param>
    /// <param name="httpClientFactory">The HTTP client factory, used to forward server side routes.</param>
    public CustomPagesController(
        IPageService pages,
        IStoreService stores,
        IAuthorizationContext authorization,
        IHttpClientFactory httpClientFactory)
    {
        _pages = pages;
        _stores = stores;
        _authorization = authorization;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Entry point for a page. Anonymous pages render directly; protected pages render an auth shell
    /// that re-fetches the content with the signed-in user's token.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The page or shell HTML.</returns>
    [HttpGet("{slug}")]
    [AllowAnonymous]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ContentResult Get(string slug)
    {
        Harden();

        var page = _pages.Find(slug);
        if (page is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Content(_pages.NotFoundHtml(slug), HtmlContentType);
        }

        if (!page.Visibility.RequiresAuth())
        {
            return Content(_pages.Render(page), HtmlContentType);
        }

        return Content(_pages.GetShellHtml(page.Slug, page.Visibility), HtmlContentType);
    }

    /// <summary>
    /// Serves the web client's favicon so pages can reuse the server's real icon at a stable path.
    /// </summary>
    /// <returns>The favicon bytes, or 404 when none could be located.</returns>
    [HttpGet("favicon.ico")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Favicon()
    {
        var favicon = _pages.GetFavicon();
        if (favicon is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(favicon.Value.Bytes, favicon.Value.ContentType);
    }

    /// <summary>
    /// Serves a hosted anonymous image asset by name. Referenced from pages as <c>asset/{name}</c>.
    /// </summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The asset bytes, or 404 when no anonymous asset matches.</returns>
    [HttpGet("asset/{name}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Asset(string name)
    {
        // Browsers fetch images without the viewer's token, so this endpoint can only ever serve
        // anonymous assets. Gated assets are embedded into their pages as data: URIs on render and
        // return 404 here so the public endpoint does not disclose which gated names exist.
        var asset = _pages.FindAsset(name);
        if (asset is null || asset.Visibility.RequiresAuth())
        {
            return NotFound();
        }

        var bytes = _pages.GetAssetBytes(asset);
        if (bytes is null)
        {
            return NotFound();
        }

        var contentType = asset.ContentType is not null
            && asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? asset.ContentType
                : "application/octet-stream";

        // Assets render fine as <img>/CSS backgrounds, but a script-bearing format (e.g. SVG) opened as a
        // top-level document would otherwise run same-origin. The sandbox CSP neutralizes that without
        // affecting embedded image rendering.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Public assets are cached by browsers and by any shared cache in front of the server. Raising an
        // asset's tier therefore does not retract copies already handed out: expect it to stay fetchable
        // for up to this window. Anything that must be retracted immediately should be deleted, not
        // re-tiered.
        Response.Headers["Cache-Control"] = "public, max-age=300";
        return File(bytes, contentType);
    }

    /// <summary>
    /// Returns the rendered content of a user-tier page to any signed-in user.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The page HTML.</returns>
    [HttpGet("{slug}/user")]
    [Authorize]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UserContent(string slug)
    {
        var page = _pages.Find(slug);
        if (page is null || page.Visibility != PageVisibility.User)
        {
            return NotFound();
        }

        if (!await AllowsCallerAsync(page).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Harden();
        return Content(_pages.Render(page), HtmlContentType);
    }

    /// <summary>
    /// Returns the rendered content of an admin-tier page to administrators.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The page HTML.</returns>
    [HttpGet("{slug}/admin")]
    [Authorize(Policy = AdminPolicy)]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AdminContent(string slug)
    {
        var page = _pages.Find(slug);
        if (page is null || page.Visibility != PageVisibility.Admin)
        {
            return NotFound();
        }

        if (!await AllowsCallerAsync(page).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Harden();
        return Content(_pages.Render(page), HtmlContentType);
    }

    /// <summary>
    /// Resolves the calling user and asks whether a page restricted to named users admits them.
    /// </summary>
    /// <remarks>
    /// The tier check above is the first gate and this is the second. Both run before
    /// <see cref="IPageService.Render"/>, so a refused viewer is never sent the page body. That
    /// ordering is the whole guarantee for a page whose content is worth restricting: the bytes are
    /// only ever composed for a request that already passed both gates. A failure to resolve the
    /// caller is treated as no user, which a restricted page refuses.
    ///
    /// An administrator passes the list unconditionally, which is why the dashboard picker does not
    /// offer them. An API key still does not, since it carries no user an allow list could name.
    /// </remarks>
    /// <param name="page">The page being requested.</param>
    /// <returns><c>true</c> when the caller may view the page.</returns>
    private async Task<bool> AllowsCallerAsync(CustomPage page)
    {
        if (page.AllowedUserIds is null || page.AllowedUserIds.Count == 0)
        {
            return true;
        }

        var info = await _authorization.GetAuthorizationInfo(Request).ConfigureAwait(false);

        // An API key authenticates the caller without identifying a user, so it carries no identity
        // an allow list could name. AllowsUser refuses Guid.Empty, which is what UserId reports here.
        return PageService.AllowsUser(
            page,
            info.IsApiKey ? Guid.Empty : info.UserId,
            !info.IsApiKey && info.User is not null && info.User.HasPermission(PermissionKind.IsAdministrator));
    }

    /// <summary>
    /// Forwards a page's named server side route to its configured target. The page calls this through the
    /// injected <c>serverFetch</c> helper, so a browser never faces the target directly and the target's
    /// credentials stay on the server.
    /// </summary>
    /// <remarks>
    /// The route target is fixed in configuration and never comes from the caller, so this is not an open
    /// proxy: a page can only reach the resources an administrator wired up for it. Access is gated to
    /// exactly the audience of the page the route belongs to, checked before anything is forwarded.
    /// </remarks>
    /// <param name="slug">The page slug.</param>
    /// <param name="name">The route name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The target's response, or 403/404 when the caller or route is not permitted.</returns>
    [HttpGet("{slug}/api/{name}")]
    [HttpPost("{slug}/api/{name}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApiRoute(string slug, string name, CancellationToken cancellationToken)
    {
        var page = _pages.Find(slug);
        var route = page?.ApiRoutes?.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (page is null || route is null)
        {
            return NotFound();
        }

        var access = await CheckPageAccessAsync(page).ConfigureAwait(false);
        if (access != StatusCodes.Status200OK)
        {
            return StatusCode(access);
        }

        byte[]? body = null;
        if (Request.ContentLength is > 0 || HttpMethods.IsPost(Request.Method))
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            body = buffer.ToArray();
        }

        var requestHeaders = Request.Headers
            .Select(h => new KeyValuePair<string, string>(h.Key, h.Value.ToString()));

        var forwarded = await RouteProxy.ForwardAsync(
            _httpClientFactory,
            route,
            Request.Method,
            body,
            Request.ContentType,
            requestHeaders,
            Plugin.Instance?.Secrets.Unprotect(route.Password) ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        if (forwarded is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        Response.Headers["Cache-Control"] = "no-store";
        foreach (var header in forwarded.Headers)
        {
            Response.Headers[header.Key] = header.Value;
        }

        Response.StatusCode = forwarded.StatusCode;
        return File(forwarded.Body, forwarded.ContentType ?? "application/octet-stream");
    }

    /// <summary>
    /// Returns the records in a store the caller is allowed to see, newest first.
    /// </summary>
    /// <remarks>
    /// Stores are addressed by name rather than through a page, so which page is calling has no bearing
    /// on what comes back. The store's own read tier and read scope decide everything, which is what
    /// lets a page that collects submissions and a page that reports on them work against the same
    /// records without one of them silently widening the other's audience.
    /// </remarks>
    /// <param name="name">The store name.</param>
    /// <param name="id">An optional single record ID to fetch instead of the whole store.</param>
    /// <returns>The records, or the status refusing the caller.</returns>
    [HttpGet("store/{name}/read")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StoreRead(string name, [FromQuery] string? id)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        var caller = await ResolveStoreCallerAsync().ConfigureAwait(false);
        var level = StoreAccess.ResolveRead(store, caller);
        if (level == StoreAccessLevel.None)
        {
            return Refuse(caller);
        }

        var owner = StoreAccess.OwnerFilter(level, caller);
        NoStore();

        if (!string.IsNullOrEmpty(id))
        {
            var single = _stores.ReadOne(store, id, owner);
            return single is null ? NotFound() : Json(single);
        }

        var records = _stores.Read(store, owner);
        return Json(new StoreReadResponse
        {
            Records = records,
            Count = records.Count,
            Limit = StoreService.EffectiveMaxRecords(store),
            Scope = level == StoreAccessLevel.All ? "all" : "own"
        });
    }

    /// <summary>
    /// Creates a record, or replaces the payload of one the caller may modify.
    /// </summary>
    /// <remarks>
    /// A body carrying no <c>id</c> creates a record and the server assigns its ID, timestamps, and
    /// owner. A body carrying an <c>id</c> replaces that record's payload and leaves the rest alone, so
    /// an administrator writing a status onto a submission never takes ownership of it away from the
    /// person who submitted it.
    /// </remarks>
    /// <param name="name">The store name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored record, or the status refusing the write.</returns>
    [HttpPost("store/{name}/write")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StoreWrite(string name, CancellationToken cancellationToken)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        var caller = await ResolveStoreCallerAsync().ConfigureAwait(false);
        if (!StoreAccess.CanCreate(store, caller))
        {
            return Refuse(caller);
        }

        var (body, tooLarge) = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);
        if (tooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        JsonNode? envelope;
        try
        {
            envelope = body.Length == 0 ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var recordId = ReadStringProperty(envelope, "id");
        var data = ReadProperty(envelope, "data");

        // The record's primary element. It only ever names the record in the activity log, so it is
        // never interpreted and the store caps and flattens it before it reaches an administrator.
        var label = ReadStringProperty(envelope, "label");

        NoStore();

        if (string.IsNullOrEmpty(recordId))
        {
            var created = _stores.Create(store, data, label, caller.UserId);
            return StoreResult(created);
        }

        // Updating an existing record is a different grant from creating one, so it is resolved
        // separately rather than inferred from the caller having got this far.
        var modify = StoreAccess.ResolveModify(store, caller);
        if (modify == StoreAccessLevel.None)
        {
            return Refuse(caller);
        }

        var updated = _stores.Update(store, recordId, data, label, StoreAccess.OwnerFilter(modify, caller));
        return StoreResult(updated);
    }

    /// <summary>
    /// Deletes one record the caller may modify.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204 when a record was deleted, otherwise the refusing status.</returns>
    [HttpPost("store/{name}/delete")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StoreDelete(string name, CancellationToken cancellationToken)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        var caller = await ResolveStoreCallerAsync().ConfigureAwait(false);
        var modify = StoreAccess.ResolveModify(store, caller);
        if (modify == StoreAccessLevel.None)
        {
            return Refuse(caller);
        }

        var (body, tooLarge) = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);
        if (tooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        JsonNode? envelope;
        try
        {
            envelope = body.Length == 0 ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var recordId = ReadStringProperty(envelope, "id");
        if (string.IsNullOrEmpty(recordId))
        {
            return BadRequest();
        }

        NoStore();
        return _stores.Delete(store, recordId, StoreAccess.OwnerFilter(modify, caller))
            ? NoContent()
            : NotFound();
    }

    /// <summary>
    /// Reports how many records a store holds, for the dashboard. Administrators only, so a page can
    /// never use it to learn the size of a store it cannot read.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <returns>The record count and the store's limit.</returns>
    [HttpGet("store/{name}/stats")]
    [Authorize(Policy = AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StoreStats(string name)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        NoStore();
        return Json(new StoreStatsResponse
        {
            Count = _stores.Count(store),
            Limit = StoreService.EffectiveMaxRecords(store)
        });
    }

    /// <summary>
    /// Deletes every record in a store. Administrators only, and never reachable from a page's helper,
    /// since emptying a store is an administrative act rather than something page script should do.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <returns>The number of records removed.</returns>
    [HttpPost("store/{name}/clear")]
    [Authorize(Policy = AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StoreClear(string name)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        NoStore();
        return Json(new StoreClearResponse { Removed = _stores.Clear(store) });
    }

    /// <summary>
    /// Resolves the identity behind a store request.
    /// </summary>
    /// <remarks>
    /// An API key is treated as the administrator tier with no user identity, which is deliberately
    /// different from how the page endpoints treat one. A Jellyfin API key is issued by an
    /// administrator and already carries administrator reach, and a store's whole point is that
    /// something outside the browser can pick work up and write results back. Carrying no user means it
    /// can never own a record, so the Own read scope and the edit-own grant never apply to it. An API
    /// key therefore sees a store exactly as an administrator does, and nothing narrower is silently
    /// widened for it.
    /// </remarks>
    /// <returns>The resolved caller.</returns>
    private async Task<StoreCaller> ResolveStoreCallerAsync()
    {
        AuthorizationInfo info;
        try
        {
            info = await _authorization.GetAuthorizationInfo(Request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or SecurityException)
        {
            // A malformed or revoked token is not a credential, so fall back to the anonymous tier
            // rather than failing the request outright. An anonymous store still answers it.
            return StoreCaller.Anonymous;
        }

        if (info.IsApiKey)
        {
            return new StoreCaller(PageVisibility.Admin, Guid.Empty);
        }

        if (info.User is null || info.UserId == Guid.Empty)
        {
            return StoreCaller.Anonymous;
        }

        var tier = info.User.HasPermission(PermissionKind.IsAdministrator)
            ? PageVisibility.Admin
            : PageVisibility.User;

        return new StoreCaller(tier, info.UserId);
    }

    /// <summary>
    /// Reads the request body, refusing anything past what a single record may carry so an oversized
    /// payload is turned away before it is parsed rather than after.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The body bytes, and whether the limit was exceeded.</returns>
    private async Task<(byte[] Body, bool TooLarge)> ReadBodyAsync(CancellationToken cancellationToken)
    {
        // The record cap governs the payload; the rest of the allowance covers the envelope around it.
        var limit = StoreService.MaxRecordBytes + 4096;
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return (Array.Empty<byte>(), true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return (buffer.ToArray(), false);
    }

    /// <summary>
    /// Maps a store write outcome onto the response the caller sees.
    /// </summary>
    /// <param name="outcome">The store's outcome.</param>
    /// <returns>The response.</returns>
    private IActionResult StoreResult((StoreWriteResult Result, StoreRecord? Record) outcome) => outcome.Result switch
    {
        StoreWriteResult.Ok when outcome.Record is not null => Json(outcome.Record),
        StoreWriteResult.NotFound => NotFound(),
        StoreWriteResult.Full => StatusCode(StatusCodes.Status409Conflict),
        StoreWriteResult.TooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    /// <summary>
    /// Returns the status for a refused store request. A caller with no credentials is told to sign in,
    /// and one that is signed in and still short of the tier is refused outright.
    /// </summary>
    /// <param name="caller">The resolved caller.</param>
    /// <returns>401 or 403.</returns>
    private StatusCodeResult Refuse(StoreCaller caller) => StatusCode(
        caller.Tier == PageVisibility.Anonymous
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden);

    /// <summary>
    /// Serializes a store response with the plugin's own options, so a page author sees the same camel
    /// cased shape regardless of how the server has its own API serialization configured.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The JSON response.</returns>
    private ContentResult Json(object value)
        => Content(JsonSerializer.Serialize(value, StoreResponseJson), "application/json; charset=utf-8");

    /// <summary>
    /// Keeps store responses out of caches. Records change constantly and are frequently gated, so a
    /// shared cache in front of the server must never hold one.
    /// </summary>
    private void NoStore()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static JsonNode? ReadProperty(JsonNode? envelope, string name)
    {
        if (envelope is not JsonObject obj)
        {
            return null;
        }

        foreach (var property in obj)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadStringProperty(JsonNode? envelope, string name)
        => ReadProperty(envelope, name) is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    /// <summary>
    /// Reports whether the caller may reach the audience of a page, mirroring the tier and allow list the
    /// content endpoints enforce. Returns <see cref="StatusCodes.Status200OK"/> when permitted, otherwise
    /// the status to send back.
    /// </summary>
    /// <param name="page">The page whose audience gates the route.</param>
    /// <returns>200 when permitted, 401 when a signed in user is required, or 403 when refused.</returns>
    private async Task<int> CheckPageAccessAsync(CustomPage page)
    {
        if (!page.Visibility.RequiresAuth())
        {
            return page.AllowedUserIds is { Count: > 0 }
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status200OK;
        }

        var info = await _authorization.GetAuthorizationInfo(Request).ConfigureAwait(false);
        if (info.IsApiKey || info.User is null || info.UserId == Guid.Empty)
        {
            return StatusCodes.Status401Unauthorized;
        }

        var isAdministrator = info.User.HasPermission(PermissionKind.IsAdministrator);
        if (page.Visibility == PageVisibility.Admin && !isAdministrator)
        {
            return StatusCodes.Status403Forbidden;
        }

        return PageService.AllowsUser(page, info.UserId, isAdministrator)
            ? StatusCodes.Status200OK
            : StatusCodes.Status403Forbidden;
    }

    /// <summary>
    /// Applies the response headers that keep served pages out of caches, indexes, and third-party frames.
    /// </summary>
    private void Harden()
    {
        Response.Headers["Content-Security-Policy"] = PageCsp;
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Legacy counterpart to frame-ancestors 'self'; browsers that honour both prefer the CSP.
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
