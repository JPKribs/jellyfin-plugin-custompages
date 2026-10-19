using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// Resolves route passwords on save. The configuration page never sees a stored password: it posts back
/// <see cref="SecretProtector.KeptSentinel"/> for a field the administrator left alone, and this pairs
/// that field with the route it came from so the stored value is kept.
/// </summary>
/// <remarks>
/// Routes and pages carry no stable ID, so the pairing is by page slug and route name first. Renaming
/// either is an ordinary edit that still posts the sentinel, so when the name no longer matches, the
/// route is paired with a stored route the incoming configuration no longer names, provided that route
/// targets the same origin and the pairing is unambiguous. The origin check matters: a kept password is
/// sent to the route's target, so it is never carried over to a route pointing somewhere else. That
/// holds for a name match too, since the name alone says nothing about where the password will go: a
/// route whose target moved to another host, or a new route given a deleted route's name, has to have
/// its password entered again.
///
/// A sentinel that still cannot be paired is refused rather than resolved to an empty password. Saving
/// would otherwise silently strip the credential from a route the administrator never touched.
/// </remarks>
public static class RouteSecrets
{
    /// <summary>
    /// Replaces every route password in <paramref name="incoming"/> with the value to persist: the
    /// stored one when the field carried the sentinel, otherwise the new value encrypted.
    /// </summary>
    /// <param name="incoming">The configuration being saved. Its route passwords are rewritten in place.</param>
    /// <param name="stored">The configuration currently in effect.</param>
    /// <param name="secrets">The protector used to encrypt passwords.</param>
    /// <exception cref="ArgumentException">When a kept password cannot be paired with a stored route.</exception>
    public static void Protect(PluginConfiguration incoming, PluginConfiguration stored, SecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(secrets);

        // Every route is paired before any password is rewritten, so a refused save leaves the incoming
        // configuration exactly as it arrived.
        var pairs = Pair(incoming, stored, out var error);
        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        foreach (var (route, match) in pairs)
        {
            Resolve(route, match, secrets);
        }
    }

    /// <summary>
    /// Reports whether <see cref="Protect"/> would refuse a configuration, without changing it. The
    /// dashboard asks this before saving, because Jellyfin does not pass a refused save's reason back to
    /// the browser.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved.</param>
    /// <param name="stored">The configuration currently in effect.</param>
    /// <returns>The reason the save would be refused, or <c>null</c> when every kept password pairs.</returns>
    public static string? Check(PluginConfiguration incoming, PluginConfiguration stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);

