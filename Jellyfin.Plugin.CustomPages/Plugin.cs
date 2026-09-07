using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using Jellyfin.Plugin.CustomPages.Utilities;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomPages;

/// <summary>
/// Main plugin entry point for Custom Pages.
/// </summary>
public class Plugin : PluginBase<Plugin, PluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The XML serializer.</param>
    /// <param name="logger">The logger.</param>
    private readonly Lazy<SecretProtector> _secrets;

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _secrets = new Lazy<SecretProtector>(() => CreateSecretProtector(applicationPaths, logger));
        logger.LogInformation("Custom Pages plugin initialized");
    }

    /// <summary>Gets the protector used to read route credentials back at call time.</summary>
    public SecretProtector Secrets => _secrets.Value;

    /// <inheritdoc />
    public override string Name => "Custom Pages";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("409ef72d-6014-47fd-8928-ebad581bf81b");

    /// <inheritdoc />
    public override string Description => "Author and serve authorization-gated pages at /pages/{slug}.";

    /// <summary>
    /// Validates incoming configuration before persisting it. The dashboard enforces the same rules in
    /// the browser, but configuration can arrive from any API client, so reachable slugs must be unique
    /// and assets must be valid Base64 under the size cap before they are allowed.
    /// </summary>
    /// <param name="configuration">The incoming configuration.</param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration config)
        {
            ConfigurationValidator.Validate(config);
            ProtectRouteSecrets(config);
        }

        base.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Encrypts route credentials before they are written, and keeps the stored one when the config page
    /// posts back the sentinel rather than a replacement. The real credential therefore never has to be
    /// sent to a browser and is never written to disk in the clear.
    /// </summary>
    /// <param name="config">The incoming configuration.</param>
    private void ProtectRouteSecrets(PluginConfiguration config)
    {
        foreach (var page in config.Pages)
        {
            if (page.ApiRoutes is null || page.ApiRoutes.Count == 0)
            {
                continue;
            }

            var storedPage = Configuration.Pages.FirstOrDefault(p =>
                string.Equals(p.Slug, page.Slug, StringComparison.OrdinalIgnoreCase));

            foreach (var route in page.ApiRoutes)
            {
                var stored = storedPage?.ApiRoutes?.FirstOrDefault(r =>
                    string.Equals(r.Name, route.Name, StringComparison.OrdinalIgnoreCase));

                // ResolveIncoming hands back the stored value untouched when the field carried the
                // sentinel, and that value is plaintext for anything saved before encryption existed.
                // Protect no-ops on an already encrypted or empty value, so wrapping the result migrates
                // a legacy secret on the next save instead of leaving it in the clear forever.
                var kept = _secrets.Value.ResolveIncoming(route.Password, stored?.Password);
                route.Password = _secrets.Value.Protect(kept);
            }
        }
    }

    private static SecretProtector CreateSecretProtector(IApplicationPaths paths, ILogger logger)
    {
        var keyDirectory = Path.Join(paths.PluginConfigurationsPath, "Jellyfin.Plugin.CustomPages.Keys");
        var provider = StableSecretProtection.Build(keyDirectory, logger);
        return new SecretProtector("Jellyfin.Plugin.CustomPages.Secrets.v1", logger, provider);
    }

    /// <inheritdoc />
    public override IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = typeof(Plugin).Namespace;

        // Tab 1: Pages (the dashboard menu entry).
        yield return new PluginPageInfo
        {
            Name = "custompages_pages",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_pages.html",
            MenuSection = "server",
            DisplayName = "Custom Pages",
            EnableInMainMenu = false
        };

        yield return new PluginPageInfo
        {
            Name = "custompages_pages.js",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_pages.js"
        };

        // Tab 2: Assets.
        yield return new PluginPageInfo
        {
            Name = "custompages_assets",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_assets.html"
        };

        yield return new PluginPageInfo
        {
            Name = "custompages_assets.js",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_assets.js"
        };

        // Shared base CSS and JS compiled in from the JPKribs.Jellyfin.Base package.
        foreach (var page in GetSharedPages("custompages"))
        {
            yield return page;
        }
    }
}
