using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using JPKribs.Jellyfin.Base;

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
    /// <param name="language">The language of the refusal, such as de, or <c>null</c> for US English.</param>
    /// <exception cref="ArgumentException">When a reachable slug is duplicated or an asset is invalid.</exception>
    public static void Validate(PluginConfiguration config, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidatePages(config.Pages, language);
        ValidateAssets(config.Assets, language);
        ValidateStores(config.Stores, language);
    }

    /// <summary>
    /// Builds a refusal in the administrator's language, since the Pages tab shows it as the reason a save failed.
    /// </summary>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <param name="key">The refusal's key.</param>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The exception to throw.</returns>
    private static ArgumentException Refuse(string? language, string key, params object?[] args)
        => new(BaseTranslations.Format(key, language, args));

    private static void ValidatePages(IEnumerable<CustomPage> pages, string? language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            // An out of range tier is not merely cosmetic: it is not equal to User or Admin, so the tier
            // gated endpoints refuse it, yet it compares as outranking every tier when deciding which
            // gated assets a page may embed. Reject it rather than reason about a value that has no tier.
            if (!Enum.IsDefined(page.Visibility))
            {
                throw Refuse(language, TranslationKeys.ErrorPageTier, page.Slug);
            }

            var allowed = page.AllowedUserIds;
            if (allowed is not null && allowed.Count > 0)
            {
                // An allow list only means something on the User tier. On Anonymous the page already
                // serves everyone, and on Admin every viewer is an administrator and administrators are
                // admitted unconditionally, so the list would be ignored at serve time while the
                // dashboard showed a tidy set of users beside it. Refuse the combination rather than
                // let the two disagree.
                if (page.Visibility != PageVisibility.User)
                {
                    throw Refuse(language, TranslationKeys.ErrorPageAllowList, page.Slug);
                }

                foreach (var entry in allowed)
                {
                    if (!Guid.TryParse(entry, out _))
                    {
                        throw Refuse(language, TranslationKeys.ErrorPageUserId, page.Slug);
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
                throw Refuse(language, TranslationKeys.ErrorPageDuplicate, page.Slug);
            }

            ValidateApiRoutes(page, language);
        }
    }

    private static void ValidateApiRoutes(CustomPage page, string? language)
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
                throw Refuse(language, TranslationKeys.ErrorRouteName, page.Slug, route.Name);
            }

            if (!names.Add(route.Name))
            {
                throw Refuse(language, TranslationKeys.ErrorRouteDuplicate, page.Slug, route.Name);
            }

            // A fixed absolute http(s) target is what keeps this from being an open proxy. Reject anything
            // else rather than forward a caller to a scheme or a relative target it could exploit.
            if (!Uri.TryCreate(route.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw Refuse(language, TranslationKeys.ErrorRouteUrl, route.Name, page.Slug);
            }
        }
    }

    private static void ValidateAssets(IEnumerable<PageAsset> assets, string? language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (!PageService.IsValidAssetName(asset.Name))
            {
                throw Refuse(language, TranslationKeys.ErrorAssetName, asset.Name);
            }

            if (!seen.Add(asset.Name))
            {
                throw Refuse(language, TranslationKeys.ErrorAssetDuplicate, asset.Name);
            }

            if (!Enum.IsDefined(asset.Visibility))
            {
                throw Refuse(language, TranslationKeys.ErrorAssetTier, asset.Name);
            }

            // The content type is echoed into a response header when the asset is served and into a
            // data: URI when it is embedded. Restricting it to a bare type/subtype keeps control
            // characters out of the header and quotes out of the dashboard's preview markup.
            if (asset.ContentType is null || !ContentTypePattern().IsMatch(asset.ContentType))
            {
                throw Refuse(language, TranslationKeys.ErrorAssetType, asset.Name);
            }

            if (asset.DataBase64 is null)
            {
                throw Refuse(language, TranslationKeys.ErrorAssetMissing, asset.Name);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(asset.DataBase64);
            }
            catch (FormatException)
            {
                throw Refuse(language, TranslationKeys.ErrorAssetBase64, asset.Name);
            }

            if (bytes.Length > MaxAssetBytes)
            {
                throw Refuse(language, TranslationKeys.ErrorAssetSize, asset.Name);
            }
        }
    }

    private static void ValidateStores(IEnumerable<PageStore> stores, string? language)
    {
        if (stores is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in stores)
        {
            // The name is both a URL segment and the store's file name under the data directory, so it
            // is held to the same safe set as a slug rather than allowed to carry separators.
            if (!StoreService.IsValidStoreName(store.Name))
            {
                throw Refuse(language, TranslationKeys.ErrorStoreName, store.Name);
            }

            if (!seen.Add(store.Name))
            {
                throw Refuse(language, TranslationKeys.ErrorStoreDuplicate, store.Name);
            }

            // An out of range tier is refused for the same reason a page's is. It matches no tier the
            // endpoints check, yet it compares as outranking every tier when access is resolved.
            if (!Enum.IsDefined(store.ReadAccess))
            {
                throw Refuse(language, TranslationKeys.ErrorStoreReadTier, store.Name);
            }

            if (!Enum.IsDefined(store.WriteAccess))
            {
                throw Refuse(language, TranslationKeys.ErrorStoreWriteTier, store.Name);
            }

            if (!Enum.IsDefined(store.ReadScope))
            {
                throw Refuse(language, TranslationKeys.ErrorStoreScope, store.Name);
            }

            if (store.MaxRecords < 1 || store.MaxRecords > PageStore.RecordCeiling)
            {
                throw Refuse(language, TranslationKeys.ErrorStoreLimit, PageStore.RecordCeiling, store.Name);
            }

            // Zero is the documented "keep forever". A negative retention would expire every record
            // the moment it was written, which is a data loss bug wearing a configuration value.
            if (store.RetentionDays < 0 || store.RetentionDays > PageStore.RetentionDayCeiling)
            {
                throw Refuse(language, TranslationKeys.ErrorStoreRetention, PageStore.RetentionDayCeiling, store.Name);
            }
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9!#$&^_.+-]+/[A-Za-z0-9!#$&^_.+-]+$")]
    private static partial Regex ContentTypePattern();
}
