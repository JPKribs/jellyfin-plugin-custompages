using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Persists each store's records as one JSON file under Jellyfin's data directory.
/// </summary>
/// <remarks>
/// Records live in the data directory rather than beside the plugin configuration because they are
/// data, not settings. Page visitors write them, they grow to the store's configured cap, and the
/// configuration must not be rewritten on every submission. The trade is that they are not captured
/// by a configuration backup the way pages and assets are, so treat a store as something to export
/// rather than something a config restore will bring back.
///
/// Every store gets its own gate and its own in memory copy of the records, so two stores never block
/// each other and a submission burst against one does not re-read the file each time. Writes go to a
/// temp file and are then moved into place, so a concurrent reader never sees half a file.
/// </remarks>
public sealed class StoreService : IStoreService
{
    /// <summary>The largest payload a single record may carry, measured as serialized UTF-8 bytes.</summary>
    public const int MaxRecordBytes = 64 * 1024;

    /// <summary>The longest label kept on a record, since it is shown to administrators in the activity log.</summary>
    public const int MaxLabelLength = 200;

    private static readonly JsonSerializerOptions StoreJson = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    // The cap is measured against compact JSON so it means the same thing as the request body limit the
    // controller applies. Measuring the indented form the file uses would reject payloads the body limit
    // had already accepted, and by a margin that varies with how deeply nested the payload happens to be.
    private static readonly JsonSerializerOptions MeasureJson = new();

    /// <summary>How long a store waits between activity log entries, so a burst cannot flood the feed.</summary>
    public static readonly TimeSpan NotifyInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, StoreState> _states = new(StringComparer.Ordinal);
    private readonly Func<PluginConfiguration?> _configuration;
    private readonly ILogger<StoreService> _logger;
    private readonly ActivityLogger? _activity;
    private readonly TimeProvider _time;
    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreService"/> class.
    /// </summary>
    /// <param name="paths">The application paths, used to place the stores under Jellyfin's data directory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="activity">Writes new-record notices to Jellyfin's activity log.</param>
    public StoreService(IApplicationPaths paths, ILogger<StoreService> logger, ActivityLogger activity)
        : this(paths, logger, static () => Plugin.Instance?.ReadConfiguration(static config => config), activity)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreService"/> class with an explicit configuration
    /// source, so the store can be exercised without a running plugin instance.
    /// </summary>
    /// <param name="paths">The application paths, used to place the stores under Jellyfin's data directory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">Returns the current configuration, or <c>null</c> when unavailable.</param>
    /// <param name="activity">Writes new-record notices to the activity log, or <c>null</c> to write none.</param>
    /// <param name="time">The clock, so retention can be exercised without waiting out a real day.</param>
    public StoreService(
        IApplicationPaths paths,
        ILogger<StoreService> logger,
        Func<PluginConfiguration?> configuration,
        ActivityLogger? activity = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logger = logger;
        _configuration = configuration;
        _activity = activity;
        _time = time ?? TimeProvider.System;
        _root = Path.Combine(paths.DataPath, "custompages", "stores");
    }

    /// <inheritdoc />
    public PageStore? Find(string name)
    {
        if (!IsValidStoreName(name))
        {
            return null;
        }

        return _configuration()?.Stores?.FirstOrDefault(s =>
            s.Enabled && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IReadOnlyList<StoreRecord> Read(PageStore store, Guid? owner)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null)
        {
            return Array.Empty<StoreRecord>();
        }

        lock (state.Gate)
        {
            Prepare(store, state);
            return state.Records
                .Where(r => Owns(r, owner))
                .OrderByDescending(r => r.CreatedUtc)
                .Select(r => r.Clone())
                .ToList();
        }
    }

