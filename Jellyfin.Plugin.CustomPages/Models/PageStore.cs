namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// A named collection of JSON records that pages read and write through <c>/pages/store/{name}</c>.
/// A store is defined once for the whole plugin rather than on one page, so a page that collects
/// submissions and a page that reports on them can work against the same data.
/// </summary>
/// <remarks>
/// Access is decided entirely by the tiers here, never by which page is calling. Listing a store on a
/// page would only look like a permission without being one, because the endpoint is reachable by any
/// caller holding a token for the right tier. Records are held in a JSON file under Jellyfin's data
/// directory rather than in the plugin configuration, since visitors write them and the configuration
/// must not be rewritten on every submission.
/// </remarks>
public class PageStore
{
    /// <summary>The largest number of records a store may be configured to hold.</summary>
    public const int RecordCeiling = 100000;

    /// <summary>The longest retention a store may be configured with, roughly ten years.</summary>
    public const int RetentionDayCeiling = 3650;

    /// <summary>Gets or sets the store name, used as the URL segment and the helper's first argument.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the store accepts calls. A disabled store returns 404.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the tier required to read the store at all. This is definitive: a caller below it
    /// reads nothing, whatever else is configured. It defaults to <see cref="PageVisibility.Admin"/>
    /// because a store usually holds what other people submitted.
    /// </summary>
    public PageVisibility ReadAccess { get; set; } = PageVisibility.Admin;

    /// <summary>
    /// Gets or sets how much of the store a caller who has cleared <see cref="ReadAccess"/> is shown.
    /// <see cref="StoreScope.Own"/> narrows each viewer to the records they created, which is what turns
    /// a store into a submission queue where people watch their own row and nobody else's.
    /// </summary>
    /// <remarks>
    /// Administrators are shown every record whatever this says, matching how they are always admitted
    /// to a page restricted to specific users. It is what lets one store serve both halves of a
    /// workflow: the submitters read their own rows, and the administrator working the queue reads all
    /// of them. An administrator can read the store's file off disk regardless, so narrowing them here
    /// would be a restriction the plugin could not actually keep.
    /// </remarks>
    public StoreScope ReadScope { get; set; } = StoreScope.All;

    /// <summary>
    /// Gets or sets the tier required to create a record. <see cref="PageVisibility.Anonymous"/> makes
    /// this an unauthenticated write endpoint reachable by anyone who can reach the server, which is
    /// why the record and size caps apply to every store regardless of tier.
    /// </summary>
    public PageVisibility WriteAccess { get; set; } = PageVisibility.User;


    /// <summary>
    /// Gets or sets a value indicating whether a signed in viewer may update and delete the records
    /// they created. Off by default, so a store whose records carry a server assigned status cannot
    /// have that status rewritten by the person who submitted the row. It grants nothing on its own:
    /// a caller still has to clear both the read and the write tier to reach a record at all.
    /// </summary>
    public bool AllowEditOwn { get; set; }

    /// <summary>
    /// Gets or sets the number of records the store holds before further writes are refused. Together
    /// with the per record size cap this bounds what a store can consume on disk.
    /// </summary>
    public int MaxRecords { get; set; } = 1000;

    /// <summary>
    /// Gets or sets how many days a record is kept before it is removed. Zero, the default, keeps
    /// records forever. Age is measured from when a record was created, not when it was last written,
    /// so a row's lifetime is fixed the moment it is submitted rather than extended by every status
    /// update an administrator writes onto it.
    /// </summary>
    public int RetentionDays { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a new record writes an entry to Jellyfin's activity log,
    /// where administrators already look. Entries are rate limited per store, so a burst of submissions
    /// cannot flood the feed.
    /// </summary>
    public bool NotifyOnWrite { get; set; }
}
