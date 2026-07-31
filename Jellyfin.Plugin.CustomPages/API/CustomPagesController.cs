using System;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
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

    private readonly IPageService _pages;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPagesController"/> class.
    /// </summary>
    /// <param name="pages">The page service.</param>
    public CustomPagesController(IPageService pages)
    {
        _pages = pages;
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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult UserContent(string slug)
    {
        var page = _pages.Find(slug);
        if (page is null || page.Visibility != PageVisibility.User)
        {
            return NotFound();
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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult AdminContent(string slug)
    {
        var page = _pages.Find(slug);
        if (page is null || page.Visibility != PageVisibility.Admin)
        {
            return NotFound();
        }

        Harden();
        return Content(_pages.Render(page), HtmlContentType);
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
