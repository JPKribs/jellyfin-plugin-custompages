using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// Single configuration object for the plugin. XML-serialized by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the custom pages served by the plugin.</summary>
    public List<CustomPage> Pages { get; set; } = new();

    /// <summary>Gets or sets the hosted image assets, served at <c>/pages/asset/{name}</c>.</summary>
    public List<PageAsset> Assets { get; set; } = new();
}
