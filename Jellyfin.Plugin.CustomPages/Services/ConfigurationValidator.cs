using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Validates incoming plugin configuration before it is persisted. The dashboard enforces these rules
/// in the browser, but configuration can arrive from any API client, so they are enforced server side
/// as well.
/// </summary>
public static partial class ConfigurationValidator
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
            // An out of range tier is not merely cosmetic: it is not equal to User or Admin, so the tier
            // gated endpoints refuse it, yet it compares as outranking every tier when deciding which
            // gated assets a page may embed. Reject it rather than reason about a value that has no tier.
            if (!Enum.IsDefined(page.Visibility))
            {
                throw new ArgumentException("Page has an unknown visibility tier: " + page.Slug);
            }

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

            if (!Enum.IsDefined(asset.Visibility))
            {
                throw new ArgumentException("Asset has an unknown visibility tier: " + asset.Name);
            }

            // The content type is echoed into a response header when the asset is served and into a
            // data: URI when it is embedded. Restricting it to a bare type/subtype keeps control
            // characters out of the header and quotes out of the dashboard's preview markup.
            if (asset.ContentType is null || !ContentTypePattern().IsMatch(asset.ContentType))
            {
                throw new ArgumentException(
                    "Asset content type must be a bare type/subtype such as image/png: " + asset.Name);
            }

            if (asset.DataBase64 is null)
            {
                throw new ArgumentException("Asset data is missing: " + asset.Name);
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

    [GeneratedRegex(@"^[A-Za-z0-9!#$&^_.+-]+/[A-Za-z0-9!#$&^_.+-]+$")]
    private static partial Regex ContentTypePattern();
}
