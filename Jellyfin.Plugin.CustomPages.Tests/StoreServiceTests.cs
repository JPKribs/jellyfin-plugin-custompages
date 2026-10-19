using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for <see cref="StoreService"/>, which owns the records on disk and the limits that bound them.
/// </summary>
public class StoreServiceTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _dataPath;
    private readonly PluginConfiguration _config = new();

    public StoreServiceTests()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), "cp-store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataPath);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dataPath, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private IActivityManager? _activityManager;

    private StoreService Create()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(_dataPath);

        ActivityLogger? activity = null;
        if (_activityManager is not null)
        {
            activity = new ActivityLogger(_activityManager, NullLogger<ActivityLogger>.Instance);
        }

        return new StoreService(paths, NullLogger<StoreService>.Instance, () => _config, activity, _clock);
    }

    // A settable clock, so retention can be exercised without waiting out a real day.
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private PageStore Define(string name = "downloads", int max = 10)
    {
        var store = new PageStore { Name = name, MaxRecords = max };
        _config.Stores.Add(store);
        return store;
    }

    private static JsonNode Data(string url) => JsonNode.Parse("{\"url\":\"" + url + "\"}")!;

    // MARK: Finding

    [Fact]
    public void Find_DisabledStore_ReturnsNull()
    {
        var store = Define();
        store.Enabled = false;
        Assert.Null(Create().Find("downloads"));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Define();
        Assert.NotNull(Create().Find("DOWNLOADS"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("with/slash")]
    [InlineData("")]
    public void Find_UnsafeName_ReturnsNull(string name)
    {
        // The name becomes a file name under the data directory, so a traversal attempt must not
        // resolve even when a hand edited configuration contains one.
        _config.Stores.Add(new PageStore { Name = name });
        Assert.Null(Create().Find(name));
    }

    // MARK: Creating and reading

    [Fact]
    public void Create_AssignsServerSideFields()
    {
        var service = Create();
        var store = Define();

        var (result, record) = service.Create(store, Data("a"), null, Alice);

        Assert.Equal(StoreWriteResult.Ok, result);
        Assert.NotNull(record);
        Assert.False(string.IsNullOrEmpty(record!.Id));
        Assert.Equal(Alice.ToString("D"), record.UserId);
        Assert.NotEqual(default, record.CreatedUtc);
    }

    [Fact]
    public void Create_AnonymousWrite_HasNoOwner()
    {
        var service = Create();
        var store = Define();

        var (_, record) = service.Create(store, Data("a"), null, Guid.Empty);

        Assert.Null(record!.UserId);
    }

    [Fact]
    public void Read_ScopedToOwner_ExcludesOtherUsers()
    {
        var service = Create();
        var store = Define();
        service.Create(store, Data("alice"), null, Alice);
        service.Create(store, Data("bob"), null, Bob);

        var mine = service.Read(store, Alice);

        Assert.Single(mine);
        Assert.Equal(Alice.ToString("D"), mine[0].UserId);
    }

    [Fact]
    public void Read_ScopedToOwner_ExcludesAnonymousRecords()
    {
        // An unowned record belongs to nobody, so no scoped caller should be handed it.
        var service = Create();
        var store = Define();
        service.Create(store, Data("anon"), null, Guid.Empty);

        Assert.Empty(service.Read(store, Alice));
    }

    [Fact]
    public void Read_Unscoped_ReturnsEverything()
    {
        var service = Create();
        var store = Define();
        service.Create(store, Data("alice"), null, Alice);
        service.Create(store, Data("bob"), null, Bob);

        Assert.Equal(2, service.Read(store, null).Count);
    }

    [Fact]
    public void Read_ReturnsCopies_SoCallerCannotMutateTheStore()
    {
        var service = Create();
        var store = Define();
        service.Create(store, Data("a"), null, Alice);

        var first = service.Read(store, null)[0];
        first.Data!["url"] = "tampered";

        Assert.Equal("a", service.Read(store, null)[0].Data!["url"]!.GetValue<string>());
    }

    // MARK: Limits

    [Fact]
    public void Create_AtRecordLimit_IsRefused()
    {
        var service = Create();
        var store = Define(max: 2);
        service.Create(store, Data("a"), null, Alice);
        service.Create(store, Data("b"), null, Alice);

        var (result, _) = service.Create(store, Data("c"), null, Alice);

        Assert.Equal(StoreWriteResult.Full, result);
        Assert.Equal(2, service.Count(store));
    }

    [Fact]
    public void Create_OversizedPayload_IsRefused()
    {
        var service = Create();
        var store = Define();
        var big = JsonNode.Parse("{\"blob\":\"" + new string('x', StoreService.MaxRecordBytes + 1) + "\"}");

        var (result, _) = service.Create(store, big, null, Alice);

        Assert.Equal(StoreWriteResult.TooLarge, result);
        Assert.Equal(0, service.Count(store));
    }

    [Fact]
    public void EffectiveMaxRecords_ClampsOutOfRangeConfiguration()
    {
        Assert.Equal(1, StoreService.EffectiveMaxRecords(new PageStore { MaxRecords = 0 }));
        Assert.Equal(PageStore.RecordCeiling, StoreService.EffectiveMaxRecords(new PageStore { MaxRecords = int.MaxValue }));
    }

    // MARK: Updating and deleting

    [Fact]
    public void Update_KeepsIdentityAndOwner()
    {
        var service = Create();
        var store = Define();
        var (_, created) = service.Create(store, Data("a"), null, Alice);

        var (result, updated) = service.Update(store, created!.Id, Data("done"), null, null);

        Assert.Equal(StoreWriteResult.Ok, result);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal(created.CreatedUtc, updated.CreatedUtc);

        // An administrator writing a status onto a submission must not take it over, or the submitter
        // loses their own view of the row they created.
        Assert.Equal(Alice.ToString("D"), updated.UserId);
        Assert.Equal("done", updated.Data!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Update_ScopedToAnotherOwner_IsNotFound()
    {
        var service = Create();
        var store = Define();
        var (_, created) = service.Create(store, Data("a"), null, Alice);

        var (result, _) = service.Update(store, created!.Id, Data("hijacked"), null, Bob);

        Assert.Equal(StoreWriteResult.NotFound, result);
        Assert.Equal("a", service.Read(store, null)[0].Data!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Delete_ScopedToOwner_RemovesOnlyTheirs()
    {
        var service = Create();
        var store = Define();
        var (_, mine) = service.Create(store, Data("alice"), null, Alice);
        var (_, theirs) = service.Create(store, Data("bob"), null, Bob);

        Assert.False(service.Delete(store, theirs!.Id, Alice));
        Assert.True(service.Delete(store, mine!.Id, Alice));
        Assert.Equal(1, service.Count(store));
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var service = Create();
        var store = Define();
        service.Create(store, Data("a"), null, Alice);
        service.Create(store, Data("b"), null, Bob);

        Assert.Equal(2, service.Clear(store));
        Assert.Equal(0, service.Count(store));
    }

    // MARK: Persistence

    [Fact]
    public void Records_SurviveANewServiceInstance()
    {
        var store = Define();
        Create().Create(store, Data("a"), null, Alice);

        var reloaded = Create().Read(store, null);

        Assert.Single(reloaded);
        Assert.Equal("a", reloaded[0].Data!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Records_LiveUnderTheDataDirectory()
    {
        var store = Define();
        Create().Create(store, Data("a"), null, Alice);

        Assert.True(File.Exists(Path.Combine(_dataPath, "custompages", "stores", "downloads.json")));
    }

    [Fact]
    public void UnreadableFile_IsNotOverwritten()
    {
        // Starting empty on a parse failure would let the next write destroy whatever the file held,
        // so the store answers as empty and refuses to write until an administrator looks at it.
        var store = Define();
        var file = Path.Combine(_dataPath, "custompages", "stores", "downloads.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ this is not json");

        var service = Create();
        var (result, _) = service.Create(store, Data("a"), null, Alice);

        Assert.Equal(StoreWriteResult.Failed, result);
        Assert.Equal("{ this is not json", File.ReadAllText(file));
    }

    [Fact]
    public void Stores_AreIsolatedFromEachOther()
    {
        var service = Create();
        var first = Define("downloads");
        var second = Define("requests");
        service.Create(first, Data("a"), null, Alice);

        Assert.Equal(1, service.Count(first));
        Assert.Equal(0, service.Count(second));
    }

    [Fact]
    public void ConcurrentCreates_NeverExceedTheLimit()
    {
        var service = Create();
        var store = Define(max: 20);
        var results = new List<StoreWriteResult>();

        System.Threading.Tasks.Parallel.For(0, 100, _ =>
        {
            var (result, _) = service.Create(store, Data("x"), null, Alice);
            lock (results)
            {
                results.Add(result);
            }
        });

        Assert.Equal(20, service.Count(store));
        Assert.Equal(20, results.FindAll(r => r == StoreWriteResult.Ok).Count);
    }

    // MARK: Retention

    [Fact]
    public void Retention_ZeroKeepsRecordsForever()
    {
        var service = Create();
        var store = Define();
        store.RetentionDays = 0;
        service.Create(store, Data("a"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(1, service.Count(store));
    }

    [Fact]
    public void Retention_RemovesRecordsPastTheirAge()
    {
        var service = Create();
        var store = Define();
        store.RetentionDays = 7;
        service.Create(store, Data("old"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(8));

        Assert.Equal(0, service.Count(store));
    }

    [Fact]
    public void Retention_KeepsRecordsInsideTheirAge()
    {
        var service = Create();
        var store = Define();
        store.RetentionDays = 7;
        service.Create(store, Data("fresh"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(6));

        Assert.Equal(1, service.Count(store));
    }

    [Fact]
    public void Retention_IsMeasuredFromCreationNotLastWrite()
    {
        // A status update from an administrator must not extend a submission's lifetime, or a busy row
        // would outlive the policy indefinitely.
        var service = Create();
        var store = Define();
        store.RetentionDays = 7;
        var (_, created) = service.Create(store, Data("a"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(6));
        service.Update(store, created!.Id, Data("touched"), null, null);
        _clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(0, service.Count(store));
    }

    [Fact]
    public void Retention_AppliesOnReadSoNothingExpiredIsServed()
    {
        var service = Create();
        var store = Define();
        store.RetentionDays = 1;
        service.Create(store, Data("a"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(2));

        Assert.Empty(service.Read(store, null));
    }

    [Fact]
    public void Retention_ExpiredRecordsAreRemovedFromDisk()
    {
        var store = Define();
        store.RetentionDays = 1;
        Create().Create(store, Data("a"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(2));
        Create().Count(store);

        // A fresh service reads the file back, so an empty result proves the trim was persisted rather
        // than only applied in memory.
        Assert.Empty(Create().Read(store, null));
    }

    [Fact]
    public void Retention_FreesRoomAgainstTheRecordLimit()
    {
        var service = Create();
        var store = Define(max: 1);
        store.RetentionDays = 1;
        service.Create(store, Data("a"), null, Alice);
        Assert.Equal(StoreWriteResult.Full, service.Create(store, Data("b"), null, Alice).Result);

        _clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(StoreWriteResult.Ok, service.Create(store, Data("c"), null, Alice).Result);
    }

    [Fact]
    public void SweepAll_ClearsStoresNobodyIsReading()
    {
        // This is the whole reason the scheduled task exists: a store that stopped receiving traffic
        // would otherwise keep its last records forever whatever retention said.
        var service = Create();
        var keep = Define("keep");
        var expire = Define("expire");
        expire.RetentionDays = 1;
        service.Create(keep, Data("a"), null, Alice);
        service.Create(expire, Data("b"), null, Alice);

        _clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(1, service.SweepAll());
        Assert.Equal(1, service.Count(keep));
    }

    [Fact]
    public void ExpiresAt_ZeroRetentionNeverExpires()
    {
        Assert.Null(StoreService.ExpiresAt(DateTime.UtcNow, 0));
        Assert.Null(StoreService.ExpiresAt(DateTime.UtcNow, -5));
    }

    // MARK: Notification

    [Fact]
    public void Notify_OffByDefault_WritesNoActivityEntry()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define();

        service.Create(store, Data("a"), null, Alice);

        _activityManager.DidNotReceive().CreateAsync(Arg.Any<ActivityLog>());
    }

    [Fact]
    public async Task Notify_OnWrite_WritesAnActivityEntry()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define();
        store.NotifyOnWrite = true;

        service.Create(store, Data("a"), null, Alice);

        // ActivityLogger is fire and forget, so let the continuation run before asserting.
        await Task.Delay(50);
        await _activityManager.Received(1).CreateAsync(Arg.Any<ActivityLog>());
    }

    [Fact]
    public async Task Notify_RateLimitsABurst()
    {
        // A submission form under load would otherwise put one entry in the feed per submission and
        // bury everything else in it.
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define(max: 50);
        store.NotifyOnWrite = true;

        for (var i = 0; i < 10; i++)
        {
            service.Create(store, Data("a"), null, Alice);
        }

        await Task.Delay(50);
        await _activityManager.Received(1).CreateAsync(Arg.Any<ActivityLog>());
    }

    [Fact]
    public async Task Notify_ResumesAfterTheRateLimitWindow()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define(max: 50);
        store.NotifyOnWrite = true;

        service.Create(store, Data("a"), null, Alice);
        _clock.Advance(StoreService.NotifyInterval + TimeSpan.FromSeconds(1));
        service.Create(store, Data("b"), null, Alice);

        await Task.Delay(50);
        await _activityManager.Received(2).CreateAsync(Arg.Any<ActivityLog>());
    }

    // MARK: Activity log headlines

    [Theory]
    [InlineData(1, 0, "Big Buck Bunny", "Big Buck Bunny was added to downloads")]
    [InlineData(0, 1, "Big Buck Bunny", "Big Buck Bunny was removed from downloads")]
    [InlineData(1, 0, null, "A record was added to downloads")]
    [InlineData(0, 1, null, "A record was removed from downloads")]
    [InlineData(4, 0, "ignored", "4 items added to downloads")]
    [InlineData(0, 3, "ignored", "3 items removed from downloads")]
    [InlineData(4, 3, "ignored", "4 items added and 3 items removed from downloads")]
    public void Headline_ReadsAsASentence(int added, int removed, string? label, string expected)
    {
        // A batch has no single thing to name, so it reports counts rather than picking one record's
        // label and implying the entry was about that record.
        Assert.Equal(expected, StoreService.Headline("downloads", added, removed, label));
    }

    // MARK: Labels

    [Fact]
    public void Create_KeepsTheLabelOnTheRecord()
    {
        var service = Create();
        var store = Define();

        var (_, record) = service.Create(store, Data("a"), "Big Buck Bunny", Alice);

        Assert.Equal("Big Buck Bunny", record!.Label);
    }

    [Fact]
    public void Update_WithoutALabel_KeepsTheStoredOne()
    {
        // An update writing a status is not a rename, so the name an administrator already saw in the
        // activity log has to survive it, or the removal notice would name something different.
        var service = Create();
        var store = Define();
        var (_, created) = service.Create(store, Data("a"), "Big Buck Bunny", Alice);

        var (_, updated) = service.Update(store, created!.Id, Data("done"), null, null);

        Assert.Equal("Big Buck Bunny", updated!.Label);
    }

    [Fact]
    public void Update_WithALabel_ReplacesIt()
    {
        var service = Create();
        var store = Define();
        var (_, created) = service.Create(store, Data("a"), "Old name", Alice);

        var (_, updated) = service.Update(store, created!.Id, Data("a"), "New name", null);

        Assert.Equal("New name", updated!.Label);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  spaced  ", "spaced")]
    public void CleanLabel_DiscardsNothingUsable(string? raw, string? expected)
    {
        Assert.Equal(expected, StoreService.CleanLabel(raw));
    }

    [Fact]
    public void CleanLabel_FlattensControlCharacters()
    {
        // The label lands in an admin facing feed, so a page must not be able to write a line break
        // into it and forge what looks like a second entry.
        Assert.Equal("one two", StoreService.CleanLabel("one\ntwo"));
    }

    [Fact]
    public void CleanLabel_CapsTheLength()
    {
        var cleaned = StoreService.CleanLabel(new string('x', StoreService.MaxLabelLength + 50));
        Assert.Equal(StoreService.MaxLabelLength, cleaned!.Length);
    }

    [Fact]
    public async Task Notify_OnDelete_AlsoWritesAnEntry()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define();
        store.NotifyOnWrite = true;
        var (_, created) = service.Create(store, Data("a"), "Big Buck Bunny", Alice);

        _clock.Advance(StoreService.NotifyInterval + TimeSpan.FromSeconds(1));
        service.Delete(store, created!.Id, null);

        await Task.Delay(50);
        await _activityManager.Received(2).CreateAsync(Arg.Any<ActivityLog>());
    }

    [Fact]
    public async Task Notify_AddsAndRemovesShareOneWindow()
    {
        // One line saying what happened to the store, rather than two racing each other.
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define(max: 50);
        store.NotifyOnWrite = true;

        var (_, first) = service.Create(store, Data("a"), "one", Alice);
        service.Create(store, Data("b"), "two", Alice);
        service.Delete(store, first!.Id, null);

        await Task.Delay(50);
        await _activityManager.Received(1).CreateAsync(Arg.Any<ActivityLog>());
    }

    // MARK: File format

    private string StoreFile(string name = "downloads")
        => Path.Combine(_dataPath, "custompages", "stores", name + ".json");

    [Fact]
    public void Persist_WritesCompactJson()
    {
        // Every write rewrites the whole file, so indentation is pure overhead on each mutation.
        var store = Define();
        Create().Create(store, JsonNode.Parse("{\"nested\":{\"a\":[1,2]}}"), null, Alice);

        var text = File.ReadAllText(StoreFile());

        Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsAnIndentedFileFromAnEarlierVersion()
    {
        var store = Define();
        Directory.CreateDirectory(Path.GetDirectoryName(StoreFile())!);
        File.WriteAllText(StoreFile(), """
            [
              {
                "Id": "4752bb755ed34da79b118484d480288c",
                "CreatedUtc": "2026-01-01T00:00:00Z",
                "UpdatedUtc": "2026-01-01T00:00:00Z",
                "UserId": "11111111-1111-1111-1111-111111111111",
                "Label": "legacy",
                "Data": {
                  "url": "a"
                }
              }
            ]
            """);

        var service = Create();
        var records = service.Read(store, null);

        Assert.Single(records);
        Assert.Equal("legacy", records[0].Label);
        Assert.Equal("a", records[0].Data!["url"]!.GetValue<string>());

        // The next write carries the legacy record over into the compact form.
        service.Create(store, Data("b"), null, Alice);
        Assert.Equal(2, Create().Read(store, null).Count);
        Assert.DoesNotContain("\n", File.ReadAllText(StoreFile()), StringComparison.Ordinal);
    }

    // MARK: Total size budget

    // Writes a store file already holding just over the budget, so the budget can be exercised without
    // making thousands of writes that each rewrite the whole file.
    private PageStore DefineNearlyFull(out string firstId)
    {
        var store = Define(max: PageStore.RecordCeiling);
        var chunk = new string('x', 60 * 1024);
        var count = (int)(StoreService.MaxStoreBytes / (chunk.Length + 8)) + 1;
        var records = new System.Text.StringBuilder("[");
        firstId = "r0";
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                records.Append(',');
            }

            records.Append("{\"Id\":\"r").Append(i).Append("\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\",\"UpdatedUtc\":\"2026-01-01T00:00:00Z\",")
                .Append("\"UserId\":\"11111111-1111-1111-1111-111111111111\",\"Data\":{\"v\":\"").Append(chunk).Append("\"}}");
        }

        records.Append(']');
        Directory.CreateDirectory(Path.GetDirectoryName(StoreFile())!);
        File.WriteAllText(StoreFile(), records.ToString());
        return store;
    }

    [Fact]
    public void Create_PastTheStoreBudget_IsRefused()
    {
        var store = DefineNearlyFull(out _);
        var service = Create();
        var before = File.ReadAllBytes(StoreFile()).Length;

        var (result, record) = service.Create(store, Data("one more"), null, Alice);

        Assert.Equal(StoreWriteResult.OverBudget, result);
        Assert.Null(record);
        Assert.Equal(before, File.ReadAllBytes(StoreFile()).Length);
    }

    [Fact]
    public void Update_ThatGrowsAStorePastItsBudget_IsRefused()
    {
        var store = DefineNearlyFull(out var id);
        var service = Create();

        var (result, _) = service.Update(store, id, JsonNode.Parse("{\"v\":\"" + new string('y', 63 * 1024) + "\"}"), null, null);

        Assert.Equal(StoreWriteResult.OverBudget, result);
    }

    [Fact]
    public void Update_ThatShrinksARecord_IsAllowedEvenOverBudget()
    {
        var store = DefineNearlyFull(out var id);
        var service = Create();

        var (result, _) = service.Update(store, id, Data("small"), null, null);

        Assert.Equal(StoreWriteResult.Ok, result);
    }

    [Fact]
    public void Delete_FreesRoomInTheBudget()
    {
        var store = DefineNearlyFull(out _);
        var service = Create();
        Assert.Equal(StoreWriteResult.OverBudget, service.Create(store, Data("x"), null, Alice).Result);

        // Removing two records frees well over the overshoot.
        Assert.True(service.Delete(store, "r1", null));
        Assert.True(service.Delete(store, "r2", null));

        Assert.Equal(StoreWriteResult.Ok, service.Create(store, Data("x"), null, Alice).Result);
    }

    // MARK: Clearing

    [Fact]
    public async Task Clear_WritesAnActivityEntry()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define();
        service.Create(store, Data("a"), null, Alice);
        service.Create(store, Data("b"), null, Alice);
        store.NotifyOnWrite = true;

        Assert.Equal(2, service.Clear(store));

        await Task.Delay(50);
        await _activityManager.Received(1).CreateAsync(Arg.Is<ActivityLog>(l =>
            l.Name == "2 records were cleared from downloads" && l.Type == "CustomPages.StoreClear"));
    }

    [Fact]
    public async Task Clear_IsAnnouncedEvenInsideTheRateLimitWindow()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define();
        store.NotifyOnWrite = true;
        service.Create(store, Data("a"), null, Alice);

        service.Clear(store);

        await Task.Delay(50);
        await _activityManager.Received(2).CreateAsync(Arg.Any<ActivityLog>());
    }

    [Fact]
    public async Task Clear_OnAStoreThatDoesNotNotify_WritesNothing()
    {
        _activityManager = Substitute.For<IActivityManager>();
        var service = Create();
        var store = Define();
        service.Create(store, Data("a"), null, Alice);

        service.Clear(store);

        await Task.Delay(50);
        await _activityManager.DidNotReceive().CreateAsync(Arg.Any<ActivityLog>());
    }

    [Theory]
    [InlineData(1, "1 record was cleared from downloads")]
    [InlineData(5, "5 records were cleared from downloads")]
    public void ClearHeadline_ReadsAsASentence(int removed, string expected)
    {
        Assert.Equal(expected, StoreService.ClearHeadline("downloads", removed));
    }

    // MARK: Sweep cancellation

    [Fact]
    public void SweepAll_HonoursCancellation()
    {
        var store = Define();
        store.RetentionDays = 1;
        var service = Create();
        service.Create(store, Data("a"), null, Alice);
        _clock.Advance(TimeSpan.FromDays(2));

        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => service.SweepAll(cancelled.Token));

        // Nothing was swept, so the expired record is still on disk for the next run.
        Assert.Contains("\"a\"", File.ReadAllText(StoreFile()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetentionTask_PassesItsCancellationTokenToTheSweep()
    {
        var stores = Substitute.For<IStoreService>();
        var task = new Jellyfin.Plugin.CustomPages.Tasks.StoreRetentionTask(
            stores, NullLogger<Jellyfin.Plugin.CustomPages.Tasks.StoreRetentionTask>.Instance);
        using var source = new System.Threading.CancellationTokenSource();

        await task.ExecuteAsync(new Progress<double>(), source.Token);

        stores.Received(1).SweepAll(source.Token);
    }

    [Fact]
    public async Task RetentionTask_CancelledRun_StopsWithACancellation()
    {
        var store = Define();
        store.RetentionDays = 1;
        var task = new Jellyfin.Plugin.CustomPages.Tasks.StoreRetentionTask(
            Create(), NullLogger<Jellyfin.Plugin.CustomPages.Tasks.StoreRetentionTask>.Instance);
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.ExecuteAsync(new Progress<double>(), cancelled.Token));
    }
}
