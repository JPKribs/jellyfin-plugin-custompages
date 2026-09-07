using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Resolves slugs to configured pages and renders their served HTML from embedded templates.
/// </summary>
public partial class PageService : IPageService
{
    // The iframe attribute applied to every page that has not opted out. allow-same-origin is
    // deliberately absent, which is what holds the frame on an opaque origin and away from the
    // viewer's Jellyfin session. The leading space belongs to the attribute so the opt out can
    // substitute an empty string.
    private const string SandboxAttribute =
        " sandbox=\"allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox\"";

    private readonly IServerApplicationPaths _paths;
    private readonly Func<PluginConfiguration?> _configuration;

    // Decoded asset bytes, keyed by the asset instance rather than by name. Jellyfin replaces the whole
    // configuration object on save, so an entry becomes unreachable together with the configuration that
    // produced it and the table drops it. That removes the generation bookkeeping this used to need, and
    // with it the window where an asset resolved just before a save could publish its bytes under a name
    // the new configuration had already reused.
    private readonly ConditionalWeakTable<PageAsset, byte[]> _assetBytesCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PageService"/> class.
    /// </summary>
    /// <param name="paths">The server application paths, used to locate the web client favicon.</param>
    public PageService(IServerApplicationPaths paths)
        : this(paths, static () => Plugin.Instance?.ReadConfiguration(static config => config))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PageService"/> class with an explicit configuration
    /// source, so the resolution and rendering paths can be exercised without a running plugin instance.
    /// </summary>
    /// <param name="paths">The server application paths, used to locate the web client favicon.</param>
    /// <param name="configuration">Returns the current configuration, or <c>null</c> when unavailable.</param>
    public PageService(IServerApplicationPaths paths, Func<PluginConfiguration?> configuration)
    {
        _paths = paths;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public (byte[] Bytes, string ContentType)? GetFavicon() => FaviconResolver.Resolve(_paths);

    /// <summary>
    /// Finds an enabled page by slug, case-insensitively. Rejects slugs outside the safe character set.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The matching page, or <c>null</c> when none is enabled for the slug.</returns>
    public CustomPage? Find(string slug)
    {
        if (!IsValidSlug(slug))
        {
            return null;
        }

        return _configuration()?.Pages.FirstOrDefault(p =>
            p.Enabled && string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Renders a page for serving. The author's content runs inside a sandboxed, opaque origin iframe
    /// so it cannot read the Jellyfin origin's token, cookies, or storage, unless the page opts out.
    /// </summary>
    /// <remarks>
    /// SECURITY INVARIANT. Author markup must never reach the top level document in live form. The auth
    /// shell document.writes this output into a document that is same origin with Jellyfin and whose CSP
    /// allows inline script, so author content may only ever appear HTML encoded inside the iframe's
    /// srcdoc attribute. That encoding holds for every page, opted out or not, so never serve author
    /// content without this wrapper.
    ///
    /// The sandbox attribute is the second barrier and it is the one <see cref="CustomPage.Unsandboxed"/>
    /// removes. An unsandboxed page runs on the Jellyfin origin, so its script can read the viewer's
    /// access token out of local storage and call the server API as that viewer. It is meant only for
    /// pages whose source the administrator wrote and controls. The opt out drops the attribute outright
    /// rather than adding allow-same-origin to it, because a frame holding allow-same-origin and
    /// allow-scripts together can reach into the parent document and strip its own sandbox anyway.
    /// </remarks>
    /// <param name="page">The page to render.</param>
    /// <returns>The full HTML document to serve.</returns>
    public string Render(CustomPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var inner = BuildInnerDocument(page);
        if (page.ApiRoutes is { Count: > 0 })
        {
            inner = InjectServerFetch(inner, page.Slug);
        }

        var assets = _configuration()?.Assets;
        if (assets is not null)
        {
            inner = InlineGatedAssets(inner, page.Visibility, assets);
        }

        return TemplateLoader.Fill("custompages_wrapper", new Dictionary<string, string>
        {
            ["TITLE"] = WebUtility.HtmlEncode(page.Title),
            ["SANDBOX"] = RunsUnsandboxed(page) ? string.Empty : SandboxAttribute,
            ["SRCDOC"] = WebUtility.HtmlEncode(inner)
        });
    }

    /// <summary>
    /// Replaces <c>asset/{name}</c> references to gated assets with <c>data:</c> URIs. Image fetches
    /// cannot carry the viewer's token, so gated assets are never served by URL. Embedding them into
    /// the rendered document lets the bytes travel inside a response that is already tier gated. Only
    /// image assets at or below the page's own tier are embedded, so a page can never expose an asset
    /// its viewers are not cleared for.
    /// </summary>
    /// <param name="document">The inner page document.</param>
    /// <param name="pageVisibility">The tier of the page being rendered.</param>
    /// <param name="assets">The candidate assets. Anonymous assets are ignored.</param>
    /// <returns>The document with eligible references embedded.</returns>
    public static string InlineGatedAssets(string document, PageVisibility pageVisibility, IEnumerable<PageAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assets);

        // Save time validation rejects out of range tiers, but a hand edited XML config never passes
        // through it. An undefined value would compare as covering every tier, so embed nothing rather
        // than let it unlock admin assets on a page the server will happily serve to any signed in user.
        if (!Enum.IsDefined(pageVisibility))
        {
            return document;
        }

        foreach (var asset in assets)
        {
            if (!Enum.IsDefined(asset.Visibility)
                || !asset.Visibility.RequiresAuth()
                || !pageVisibility.Covers(asset.Visibility)
                || !IsValidAssetName(asset.Name)
                || asset.ContentType is null
                || !asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(asset.DataBase64);
            }
            catch (FormatException)
            {
                continue;
            }
            catch (ArgumentNullException)
            {
                continue;
            }

            var dataUri = "data:" + asset.ContentType + ";base64," + Convert.ToBase64String(bytes);
            document = document.Replace("asset/" + asset.Name, dataUri, StringComparison.OrdinalIgnoreCase);
        }

        return document;
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
    /// Reports whether a user may view a page, on top of the tier check the endpoint already made.
    /// An empty allow list admits everyone the tier admits, and a non-empty one admits only its members.
    /// </summary>
    /// <remarks>
    /// This fails closed on purpose. A list that holds only unparseable entries admits nobody rather
    /// than collapsing to the empty list and readmitting every user, which is the direction a hand
    /// edited configuration is most likely to break in. <see cref="Guid.Empty"/> is never a member,
    /// so an API key caller, which carries no user, is refused by any restricted page.
    /// </remarks>
    /// <param name="page">The page being requested.</param>
    /// <param name="userId">The calling user's ID.</param>
    /// <returns><c>true</c> when the user may view the page.</returns>
    public static bool AllowsUser(CustomPage page, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(page);

        var allowed = page.AllowedUserIds;
        if (allowed is null || allowed.Count == 0)
        {
            return true;
        }

        if (userId == Guid.Empty)
        {
            return false;
        }

        foreach (var entry in allowed)
        {
            if (Guid.TryParse(entry, out var parsed) && parsed == userId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds a hosted asset by name, case-insensitively. Rejects names outside the safe character set.
    /// </summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The matching asset, or <c>null</c> when none exists for the name.</returns>
    public PageAsset? FindAsset(string name)
    {
        if (!IsValidAssetName(name))
        {
            return null;
        }

        return _configuration()?.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Decodes an asset's bytes, cached so repeated requests do not re-decode Base64 configuration data.
    /// </summary>
    /// <param name="asset">The asset to decode.</param>
    /// <returns>The decoded bytes, or <c>null</c> when the stored Base64 is missing or invalid.</returns>
    public byte[]? GetAssetBytes(PageAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (_assetBytesCache.TryGetValue(asset, out var cached))
        {
            return cached;
        }

        if (string.IsNullOrEmpty(asset.DataBase64))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(asset.DataBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        _assetBytesCache.AddOrUpdate(asset, bytes);
        return bytes;
    }

    /// <summary>
    /// Reports whether an asset name is a single safe file name (no path segments).
    /// </summary>
    /// <param name="name">The asset name to test.</param>
    /// <returns><c>true</c> when the name matches <c>[a-z0-9._-]+</c> and is not a dot-only name.</returns>
    public static bool IsValidAssetName(string? name)
        => !string.IsNullOrEmpty(name) && name != "." && name != ".." && AssetNamePattern().IsMatch(name);

    /// <summary>
    /// Reports whether a page is served without the isolating sandbox, either because it asked for
    /// system access or because it defines server side routes.
    /// </summary>
    /// <remarks>
    /// Routes imply it rather than requiring the administrator to tick a second box. The injected helper
    /// reads the viewer's token from same origin storage and calls the route on the Jellyfin origin, and
    /// neither is possible from an opaque origin frame, so a sandboxed page with routes could only ever
    /// fail silently.
    /// </remarks>
    /// <param name="page">The page being rendered.</param>
    /// <returns><c>true</c> when the sandbox attribute is omitted.</returns>
    public static bool RunsUnsandboxed(CustomPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.Unsandboxed || page.ApiRoutes is { Count: > 0 };
    }

    /// <summary>
    /// Injects the <c>serverFetch(name, init)</c> helper into a page that defines server side routes, so
    /// author script can call a named route without knowing the page slug or handling the viewer's token.
    /// </summary>
    /// <remarks>
    /// The helper reads the viewer's Jellyfin token from same origin storage and calls the route on the
    /// Jellyfin origin, so the page must run with <see cref="CustomPage.Unsandboxed"/> access for it to
    /// reach the server. The script is inserted as early as possible so it is defined before any author
    /// script that uses it.
    /// </remarks>
    /// <param name="document">The inner page document.</param>
    /// <param name="slug">The page slug, baked into the route base.</param>
    /// <returns>The document with the helper injected.</returns>
    public static string InjectServerFetch(string document, string slug)
    {
        ArgumentNullException.ThrowIfNull(document);

        var prelude =
            "<script>(function(){var base=\"/pages/\"+\"" + EscapeJsString(slug) + "\"+\"/api/\";"
            + "window.serverFetch=function(name,init){init=init||{};var h=init.headers||{};"
            + "try{var raw=localStorage.getItem(\"jellyfin_credentials\");if(raw){var s=(JSON.parse(raw)||{}).Servers||[];"
            + "for(var i=0;i<s.length;i++){if(s[i]&&s[i].AccessToken){h[\"Authorization\"]=\"MediaBrowser Token=\\\"\"+s[i].AccessToken+\"\\\"\";break;}}}}catch(e){}"
            + "init.headers=h;return fetch(base+encodeURIComponent(name),init);};})();</script>";

        // Prefer to land the helper right after the opening head so it runs before any author script.
        // Fall back to the start of the body, then to the front of the document, so a fragment or a
        // hand written document without a head still gets it.
        var headIndex = document.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var insertAt = headIndex + "<head>".Length;
            return document.Insert(insertAt, prelude);
        }

        var bodyIndex = document.IndexOf("<body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex >= 0)
        {
            var insertAt = bodyIndex + "<body>".Length;
            return document.Insert(insertAt, prelude);
        }

        return prelude + document;
    }

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
