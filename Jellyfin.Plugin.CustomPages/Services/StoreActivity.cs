using System;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Writes store notices to Jellyfin's activity log. Records arriving and leaving are summed into one
/// entry per outcome, such as "4 Records Added", so a submission form under load cannot bury the rest of
/// the feed. Clearing a store is a single administrator action and is written on its own straight away.
/// Registered as a singleton, so whatever is still waiting is written when the host shuts down.
/// </summary>
public sealed class StoreActivity : IDisposable
{
    /// <summary>The activity type of a store being cleared from the dashboard.</summary>
    public const string ClearedType = "CustomPages.StoreClear";

    /// <summary>Records written to a store.</summary>
    public static readonly ActivityOutcome Added = new(
        "CustomPages.RecordsAdded",
        "Added",
        tally => AddedOverview(tally),
        Noun: "Record");

    /// <summary>Records deleted from a store.</summary>
    public static readonly ActivityOutcome Removed = new(
        "CustomPages.RecordsRemoved",
        "Removed",
        tally => RemovedOverview(tally),
        Noun: "Record");

    private readonly ActivityLogger _logger;
    private readonly ActivitySummary _summary;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreActivity"/> class.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    public StoreActivity(ActivityLogger logger)
        : this(logger, new ActivitySummary(logger))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreActivity"/> class with its own summary, so tests
    /// can choose when it writes.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    /// <param name="summary">The summary records are counted into.</param>
    public StoreActivity(ActivityLogger logger, ActivitySummary summary)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(summary);
        _logger = logger;
        _summary = summary;
    }

    /// <summary>Counts a record written to a store.</summary>
    /// <param name="store">The store name.</param>
    /// <param name="label">The record's label, when the page gave one.</param>
    public void RecordAdded(string store, string? label) => _summary.Add(Added, store, label);

    /// <summary>Counts a record deleted from a store.</summary>
    /// <param name="store">The store name.</param>
    /// <param name="label">The record's label, when the page gave one.</param>
    public void RecordRemoved(string store, string? label) => _summary.Add(Removed, store, label);

    /// <summary>Writes the entry for a store cleared from the dashboard.</summary>
    /// <param name="store">The store name.</param>
    /// <param name="removed">How many records the clear removed.</param>
    public void StoreCleared(string store, int removed)
        => _logger.Log(
            ActivityText.SummaryName(removed, "Cleared", "Record"),
            ClearedType,
            ClearedOverview(store, removed));

    /// <summary>Writes every summary waiting, now.</summary>
    public void Flush() => _summary.Flush();

    /// <inheritdoc />
    public void Dispose() => _summary.Dispose();

    /// <summary>The overview of a summary of records added, naming the stores and any labels.</summary>
    /// <param name="tally">What was counted.</param>
    /// <returns>The overview.</returns>
    public static string AddedOverview(ActivityTally tally) => Overview(tally, "added to");

    /// <summary>The overview of a summary of records removed, naming the stores and any labels.</summary>
    /// <param name="tally">What was counted.</param>
    /// <returns>The overview.</returns>
    public static string RemovedOverview(ActivityTally tally) => Overview(tally, "removed from");

    /// <summary>The overview of a store cleared from the dashboard.</summary>
    /// <param name="store">The store name.</param>
    /// <param name="removed">How many records the clear removed.</param>
    /// <returns>The overview.</returns>
    public static string ClearedOverview(string store, int removed)
        => ActivityText.Sentence(
            ActivityText.Count(removed, "record") + " " + ActivityText.WasOrWere(removed)
            + " cleared from " + store + " by an administrator from the dashboard")!;

    // Labels name the records, which is what an administrator reads them for, so any given are listed
    // after the stores. Records written without one are counted but not named.
    private static string Overview(ActivityTally tally, string movement)
    {
        ArgumentNullException.ThrowIfNull(tally);
        var text = ActivityText.Count(tally.Total, "record") + " " + ActivityText.WasOrWere(tally.Total)
            + " " + movement + " " + ActivityText.CountedList(tally.Sources);
        var labels = ActivityText.CountedList(tally.Reasons);
        if (labels.Length > 0)
        {
            text += ", including " + labels;
        }

        return ActivityText.Sentence(text)!;
    }
}
