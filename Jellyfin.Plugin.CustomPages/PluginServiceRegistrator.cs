using System;
using Jellyfin.Plugin.CustomPages.Services;
using Jellyfin.Plugin.CustomPages.Utilities;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomPages;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PageService>();
        serviceCollection.AddSingleton<IPageService>(provider => provider.GetRequiredService<PageService>());

        // Writes store notices to Jellyfin's activity log, which is where the dashboard surfaces plugin
        // events to administrators.
        serviceCollection.AddSingleton(sp => new ActivityLogger(
            sp.GetRequiredService<IActivityManager>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ActivityLogger>()));

        // Holds each store's records in memory behind its own gate, so it has to be a singleton. Two
        // instances would each believe they owned the file and the later write would drop the other's
        // records.
        serviceCollection.AddSingleton<StoreService>();
        serviceCollection.AddSingleton<IStoreService>(provider => provider.GetRequiredService<StoreService>());

        // Encrypts route credentials at rest. Keys live in a fixed directory under the Jellyfin data
        // folder with a pinned application name, so a host launch context change does not shift the Data
        // Protection discriminator and leave stored secrets undecryptable.
        serviceCollection.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Jellyfin.Plugin.CustomPages.SecretProtector");
            IDataProtectionProvider? provider = null;

            var paths = sp.GetService<IApplicationPaths>();
            if (paths is not null)
            {
                var keyDirectory = System.IO.Path.Combine(
                    paths.PluginConfigurationsPath, "Jellyfin.Plugin.CustomPages.Keys");
                provider = StableSecretProtection.Build(keyDirectory, logger);
            }

            return new SecretProtector("Jellyfin.Plugin.CustomPages.Secrets.v1", logger, provider);
        });

        // Applies store retention on a schedule, for the stores nobody reads or writes.
        serviceCollection.AddSingleton<IScheduledTask, Tasks.StoreRetentionTask>();

        // Named client used by the per page server side routes. HandlerLifetime caps DNS staleness for
        // the long lived plugin process.
        serviceCollection
            .AddHttpClient(RouteProxy.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
    }
}
