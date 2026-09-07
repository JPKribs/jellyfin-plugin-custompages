using System.Collections.Generic;

namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// The body returned by <c>/pages/store/{name}/read</c>.
/// </summary>
public class StoreReadResponse
{
    /// <summary>Gets or sets the records the caller is allowed to see, newest first.</summary>
    public IReadOnlyList<StoreRecord> Records { get; set; } = new List<StoreRecord>();

    /// <summary>Gets or sets how many records were returned.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets the number of records the store accepts before refusing writes.</summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets what the caller was shown, either <c>all</c> for every record in the store or
    /// <c>own</c> for only the ones they created. A page can use this to decide whether it is
    /// displaying a personal view or a full one.
    /// </summary>
    public string Scope { get; set; } = "own";
}
