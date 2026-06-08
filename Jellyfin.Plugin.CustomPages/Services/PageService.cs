using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.CustomPages.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Resolves slugs to configured pages and renders their served HTML from embedded templates.
/// </summary>
public partial class PageService
{
    private readonly IServerApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageService"/> class.
    /// </summary>
    /// <param name="paths">The server application paths, used to locate the web client favicon.</param>
    public PageService(IServerApplicationPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Resolves the web client's favicon bytes so served pages can reuse the server's real icon.
    /// </summary>
    /// <returns>The favicon bytes and content type, or <c>null</c> when none could be located.</returns>
    public (byte[] Bytes, string ContentType)? GetFavicon() => FaviconResolver.Resolve(_paths);

    /// <summary>
    /// Finds an enabled page by slug, case-insensitively. Rejects slugs outside the safe character set.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The matching page, or <c>null</c> when none is enabled for the slug.</returns>
    public CustomPage? Find(string slug)
    {
        if (!IsValidSlug(slug) || Plugin.Instance is null)
        {
            return null;
        }

        return Plugin.Instance.ReadConfiguration(config =>
            config.Pages.FirstOrDefault(p =>
                p.Enabled && string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Renders a page for serving: the author's content runs inside a sandboxed, opaque-origin iframe
    /// so it cannot read the Jellyfin origin's token, cookies, or storage.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <returns>The full HTML document to serve.</returns>
    public string Render(CustomPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return TemplateLoader.Fill("custompages_wrapper", new Dictionary<string, string>
        {
            ["TITLE"] = WebUtility.HtmlEncode(page.Title),
            ["SRCDOC"] = WebUtility.HtmlEncode(BuildInnerDocument(page))
        });
    }

    /// <summary>
    /// Renders the authentication shell for a protected page, with the slug and tier baked in.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <param name="visibility">The page visibility tier.</param>
    /// <returns>The shell HTML document.</returns>
    public string GetShellHtml(string slug, PageVisibility visibility)
    {
        var tier = visibility.EndpointTier();
        var content = TemplateLoader.Fill("custompages_shell", new Dictionary<string, string>
        {
            ["SLUG"] = EscapeJsString(slug),
            ["TIER"] = EscapeJsString(tier)
        });

        return TemplateLoader.Fill("status", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TITLE"] = "Loading…",
            ["HEADING"] = "Loading…",
            ["MESSAGE"] = string.Empty,
            ["SPINNER"] = "<div class=\"jpk-spinner\" role=\"status\" aria-label=\"Loading\"></div>",
            ["BUTTON"] = string.Empty,
            ["CONTENT"] = content
        });
    }

    /// <summary>
    /// Renders the Jellyfin-styled page returned when a slug has no enabled page.
    /// </summary>
    /// <param name="slug">The requested slug.</param>
    /// <returns>A standalone 404 HTML document.</returns>
    public string NotFoundHtml(string slug)
    {
        var where = IsValidSlug(slug) ? "/pages/" + slug : "this address";
        return StatusPage.Render(
            "Page not found",
            "There's nothing published at " + where + ".",
            buttonText: "Back to Jellyfin",
            buttonHref: "../web/");
    }

    /// <summary>
    /// Reports whether a slug consists only of the safe URL character set.
    /// </summary>
    /// <param name="slug">The slug to test.</param>
    /// <returns><c>true</c> when the slug is non-empty and matches <c>[a-z0-9_-]+</c>.</returns>
    public static bool IsValidSlug(string? slug)
        => !string.IsNullOrEmpty(slug) && SlugPattern().IsMatch(slug);

    /// <summary>
    /// Finds a hosted asset by name, case-insensitively. Rejects names outside the safe character set.
    /// </summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The matching asset, or <c>null</c> when none exists for the name.</returns>
    public PageAsset? FindAsset(string name)
    {
        if (!IsValidAssetName(name) || Plugin.Instance is null)
        {
            return null;
        }

        return Plugin.Instance.ReadConfiguration(config =>
            config.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Reports whether an asset name is a single safe file name (no path segments).
    /// </summary>
    /// <param name="name">The asset name to test.</param>
    /// <returns><c>true</c> when the name matches <c>[a-z0-9._-]+</c> and is not a dot-only name.</returns>
    public static bool IsValidAssetName(string? name)
        => !string.IsNullOrEmpty(name) && name != "." && name != ".." && AssetNamePattern().IsMatch(name);

    private string BuildInnerDocument(CustomPage page)
    {
        if (page.SingleFile)
        {
            return page.Document ?? string.Empty;
        }

        return TemplateLoader.Fill("custompages_inner", new Dictionary<string, string>
        {
            ["TITLE"] = WebUtility.HtmlEncode(page.Title),
            ["CSS"] = page.Css ?? string.Empty,
            ["BODY"] = page.Html ?? string.Empty,
            ["JS"] = page.Js ?? string.Empty
        });
    }

    private static string EscapeJsString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '"': sb.Append("\\\""); break;
                case '<': sb.Append("\\x3C"); break;
                case '>': sb.Append("\\x3E"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex AssetNamePattern();
}
