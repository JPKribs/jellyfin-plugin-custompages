namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// The body returned by <c>/pages/store/{name}/clear</c>.
/// </summary>
public class StoreClearResponse
{
    /// <summary>Gets or sets how many records were removed.</summary>
    public int Removed { get; set; }
}
