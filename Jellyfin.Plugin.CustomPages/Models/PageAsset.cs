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
}