    /// <inheritdoc />
    public StoreRecord? ReadOne(PageStore store, string id, Guid? owner)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null || string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (state.Gate)
        {
            Prepare(store, state);
            var record = state.Records.FirstOrDefault(r =>
                string.Equals(r.Id, id, StringComparison.Ordinal) && Owns(r, owner));
            return record?.Clone();
        }
    }

    /// <inheritdoc />
    public (StoreWriteResult Result, StoreRecord? Record) Create(PageStore store, JsonNode? data, string? label, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null)
        {
            return (StoreWriteResult.NotFound, null);
        }

        if (Oversized(data))
        {
            return (StoreWriteResult.TooLarge, null);
        }

        lock (state.Gate)
        {
            Prepare(store, state);

            // The cap is checked inside the gate so a burst of concurrent submissions cannot each see
            // room and then overshoot it together.
            if (state.Records.Count >= EffectiveMaxRecords(store))
            {
                return (StoreWriteResult.Full, null);
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var record = new StoreRecord
            {
                Id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                CreatedUtc = now,
                UpdatedUtc = now,
                UserId = userId == Guid.Empty ? null : userId.ToString("D", CultureInfo.InvariantCulture),
                Label = CleanLabel(label),
                Data = data?.DeepClone()
            };

            state.Records.Add(record);
            if (!Persist(state))
            {
                state.Records.Remove(record);
                return (StoreWriteResult.Failed, null);
            }

            Notify(store, state, NoticeKind.Added, record.Label, now);
            return (StoreWriteResult.Ok, record.Clone());
        }
    }

    /// <inheritdoc />
    public (StoreWriteResult Result, StoreRecord? Record) Update(PageStore store, string id, JsonNode? data, string? label, Guid? owner)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null || string.IsNullOrEmpty(id))
        {
            return (StoreWriteResult.NotFound, null);
        }

        if (Oversized(data))
        {
            return (StoreWriteResult.TooLarge, null);
        }

        lock (state.Gate)
        {
            Prepare(store, state);
            var record = state.Records.FirstOrDefault(r =>
                string.Equals(r.Id, id, StringComparison.Ordinal) && Owns(r, owner));
            if (record is null)
            {
                return (StoreWriteResult.NotFound, null);
            }

            // Only the payload and the update stamp move. The owner in particular is never reassigned,
            // so an administrator writing a status onto somebody's submission does not take it over and
            // strand the submitter's own view of it.
            var previousData = record.Data;
            var previousLabel = record.Label;
            record.Data = data?.DeepClone();

            // A caller that sends no label is updating the payload, not renaming the record, so the
            // name an administrator already saw in the activity log survives the write.
            var incoming = CleanLabel(label);
            if (incoming is not null)
            {
                record.Label = incoming;
            }

            record.UpdatedUtc = _time.GetUtcNow().UtcDateTime;

            if (!Persist(state))
            {
                record.Data = previousData;
                record.Label = previousLabel;
                return (StoreWriteResult.Failed, null);
            }

            return (StoreWriteResult.Ok, record.Clone());
        }
    }

    /// <inheritdoc />
    public bool Delete(PageStore store, string id, Guid? owner)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null || string.IsNullOrEmpty(id))
        {
            return false;
        }

        lock (state.Gate)
        {
            Prepare(store, state);
            var record = state.Records.FirstOrDefault(r =>
                string.Equals(r.Id, id, StringComparison.Ordinal) && Owns(r, owner));
            if (record is null)
            {
                return false;
            }

            state.Records.Remove(record);
            if (!Persist(state))
            {
                state.Records.Add(record);
                return false;
            }

            // The label was supplied when the record was written, so a removal names the same thing the
            // addition did without the page having to send it again.
            Notify(store, state, NoticeKind.Removed, record.Label, _time.GetUtcNow().UtcDateTime);
            return true;
        }
    }

    /// <inheritdoc />
    public int Clear(PageStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null)
        {
            return 0;
        }

        lock (state.Gate)
        {
            Prepare(store, state);
            var removed = state.Records;
            if (removed.Count == 0)
            {
                return 0;
            }

            state.Records = new List<StoreRecord>();
            if (!Persist(state))
            {
                state.Records = removed;
                return 0;
            }

            return removed.Count;
        }
    }

    /// <inheritdoc />
    public int Count(PageStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var state = StateFor(store);
        if (state is null)
        {
            return 0;
        }

        lock (state.Gate)
        {
            Prepare(store, state);
            return state.Records.Count;
        }
    }

    /// <summary>
    /// Reports whether a store name is safe to use as a URL segment and a file name.
    /// </summary>
    /// <param name="name">The store name to test.</param>
    /// <returns><c>true</c> when the name matches the slug character set.</returns>
    public static bool IsValidStoreName(string? name) => PageService.IsValidSlug(name);

    /// <summary>
    /// Clamps a configured record cap into the supported range, so a hand edited configuration cannot
    /// set a store to unlimited or to a negative cap that would refuse every write.
    /// </summary>
    /// <param name="store">The store whose cap is being read.</param>
    /// <returns>The cap actually applied.</returns>
    public static int EffectiveMaxRecords(PageStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return Math.Clamp(store.MaxRecords, 1, PageStore.RecordCeiling);
    }

    /// <summary>
    /// Applies every store's retention policy, for the scheduled task. A store nobody reads or writes
    /// would otherwise keep its records past their retention, since the sweep on access never runs.
    /// </summary>
    /// <returns>The number of records removed across all stores.</returns>
    public int SweepAll()
    {
        var removed = 0;
        foreach (var store in _configuration()?.Stores ?? new List<PageStore>())
        {
            var state = StateFor(store);
            if (state is null)
            {
                continue;
            }

            lock (state.Gate)
            {
                EnsureLoaded(state);
                removed += Sweep(store, state);
            }
        }

        return removed;
    }

    /// <summary>
    /// Returns when a record created at <paramref name="createdUtc"/> expires under a retention policy.
    /// </summary>
    /// <param name="createdUtc">When the record was created.</param>
    /// <param name="retentionDays">The retention in days, or zero to keep forever.</param>
    /// <returns>The expiry instant, or <c>null</c> when the record is kept forever.</returns>
    public static DateTime? ExpiresAt(DateTime createdUtc, int retentionDays)
        => retentionDays <= 0 ? null : createdUtc.AddDays(retentionDays);

    // Loads the records, then drops anything past the store's retention. Retention is applied on the way
    // in rather than on a timer alone, so a read never returns a record the policy has already expired
    // even if the scheduled sweep has not run since.
    private void Prepare(PageStore store, StoreState state)
    {
        EnsureLoaded(state);
        Sweep(store, state);
    }

    // Removes expired records and persists when anything went. Returns how many were removed. A failed
    // persist leaves the trimmed list in memory: the records are past their retention either way, and
    // the next successful write commits the trim.
    private int Sweep(PageStore store, StoreState state)
    {
        if (store.RetentionDays <= 0 || state.Records.Count == 0)
        {
            return 0;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var removed = state.Records.RemoveAll(r => ExpiresAt(r.CreatedUtc, store.RetentionDays) <= now);
        if (removed > 0)
        {
            Persist(state);
            _logger.LogInformation(
                "Custom Pages: removed {Count} expired records from store {Store}", removed, store.Name);
        }

        return removed;
    }

    // Which way a record moved, for the activity log notice.
    private enum NoticeKind
    {
        Added,
        Removed
    }

    // Announces a record arriving or leaving in Jellyfin's activity log, rate limited per store. A
    // submission form under load would otherwise put one entry in the feed per submission and bury
    // everything else in it, so events inside the window are counted and reported by the next entry
    // that does go out. Adds and removes share one window, because what an administrator wants is one
    // line saying what happened to the store, not two racing each other.
    private void Notify(PageStore store, StoreState state, NoticeKind kind, string? label, DateTime now)
    {
        if (!store.NotifyOnWrite || _activity is null)
        {
            return;
        }

        if (kind == NoticeKind.Added)
        {
            state.PendingAdds++;
        }
        else
        {
            state.PendingRemoves++;
        }

        // Only useful when this turns out to be the only event in the window, which is the case the
        // label exists for. A batch reports counts instead and has no single thing to name.
        state.PendingLabel = label;

        if (state.LastNoticeUtc is not null && now - state.LastNoticeUtc.Value < NotifyInterval)
        {
            return;
        }

        var added = state.PendingAdds;
        var removed = state.PendingRemoves;
        var only = state.PendingLabel;
        state.PendingAdds = 0;
        state.PendingRemoves = 0;
        state.PendingLabel = null;
        state.LastNoticeUtc = now;

        _activity.Log(
            Headline(store.Name, added, removed, only),
            "CustomPages.StoreWrite",
            "The store now holds " + state.Records.Count.ToString(CultureInfo.InvariantCulture) + " records.");
    }

    /// <summary>
    /// Builds the activity log headline for a window of store activity. One record names itself when the
    /// page supplied a label, and anything more reports counts, since a batch has no single thing to name.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="added">How many records arrived in the window.</param>
    /// <param name="removed">How many records left in the window.</param>
    /// <param name="label">The label of the only record involved, when there was only one.</param>
    /// <returns>The headline.</returns>
    public static string Headline(string store, int added, int removed, string? label)
    {
        var count = FormattableString.Invariant;

        if (added == 1 && removed == 0)
        {
            return (string.IsNullOrEmpty(label) ? "A record" : label) + " was added to " + store;
        }

        if (added == 0 && removed == 1)
        {
            return (string.IsNullOrEmpty(label) ? "A record" : label) + " was removed from " + store;
        }

        if (removed == 0)
        {
            return count($"{added} items added to {store}");
        }

        if (added == 0)
        {
            return count($"{removed} items removed from {store}");
        }

        return count($"{added} items added and {removed} items removed from {store}");
    }

    /// <summary>
    /// Normalizes a caller supplied label. It is shown to administrators in the activity log, so control
    /// characters are flattened and the length is capped rather than letting a page write a wall of text
    /// or a line break into the feed.
    /// </summary>
    /// <param name="label">The raw label.</param>
    /// <returns>The cleaned label, or <c>null</c> when there was nothing usable in it.</returns>
    public static string? CleanLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(label.Length, MaxLabelLength));
        foreach (var c in label)
        {
            if (builder.Length >= MaxLabelLength)
            {
                break;
            }

            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static bool Owns(StoreRecord record, Guid? owner)
    {
        if (owner is null)
        {
            return true;
        }

        // An unowned record belongs to nobody, so no scoped caller ever matches it.
        return record.UserId is not null
            && Guid.TryParse(record.UserId, out var parsed)
            && parsed == owner.Value;
    }

    private static bool Oversized(JsonNode? data)
    {
        if (data is null)
        {
            return false;
        }

        return JsonSerializer.SerializeToUtf8Bytes(data, MeasureJson).Length > MaxRecordBytes;
    }

    private StoreState? StateFor(PageStore store)
    {
        if (!IsValidStoreName(store.Name))
        {
            return null;
        }

        var key = store.Name.ToLowerInvariant();
        return _states.GetOrAdd(key, static (k, root) => new StoreState(Path.Combine(root, k + ".json")), _root);
    }

    private void EnsureLoaded(StoreState state)
    {
        if (state.Loaded)
        {
            return;
        }

        try
        {
            if (File.Exists(state.File))
            {
                using var stream = File.OpenRead(state.File);
                var loaded = JsonSerializer.Deserialize<List<StoreRecord>>(stream, StoreJson);
                state.Records = loaded ?? new List<StoreRecord>();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Starting empty would silently discard the file on the next write, so refuse to load
            // instead: the store answers as empty this run and the unreadable file is left untouched
            // for an administrator to look at.
            _logger.LogError(ex, "Custom Pages: could not read the store at {File}. It will answer as empty and refuse writes until the file is repaired or removed and Jellyfin is restarted", state.File);
            state.Records = new List<StoreRecord>();
            state.Readable = false;
        }

        state.Loaded = true;
    }

    private bool Persist(StoreState state)
    {
        if (!state.Readable)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(state.File)!);
            var temp = state.File + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, state.Records, StoreJson);
            }

            File.Move(temp, state.File, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Custom Pages: could not write the store at {File}", state.File);
            return false;
        }
    }

    // One store's gate, file path, and in memory records. Held for the process lifetime, keyed by the
    // normalized store name, so renaming a store in configuration simply routes to a different file.
    private sealed class StoreState
    {
        public StoreState(string file)
        {
            File = file;
        }

        public object Gate { get; } = new object();

        public string File { get; }

        public List<StoreRecord> Records { get; set; } = new();

        public bool Loaded { get; set; }

        // Cleared when the backing file exists but could not be parsed. Writing then would destroy
        // whatever it holds, so every write is refused until the file is fixed or removed.
        public bool Readable { get; set; } = true;

        // Activity log rate limiting. Process lifetime only, so a restart simply starts a fresh window.
        public DateTime? LastNoticeUtc { get; set; }

        public int PendingAdds { get; set; }

        public int PendingRemoves { get; set; }

        public string? PendingLabel { get; set; }
    }
}
