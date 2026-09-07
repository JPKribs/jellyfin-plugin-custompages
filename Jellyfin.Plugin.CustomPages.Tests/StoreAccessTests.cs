using System;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for <see cref="StoreAccess"/>, the single place a store's tiers are interpreted. Every store
/// endpoint routes its decision through these, so a gap here is a gap in all of them.
/// </summary>
public class StoreAccessTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static PageStore Store(
        PageVisibility read = PageVisibility.Admin,
        PageVisibility write = PageVisibility.User,
        StoreScope scope = StoreScope.All,
        bool editOwn = false)
        => new PageStore
        {
            Name = "downloads",
            ReadAccess = read,
            ReadScope = scope,
            WriteAccess = write,
            AllowEditOwn = editOwn
        };

    private static StoreCaller User(Guid id) => new StoreCaller(PageVisibility.User, id);

    private static StoreCaller Admin(Guid id) => new StoreCaller(PageVisibility.Admin, id);

    // MARK: The read tier is definitive

    [Fact]
    public void ResolveRead_BelowTheReadTier_ReadsNothing()
    {
        // The whole point of the tier being definitive. A user who can write is still refused every
        // record when the read tier is above them, and no other setting can readmit them.
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(Store(), User(Alice)));
    }

    [Fact]
    public void ResolveRead_BelowTheReadTier_ReadsNothingEvenWithOwnScope()
    {
        var store = Store(read: PageVisibility.Admin, scope: StoreScope.Own);
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(store, User(Alice)));
    }

    [Fact]
    public void ResolveRead_AnonymousCallerOnAGatedStore_ReadsNothing()
    {
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(Store(), StoreCaller.Anonymous));
    }

    // MARK: Scope, once the tier is cleared

    [Fact]
    public void ResolveRead_AtTheTierWithAllScope_ReadsEverything()
    {
        var store = Store(read: PageVisibility.User, scope: StoreScope.All);
        Assert.Equal(StoreAccessLevel.All, StoreAccess.ResolveRead(store, User(Alice)));
    }

    [Fact]
    public void ResolveRead_AtTheTierWithOwnScope_ReadsOnlyTheirOwn()
    {
        // This is the submission queue shape: everyone signed in may call, and each sees their own row.
        var store = Store(read: PageVisibility.User, scope: StoreScope.Own);
        Assert.Equal(StoreAccessLevel.Own, StoreAccess.ResolveRead(store, User(Alice)));
    }

    [Fact]
    public void ResolveRead_AdministratorIgnoresOwnScope()
    {
        // What lets one store serve both halves of a workflow. An administrator can read the file off
        // disk regardless, so narrowing them would only look like a restriction.
        var store = Store(read: PageVisibility.User, scope: StoreScope.Own);
        Assert.Equal(StoreAccessLevel.All, StoreAccess.ResolveRead(store, Admin(Bob)));
    }

    [Fact]
    public void ResolveRead_OwnScopeShowsAnUnidentifiedCallerNothing()
    {
        // An anonymous caller owns no records, so own scope must resolve to nothing rather than
        // falling through to everything.
        var store = Store(read: PageVisibility.Anonymous, scope: StoreScope.Own);
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(store, StoreCaller.Anonymous));
    }

    [Fact]
    public void ResolveRead_AnonymousStoreWithAllScope_ReadsEverythingUnauthenticated()
    {
        var store = Store(read: PageVisibility.Anonymous, write: PageVisibility.Anonymous);
        Assert.Equal(StoreAccessLevel.All, StoreAccess.ResolveRead(store, StoreCaller.Anonymous));
    }

    [Fact]
    public void ResolveRead_UndefinedStoreTier_ReadsNothing()
    {
        var store = Store();
        store.ReadAccess = (PageVisibility)99;
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(store, Admin(Alice)));
    }

    [Fact]
    public void ResolveRead_UndefinedScope_ReadsNothing()
    {
        var store = Store();
        store.ReadScope = (StoreScope)99;
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(store, Admin(Alice)));
    }

    // MARK: Creating

    [Fact]
    public void CanCreate_AtWriteTier_Allowed()
    {
        Assert.True(StoreAccess.CanCreate(Store(), User(Alice)));
    }

    [Fact]
    public void CanCreate_BelowWriteTier_Refused()
    {
        Assert.False(StoreAccess.CanCreate(Store(), StoreCaller.Anonymous));
    }

    [Fact]
    public void CanCreate_DoesNotRequireReadAccess()
    {
        // A write only drop box is a legitimate store: submit a record, never read the store back.
        var store = Store(read: PageVisibility.Admin, write: PageVisibility.User);
        Assert.True(StoreAccess.CanCreate(store, User(Alice)));
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveRead(store, User(Alice)));
    }

    [Fact]
    public void CanCreate_UndefinedWriteTier_Refused()
    {
        var store = Store();
        store.WriteAccess = (PageVisibility)99;
        Assert.False(StoreAccess.CanCreate(store, Admin(Alice)));
    }

    // MARK: Modifying

    [Fact]
    public void ResolveModify_AdministratorModifiesEverything()
    {
        Assert.Equal(StoreAccessLevel.All, StoreAccess.ResolveModify(Store(), Admin(Alice)));
    }

    [Fact]
    public void ResolveModify_WithoutReadAccess_ModifiesNothing()
    {
        // Write access alone is not enough. A caller the store refuses to show a record to has no
        // business rewriting it by ID.
        var store = Store(read: PageVisibility.Admin, write: PageVisibility.User, editOwn: true);
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveModify(store, User(Alice)));
    }

    [Fact]
    public void ResolveModify_OwnScopeWithoutEditOwn_ModifiesNothing()
    {
        // The shape that protects a server assigned status: the submitter reads their row and cannot
        // rewrite what an administrator put on it.
        var store = Store(read: PageVisibility.User, scope: StoreScope.Own);
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveModify(store, User(Alice)));
    }

    [Fact]
    public void ResolveModify_OwnScopeWithEditOwn_ModifiesOnlyTheirOwn()
    {
        var store = Store(read: PageVisibility.User, scope: StoreScope.Own, editOwn: true);
        Assert.Equal(StoreAccessLevel.Own, StoreAccess.ResolveModify(store, User(Alice)));
    }

    [Fact]
    public void ResolveModify_WithoutWriteTier_ModifiesNothing()
    {
        var store = Store(read: PageVisibility.User, write: PageVisibility.Admin, editOwn: true);
        Assert.Equal(StoreAccessLevel.None, StoreAccess.ResolveModify(store, User(Alice)));
    }

    [Fact]
    public void ResolveModify_SharedUserStore_ModifiesEverything()
    {
        var store = Store(read: PageVisibility.User, write: PageVisibility.User);
        Assert.Equal(StoreAccessLevel.All, StoreAccess.ResolveModify(store, User(Alice)));
    }

    // MARK: Owner filtering

    [Fact]
    public void OwnerFilter_OwnLevel_NarrowsToCaller()
    {
        Assert.Equal(Bob, StoreAccess.OwnerFilter(StoreAccessLevel.Own, User(Bob)));
    }

    [Fact]
    public void OwnerFilter_AllLevel_NoFilter()
    {
        Assert.Null(StoreAccess.OwnerFilter(StoreAccessLevel.All, User(Bob)));
    }
}
