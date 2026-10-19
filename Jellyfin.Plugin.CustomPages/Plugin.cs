using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The name the route credential keys are derived from: the key folder, the Data Protection
    /// application name, and the purpose. Changing it makes every stored credential unreadable.
    /// </summary>
    public const string SecretsName = "Jellyfin.Plugin.CustomPages";

    private readonly Lazy<SecretProtector> _secrets;

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
        _secrets = new Lazy<SecretProtector>(() => StableSecretProtection.CreateProtector(applicationPaths, SecretsName, logger));
        logger.LogInformation("Custom Pages plugin initialized");
    }

    /// <summary>Gets the protector used to read route credentials back at call time.</summary>
    public SecretProtector Secrets => _secrets.Value;

    /// <inheritdoc />
    public override string Name => "Custom Pages";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("409ef72d-6014-47fd-8928-ebad581bf81b");

    /// <inheritdoc />
    public override string Description => "Author and serve authorization-gated HTML pages at /pages/{slug}.";

    /// <summary>
    /// Validates incoming configuration before persisting it. The dashboard enforces the same rules in
    /// the browser, but configuration can arrive from any API client, so reachable slugs must be unique
    /// and assets must be valid Base64 under the size cap before they are allowed.
    /// </summary>
    /// <param name="incoming">The incoming configuration.</param>
    /// <param name="stored">The configuration currently in effect.</param>
    protected override void OnConfigurationSaving(PluginConfiguration incoming, PluginConfiguration stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);

        ConfigurationValidator.Validate(incoming);
        ProtectRouteSecrets(incoming, stored);
    }

    /// <summary>
    /// Encrypts route credentials before they are written, and keeps the stored one when the config page
    /// posts back the sentinel rather than a replacement. The real credential therefore never has to be
    /// sent to a browser and is never written to disk in the clear.
    /// </summary>
    /// <param name="config">The incoming configuration.</param>
    /// <param name="storedConfig">The configuration currently in effect.</param>
    private void ProtectRouteSecrets(PluginConfiguration config, PluginConfiguration storedConfig)
        => RouteSecrets.Protect(config, storedConfig, _secrets.Value);

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

        // Tab 3: Stores.
        yield return new PluginPageInfo
        {
            Name = "custompages_stores",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_stores.html"
        };

        yield return new PluginPageInfo
        {
            Name = "custompages_stores.js",
            EmbeddedResourcePath = $"{ns}.Configuration.custompages_stores.js"
        };

        // Shared base CSS and JS compiled in from the JPKribs.Jellyfin.Base package.
        foreach (var page in GetSharedPages("custompages"))
        {
            yield return page;
        }
    }
}
