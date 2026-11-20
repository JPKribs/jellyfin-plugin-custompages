using System;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Writes store notices to Jellyfin's activity log. Records arriving and leaving are summed into one
/// entry per outcome, such as "4 records added", so a submission form under load cannot bury the rest of
/// the feed. Clearing a store is a single administrator action and is written on its own straight away.
/// Registered as a singleton, so whatever is still waiting is written when the host shuts down. Every
/// entry is in the server's display language.
/// </summary>
public sealed class StoreActivity : IDisposable
{
    /// <summary>The activity type of a store being cleared from the dashboard.</summary>
    public const string ClearedType = "CustomPages.StoreClear";

    private static readonly ActivityOutcome Added = new(
        "CustomPages.RecordsAdded",
        TranslationKeys.ActivityAddedOne,
        TranslationKeys.ActivityAddedMany,
        (tally, language) => AddedOverview(tally, language));

    private static readonly ActivityOutcome Removed = new(
        "CustomPages.RecordsRemoved",
        TranslationKeys.ActivityRemovedOne,
        TranslationKeys.ActivityRemovedMany,
        (tally, language) => RemovedOverview(tally, language));

    private readonly ActivityLogger _logger;
    private readonly ActivitySummary _summary;
    private readonly Func<string?> _language;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreActivity"/> class.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    /// <param name="config">The server configuration, read for its display language.</param>
    public StoreActivity(ActivityLogger logger, IServerConfigurationManager config)
        : this(
            logger,
            new ActivitySummary(logger, language: () => BaseTranslations.ServerLanguage(config)),
            () => BaseTranslations.ServerLanguage(config))
    {
        ArgumentNullException.ThrowIfNull(config);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreActivity"/> class with its own summary and language,
    /// so tests can choose when it writes and what it writes in.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    /// <param name="summary">The summary records are counted into, which writes in the language it was given.</param>
    /// <param name="language">Reads the language of a cleared store's entry, or gives <c>null</c> for US English.</param>
    public StoreActivity(ActivityLogger logger, ActivitySummary summary, Func<string?> language)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(language);
        _logger = logger;
        _summary = summary;
        _language = language;
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
    {
        var language = _language();
        _logger.Log(
            BaseTranslations.Format(
                removed == 1 ? TranslationKeys.ActivityStoreClearedOne : TranslationKeys.ActivityStoreClearedMany,
                language,
                removed),
            ClearedType,
            ClearedOverview(store, removed, language));
    }

    /// <summary>Writes every summary waiting, now.</summary>
    public void Flush() => _summary.Flush();

    /// <inheritdoc />
    public void Dispose() => _summary.Dispose();

    /// <summary>The overview of a summary of records added, naming the stores and any labels.</summary>
    /// <param name="tally">What was counted.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string AddedOverview(ActivityTally tally, string? language = null)
        => Overview(tally, TranslationKeys.ActivityRecordAdded, TranslationKeys.ActivityRecordsAdded, language);

    /// <summary>The overview of a summary of records removed, naming the stores and any labels.</summary>
    /// <param name="tally">What was counted.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string RemovedOverview(ActivityTally tally, string? language = null)
        => Overview(tally, TranslationKeys.ActivityRecordRemoved, TranslationKeys.ActivityRecordsRemoved, language);

    /// <summary>The overview of a store cleared from the dashboard.</summary>
    /// <param name="store">The store name.</param>
    /// <param name="removed">How many records the clear removed.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string ClearedOverview(string store, int removed, string? language = null)
        => ActivityText.Sentence(BaseTranslations.Format(
            removed == 1 ? TranslationKeys.ActivityRecordCleared : TranslationKeys.ActivityRecordsCleared,
            language,
            removed,
            store))!;

    // Labels name the records, which is what an administrator reads them for, so any given are listed
    // after the stores. Records written without one are counted but not named.
    private static string Overview(ActivityTally tally, string one, string many, string? language)
    {
        ArgumentNullException.ThrowIfNull(tally);
        var text = BaseTranslations.Format(
            tally.Total == 1 ? one : many,
            language,
            tally.Total,
            ActivityText.CountedList(tally.Sources));
        var labels = ActivityText.CountedList(tally.Reasons);
        if (labels.Length > 0)
        {
            text = BaseTranslations.Format(TranslationKeys.ActivityIncluding, language, text, labels);
        }

        return ActivityText.Sentence(text)!;
    }
}
