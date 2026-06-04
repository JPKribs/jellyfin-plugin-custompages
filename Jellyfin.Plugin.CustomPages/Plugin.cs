using System;
using System.Collections.Generic;
using Jellyfin.Plugin.CustomPages.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomPages;

/// <summary>
/// Main plugin entry point for Custom Pages.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static readonly object ConfigLock = new();

    private readonly ILogger<Plugin> _logger;

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
        Instance = this;
        _logger = logger;

        _logger.LogInformation("Custom Pages plugin initialized");
    }

    /// <inheritdoc />
    public override string Name => "Custom Pages";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("409ef72d-6014-47fd-8928-ebad581bf81b");

    /// <inheritdoc />
    public override string Description => "Author and serve authorization-gated pages at /pages/{slug}.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Atomically mutates and (optionally) persists the configuration under a process-wide lock.
    /// </summary>
    /// <param name="mutate">Mutation to apply; return <c>true</c> to persist the change.</param>
    public void MutateConfiguration(Func<PluginConfiguration, bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (ConfigLock)
        {
            if (mutate(Configuration))
            {
                SaveConfiguration();
            }
        }
    }

    /// <summary>
    /// Reads from the configuration under the same lock (safe against concurrent mutation).
    /// </summary>
    /// <typeparam name="T">The read result type.</typeparam>
    /// <param name="read">The read projection.</param>
    /// <returns>The projected value.</returns>
    public T ReadConfiguration<T>(Func<PluginConfiguration, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (ConfigLock)
        {
            return read(Configuration);
        }
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = typeof(Plugin).Namespace;

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

        yield return new PluginPageInfo
        {
            Name = "custompages_shared.css",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_shared.css"
        };

        yield return new PluginPageInfo
        {
            Name = "custompages_shared.js",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_shared.js"
        };
    }
}
