using System;
using System.Collections.Generic;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Validates incoming plugin configuration before it is persisted. The dashboard enforces these rules
/// in the browser, but configuration can arrive from any API client, so they are enforced server side
/// as well.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>The largest decoded asset size accepted, matching the dashboard's upload cap.</summary>
    public const int MaxAssetBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Validates a configuration, throwing when it must not be persisted.
    /// </summary>
    /// <param name="config">The incoming configuration.</param>
    /// <exception cref="ArgumentException">When a reachable slug is duplicated or an asset is invalid.</exception>
    public static void Validate(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidatePages(config.Pages);
        ValidateAssets(config.Assets);
    }

    private static void ValidatePages(IEnumerable<CustomPage> pages)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            // Pages with empty or invalid slugs are unreachable drafts (Find rejects them and the
            // dashboard slugifies on save), so only reachable slugs are held to uniqueness.
            if (!PageService.IsValidSlug(page.Slug))
            {
                continue;
            }

            if (!seen.Add(page.Slug))
            {
                throw new ArgumentException("Duplicate page slug: " + page.Slug);
            }
        }
    }

    private static void ValidateAssets(IEnumerable<PageAsset> assets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (!PageService.IsValidAssetName(asset.Name))
            {
                throw new ArgumentException("Invalid asset name: " + asset.Name);
            }

            if (!seen.Add(asset.Name))
            {
                throw new ArgumentException("Duplicate asset name: " + asset.Name);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(asset.DataBase64);
            }
            catch (FormatException)
            {
                throw new ArgumentException("Asset data is not valid Base64: " + asset.Name);
            }

            if (bytes.Length > MaxAssetBytes)
            {
                throw new ArgumentException("Asset exceeds the 5 MB limit: " + asset.Name);
            }
        }
    }
}
