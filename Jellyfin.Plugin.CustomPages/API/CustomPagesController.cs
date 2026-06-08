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

    // CSP for served page wrappers. The wrapper itself only frames the sandboxed iframe and uses inline
    // style; script-src 'unsafe-inline' is present so the author's JS can run INSIDE that iframe. srcdoc
    // frames inherit the embedder's CSP, so without it default-src 'none' silently blocks page scripts.
    // The frame is sandboxed without allow-same-origin, so its scripts run on an opaque origin that
    // cannot read the Jellyfin token, cookies, or storage. (The auth-shell path already allows this via
    // ShellCsp; this brings anonymous pages in line.)
    private const string PageCsp =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; "
        + "frame-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    // CSP for the auth shell: it runs one inline script and fetches the content same-origin, then frames it.
    private const string ShellCsp =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; "
        + "img-src 'self'; frame-src 'self' data:; frame-ancestors 'none'; base-uri 'none'";

    private readonly PageService _pages;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPagesController"/> class.
    /// </summary>
    /// <param name="pages">The page service.</param>
    public CustomPagesController(PageService pages)
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
        var page = _pages.Find(slug);
        if (page is null)
        {
            Harden(PageCsp);
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Content(_pages.NotFoundHtml(slug), "text/html");
        }

        if (!page.Visibility.RequiresAuth())
        {
            Harden(PageCsp);
            return Content(_pages.Render(page), "text/html");
        }

        Harden(ShellCsp);
        return Content(_pages.GetShellHtml(page.Slug, page.Visibility), "text/html");
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
    /// Serves a hosted image asset by name. Referenced from pages as <c>asset/{name}</c>.
    /// </summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The asset bytes, or 404 when no asset matches.</returns>
    [HttpGet("asset/{name}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Asset(string name)
    {
        var asset = _pages.FindAsset(name);
        if (asset is null)
        {
            return NotFound();
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(asset.DataBase64);
        }
        catch (FormatException)
        {
            return NotFound();
        }

        var contentType = asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? asset.ContentType
            : "application/octet-stream";

        // Assets render fine as <img>/CSS backgrounds, but a script-bearing format (e.g. SVG) opened as a
        // top-level document would otherwise run same-origin. The sandbox CSP neutralizes that without
        // affecting embedded image rendering.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
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

        Harden(PageCsp);
        return Content(_pages.Render(page), "text/html");
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

        Harden(PageCsp);
        return Content(_pages.Render(page), "text/html");
    }

    /// <summary>
    /// Applies response headers that keep served pages out of caches, indexes, and external frames.
    /// </summary>
    /// <param name="csp">The Content-Security-Policy to apply to this response.</param>
    private void Harden(string csp)
    {
        Response.Headers["Content-Security-Policy"] = csp;
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
