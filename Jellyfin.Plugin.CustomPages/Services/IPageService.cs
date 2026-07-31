using Jellyfin.Plugin.CustomPages.Models;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Resolves slugs and assets to configured entries and renders the HTML served for them. Exists so the
/// controller, which is where the authorization tiers are actually enforced, can be tested without a
/// running plugin instance.
/// </summary>
public interface IPageService
{
    /// <summary>
    /// Resolves the web client's favicon so served pages can reuse the server's real icon.
    /// </summary>
    /// <returns>The favicon bytes and content type, or <c>null</c> when none could be located.</returns>
    (byte[] Bytes, string ContentType)? GetFavicon();

    /// <summary>
    /// Finds an enabled page by slug, case-insensitively.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The matching page, or <c>null</c> when none is enabled for the slug.</returns>
    CustomPage? Find(string slug);

    /// <summary>
    /// Renders a page for serving, wrapped in the sandboxed iframe that isolates author content.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <returns>The full HTML document to serve.</returns>
    string Render(CustomPage page);

    /// <summary>
    /// Renders the authentication shell for a protected page.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <param name="visibility">The page visibility tier.</param>
    /// <returns>The shell HTML document.</returns>
    string GetShellHtml(string slug, PageVisibility visibility);

    /// <summary>
    /// Renders the page returned when a slug has no enabled page.
    /// </summary>
    /// <param name="slug">The requested slug.</param>
    /// <returns>A standalone 404 HTML document.</returns>
    string NotFoundHtml(string slug);

    /// <summary>
    /// Finds a hosted asset by name, case-insensitively.
    /// </summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The matching asset, or <c>null</c> when none exists for the name.</returns>
    PageAsset? FindAsset(string name);

    /// <summary>
    /// Decodes an asset's bytes.
    /// </summary>
    /// <param name="asset">The asset to decode.</param>
    /// <returns>The decoded bytes, or <c>null</c> when the stored Base64 is missing or invalid.</returns>
    byte[]? GetAssetBytes(PageAsset asset);
}