        Pair(incoming, stored, out var error);
        return error;
    }

    // Pairs every incoming route with the stored route its password comes from, or null when it brings
    // its own. Reports the first kept password that cannot be paired.
    private static List<(PageApiRoute Route, PageApiRoute? Stored)> Pair(
        PluginConfiguration incoming,
        PluginConfiguration stored,
        out string? error)
    {
        var pairs = new List<(PageApiRoute Route, PageApiRoute? Stored)>();
        var refused = new List<(int PageIndex, int RouteIndex, string Message)>();
        var claimed = new HashSet<PageApiRoute>(ReferenceEqualityComparer.Instance);
        var pending = new List<(int PageIndex, int RouteIndex, CustomPage Page, PageApiRoute Route)>();

        // Every name match across every page goes first and claims its stored route, so a fallback below
        // can never hand one route's password to another route that merely sits where it used to, and a
        // refusal names the route that actually lost its password.
        for (var p = 0; p < incoming.Pages.Count; p++)
        {
            var page = incoming.Pages[p];
            if (page.ApiRoutes is null)
            {
                continue;
            }

            var storedPage = FindPage(stored, page.Slug);
            for (var r = 0; r < page.ApiRoutes.Count; r++)
            {
                var route = page.ApiRoutes[r];
                var match = storedPage?.ApiRoutes?.FirstOrDefault(s =>
                    string.Equals(s.Name, route.Name, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    claimed.Add(match);
                    if (IsKept(route.Password)
                        && !string.IsNullOrEmpty(match.Password)
                        && !SameOrigin(match.Url, route.Url))
                    {
                        refused.Add((p, r, Refusal(route, page)));
                    }

                    pairs.Add((route, match));
                }
                else
                {
                    pending.Add((p, r, page, route));
                }
            }
        }

        foreach (var (pageIndex, routeIndex, page, route) in pending)
        {
            var match = IsKept(route.Password)
                ? FindRenamed(incoming, stored, pageIndex, routeIndex, page, route, claimed)
                : null;

            if (match is not null)
            {
                claimed.Add(match);
            }
            else if (IsKept(route.Password))
            {
                refused.Add((pageIndex, routeIndex, Refusal(route, page)));
            }

            pairs.Add((route, match));
        }

        // Reported in the order the routes appear, so the message points at the first one to fix.
        error = refused
            .OrderBy(x => x.PageIndex)
            .ThenBy(x => x.RouteIndex)
            .Select(x => x.Message)
            .FirstOrDefault();
        return pairs;
    }

    private static string Refusal(PageApiRoute route, CustomPage page)
        => "The saved password for route '" + route.Name + "' on page '" + page.Slug
            + "' could not be carried over, because the route was renamed or now points to a different host. Enter the password again and save.";

    private static bool IsKept(string? value)
        => string.Equals(value, SecretProtector.KeptSentinel, StringComparison.Ordinal);

    // ResolveIncoming hands back the stored value untouched when the field carried the sentinel, and that
    // value is plaintext for anything saved before encryption existed. Protect no-ops on an already
    // encrypted or empty value, so wrapping the result migrates a legacy secret on the next save instead
    // of leaving it in the clear forever.
    private static void Resolve(PageApiRoute route, PageApiRoute? stored, SecretProtector secrets)
    {
        var kept = secrets.ResolveIncoming(route.Password, stored?.Password);
        route.Password = secrets.Protect(kept);
    }

    private static CustomPage? FindPage(PluginConfiguration config, string? slug)
        => config.Pages.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    // Finds the stored route a renamed route came from. Candidates are stored routes nothing in the
    // incoming configuration still names, on the same page when its slug survived, otherwise on stored
    // pages whose slug is gone, and only those aimed at the same origin. A route that kept its name on a
    // renamed page wins outright, then the one at the same position, then a lone candidate. Position only
    // counts while nothing was added or removed around it, since a deletion shifts every later entry onto
    // a neighbour's slot. Anything still ambiguous pairs with nothing and the save is refused.
    private static PageApiRoute? FindRenamed(
        PluginConfiguration incoming,
        PluginConfiguration stored,
        int pageIndex,
        int routeIndex,
        CustomPage page,
        PageApiRoute route,
        HashSet<PageApiRoute> claimed)
    {
        var candidates = new List<(bool Aligned, PageApiRoute Route)>();
        var routeCount = page.ApiRoutes?.Count ?? 0;
        for (var p = 0; p < stored.Pages.Count; p++)
        {
            var storedPage = stored.Pages[p];
            var samePage = string.Equals(storedPage.Slug, page.Slug, StringComparison.OrdinalIgnoreCase);
            if (!samePage && FindPage(incoming, storedPage.Slug) is not null)
            {
                continue;
            }

            var routes = storedPage.ApiRoutes ?? new List<PageApiRoute>();
            for (var r = 0; r < routes.Count; r++)
            {
                var candidate = routes[r];
                if (!claimed.Contains(candidate)
                    && !string.IsNullOrEmpty(candidate.Password)
                    && SameOrigin(candidate.Url, route.Url))
                {
                    var aligned = r == routeIndex
                        && routes.Count == routeCount
                        && (samePage || (p == pageIndex && stored.Pages.Count == incoming.Pages.Count));
                    candidates.Add((aligned, candidate));
                }
            }
        }

        var named = candidates
            .Where(c => string.Equals(c.Route.Name, route.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (named.Count > 0)
        {
            return named.Count == 1 ? named[0].Route : null;
        }

        var positioned = candidates.Where(c => c.Aligned).ToList();
        if (positioned.Count == 1)
        {
            return positioned[0].Route;
        }

        return candidates.Count == 1 ? candidates[0].Route : null;
    }

    private static bool SameOrigin(string? left, string? right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var a) || !Uri.TryCreate(right, UriKind.Absolute, out var b))
        {
            return false;
        }

        return Uri.Compare(a, b, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
