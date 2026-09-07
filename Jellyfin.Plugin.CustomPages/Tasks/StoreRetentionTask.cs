using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.CustomPages.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomPages.Tasks;

/// <summary>
/// Applies each store's retention policy on a schedule.
/// </summary>
/// <remarks>
/// Reading or writing a store already drops its expired records, so this exists for the stores nobody
/// touches. Without it, a store that stopped receiving submissions would keep its last records forever
/// no matter what retention said, which is exactly the case an administrator sets a retention for.
/// </remarks>
public class StoreRetentionTask : PluginScheduledTask
{
    private readonly IStoreService _stores;
    private readonly ILogger<StoreRetentionTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreRetentionTask"/> class.
    /// </summary>
    /// <param name="stores">The record store service.</param>
    /// <param name="logger">The logger.</param>
    public StoreRetentionTask(IStoreService stores, ILogger<StoreRetentionTask> logger)
    {
        _stores = stores;
        _logger = logger;
    }

    /// <inheritdoc />
    public override string Name => "Apply Custom Pages store retention";

    /// <inheritdoc />
    public override string Key => "CustomPagesStoreRetention";

    /// <inheritdoc />
    public override string Description =>
        "Removes records that have passed the retention set on their store. Stores set to keep records permanently are left alone.";

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var removed = _stores.SweepAll();
        if (removed > 0)
        {
            _logger.LogInformation("Custom Pages: retention removed {Count} expired store records", removed);
        }

        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Retention is expressed in days, so a six hourly pass is far finer than the policy it enforces
        // and keeps the task cheap. The sweep on read means nothing expired is ever served in between.
        yield return EveryInterval(TimeSpan.FromHours(6));
    }
}
