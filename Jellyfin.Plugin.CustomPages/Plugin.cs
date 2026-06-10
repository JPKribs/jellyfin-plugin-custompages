using System;
using System.Collections.Generic;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
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
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.LogInformation("Custom Pages plugin initialized");
    }

    /// <inheritdoc />
    public override string Name => "Custom Pages";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("409ef72d-6014-47fd-8928-ebad581bf81b");

    /// <inheritdoc />
    public override string Description => "Author and serve authorization-gated pages at /pages/{slug}.";

    /// <summary>
    /// Validates incoming configuration before persisting it. The dashboard enforces the same rules in
    /// the browser, but configuration can arrive from any API client, so reachable slugs must be unique
    /// and assets must be valid Base64 under the size cap before they are accepted.
    /// </summary>
    /// <param name="configuration">The incoming configuration.</param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration config)
        {
            ConfigurationValidator.Validate(config);
        }

        base.UpdateConfiguration(configuration);
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
