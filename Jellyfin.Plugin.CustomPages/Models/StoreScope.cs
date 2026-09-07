namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// How much of a store a caller who has cleared its read tier is shown.
/// </summary>
public enum StoreScope
{
    /// <summary>Every record in the store.</summary>
    All,

    /// <summary>Only the records the caller created.</summary>
    Own
}
