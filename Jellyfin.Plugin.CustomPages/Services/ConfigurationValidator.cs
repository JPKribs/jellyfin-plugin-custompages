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

            var allowed = page.AllowedUserIds;
            if (allowed is not null && allowed.Count > 0)
            {
                // An allow list on a tier that already serves everyone is not a weaker restriction, it
                // is no restriction at all. A page carrying something worth restricting would be
                // published to the world while the dashboard showed a tidy list of users beside it, so
                // refuse the combination rather than silently ignore the list.
                if (!page.Visibility.RequiresAuth())
                {
                    throw new ArgumentException(
                        "A page restricted to specific users must require sign in: " + page.Slug);
                }

                foreach (var entry in allowed)
                {
                    if (!Guid.TryParse(entry, out _))
                    {
                        throw new ArgumentException("Page has an invalid allowed user ID: " + page.Slug);
                    }
                }
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

            ValidateApiRoutes(page);
        }
    }

    private static void ValidateApiRoutes(CustomPage page)
    {
        if (page.ApiRoutes is null || page.ApiRoutes.Count == 0)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in page.ApiRoutes)
        {
            // The route name becomes a URL path segment and the serverFetch argument, so it is held to
            // the same safe set as a slug rather than allowed to carry path separators or spaces.
            if (!PageService.IsValidSlug(route.Name))
            {
                throw new ArgumentException("Invalid route name on page " + page.Slug + ": " + route.Name);
            }

            if (!names.Add(route.Name))
            {
                throw new ArgumentException("Duplicate route name on page " + page.Slug + ": " + route.Name);
            }

            // A fixed absolute http(s) target is what keeps this from being an open proxy. Reject anything
            // else rather than forward a caller to a scheme or a relative target it could exploit.
            if (!Uri.TryCreate(route.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Route " + route.Name + " on page " + page.Slug + " needs an absolute http or https URL.");
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
