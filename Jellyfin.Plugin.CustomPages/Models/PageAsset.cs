namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// A binary asset (typically an image) hosted by the plugin and referenced from pages as <c>asset/{name}</c>.
/// </summary>
public class PageAsset
{
    /// <summary>Gets or sets the asset file name used in its URL, e.g. <c>logo.png</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the MIME content type, e.g. <c>image/png</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the asset bytes, Base64-encoded.</summary>
    public string DataBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authorization tier required to view the asset. Anonymous assets are served at
    /// <c>/pages/asset/{name}</c>. Gated assets are never served by URL because image fetches cannot
    /// carry the viewer's token. They are instead embedded as <c>data:</c> URIs into pages at or above
    /// their tier when those pages render.
    /// </summary>
    public PageVisibility Visibility { get; set; } = PageVisibility.Anonymous;
}
