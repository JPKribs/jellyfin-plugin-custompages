using System;
using Jellyfin.Plugin.CustomPages.Models;

namespace Jellyfin.Plugin.CustomPages.Services;

/// <summary>
/// What a caller is allowed to reach in a store.
/// </summary>
public enum StoreAccessLevel
{
    /// <summary>The caller may not touch the store at all.</summary>
    None,

    /// <summary>The caller may only reach records they created.</summary>
    Own,

    /// <summary>The caller may reach every record in the store.</summary>
    All
}

/// <summary>
/// The identity behind a store request, reduced to the three things the access rules care about.
/// </summary>
/// <param name="Tier">The highest tier the caller satisfies.</param>
/// <param name="UserId">The caller's user ID, or <see cref="Guid.Empty"/> when it has no user identity.</param>
public readonly record struct StoreCaller(PageVisibility Tier, Guid UserId)
{
    /// <summary>Gets a caller with no credentials at all.</summary>
    public static StoreCaller Anonymous => new StoreCaller(PageVisibility.Anonymous, Guid.Empty);

    /// <summary>Gets a value indicating whether the caller can own records.</summary>
    public bool HasIdentity => UserId != Guid.Empty;
}

/// <summary>
/// Decides what a caller may do in a store. These are the only place the tiers are interpreted, so the
/// read, write, and delete endpoints cannot drift apart from each other.
/// </summary>
public static class StoreAccess
{
    /// <summary>
    /// Resolves how much of a store a caller may read.
    /// </summary>
    /// <remarks>
    /// The read tier is definitive. A caller below it reads nothing, and no other setting can let them
    /// back in, so what the tier says is what the store does. Scope then decides how much of it a
    /// caller who did clear the tier is shown.
    ///
    /// Administrators are the one exception, and a deliberate one: they see every record whatever the
    /// scope says, exactly as they are always admitted to a page restricted to specific users. That is
    /// what lets one store serve both halves of a workflow, and an administrator can read the store's
    /// file off disk anyway, so narrowing them here would only look like a restriction.
    /// </remarks>
    /// <param name="store">The store being read.</param>
    /// <param name="caller">The resolved caller.</param>
    /// <returns>The read access level.</returns>
    public static StoreAccessLevel ResolveRead(PageStore store, StoreCaller caller)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!Enum.IsDefined(store.ReadAccess)
            || !Enum.IsDefined(store.ReadScope)
            || !Enum.IsDefined(caller.Tier)
            || !caller.Tier.Covers(store.ReadAccess))
        {
            return StoreAccessLevel.None;
        }

        if (store.ReadScope == StoreScope.All || caller.Tier == PageVisibility.Admin)
        {
            return StoreAccessLevel.All;
        }

        // Own scope needs somebody to own records against. A caller with no identity owns nothing, so
        // it is shown nothing rather than everything.
        return caller.HasIdentity ? StoreAccessLevel.Own : StoreAccessLevel.None;
    }

    /// <summary>
    /// Resolves whether a caller may create a record. Creation is governed only by the write tier,
    /// since a new record has no owner to compare against yet.
    /// </summary>
    /// <param name="store">The store being written.</param>
    /// <param name="caller">The resolved caller.</param>
    /// <returns><c>true</c> when the caller may create a record.</returns>
    public static bool CanCreate(PageStore store, StoreCaller caller)
    {
        ArgumentNullException.ThrowIfNull(store);

        return Enum.IsDefined(store.WriteAccess)
            && Enum.IsDefined(caller.Tier)
            && caller.Tier.Covers(store.WriteAccess);
    }

    /// <summary>
    /// Resolves how much of a store a caller may modify or delete.
    /// </summary>
    /// <remarks>
    /// Changing a record needs both tiers. The write tier is what admits a caller to the store at all,
    /// and the read tier is what decides which records are theirs to touch, so a caller can never
    /// rewrite something the store would refuse to show them. Editing your own record is the narrower
    /// grant on top, and it is off by default.
    /// </remarks>
    /// <param name="store">The store being modified.</param>
    /// <param name="caller">The resolved caller.</param>
    /// <returns>The modify access level.</returns>
    public static StoreAccessLevel ResolveModify(PageStore store, StoreCaller caller)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!CanCreate(store, caller))
        {
            return StoreAccessLevel.None;
        }

        var read = ResolveRead(store, caller);
        if (read == StoreAccessLevel.All)
        {
            return StoreAccessLevel.All;
        }

        return read == StoreAccessLevel.Own && store.AllowEditOwn && caller.HasIdentity
            ? StoreAccessLevel.Own
            : StoreAccessLevel.None;
    }

    /// <summary>
    /// Returns the owner a query must be narrowed to for an access level, or <c>null</c> for no filter.
    /// </summary>
    /// <param name="level">The resolved access level.</param>
    /// <param name="caller">The resolved caller.</param>
    /// <returns>The owner to filter on, or <c>null</c> when every record is in scope.</returns>
    public static Guid? OwnerFilter(StoreAccessLevel level, StoreCaller caller)
        => level == StoreAccessLevel.Own ? caller.UserId : null;
}
