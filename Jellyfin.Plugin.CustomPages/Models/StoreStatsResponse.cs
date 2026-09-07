namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// The body returned by <c>/pages/store/{name}/stats</c>, used by the dashboard.
/// </summary>
public class StoreStatsResponse
{
    /// <summary>Gets or sets how many records the store currently holds.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets the number of records the store accepts before refusing writes.</summary>
    public int Limit { get; set; }
}
