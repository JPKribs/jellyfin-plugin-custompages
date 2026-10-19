using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using JPKribs.Jellyfin.Base;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for how a route credential is resolved on save, covering the sentinel round trip and the
/// upgrade of a value stored before encryption existed.
/// </summary>
public class RouteSecretTests : IDisposable
{
    private readonly string _keyDirectory;
    private readonly SecretProtector _secrets;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteSecretTests"/> class with a real provider
    /// backed by a temporary key directory. Without one the protector degrades to a documented no-op,
    /// which would make every encryption assertion vacuous.
    /// </summary>
    public RouteSecretTests()
    {
        _keyDirectory = Path.Combine(Path.GetTempPath(), "custompages-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_keyDirectory);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(_keyDirectory));
        _secrets = new SecretProtector("Test.Secrets.v1", NullLogger.Instance, provider);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_keyDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    // Mirrors the resolution Plugin.OnConfigurationSaving applies to every route password.
    private string Resolve(string? incoming, string? stored)
        => _secrets.Protect(_secrets.ResolveIncoming(incoming, stored));

    [Fact]
    public void UntouchedField_KeepsTheStoredSecret()
    {
        var stored = _secrets.Protect("original");

        var result = Resolve(SecretProtector.KeptSentinel, stored);

        Assert.Equal("original", _secrets.Unprotect(result));
    }

    [Fact]
    public void UntouchedField_UpgradesALegacyPlaintextSecret()
    {
        // A value written before encryption existed must not survive a save still in the clear.
        var result = Resolve(SecretProtector.KeptSentinel, "legacy-plaintext");

        Assert.True(SecretProtector.IsProtected(result));
        Assert.Equal("legacy-plaintext", _secrets.Unprotect(result));
    }

    [Fact]
    public void ReplacementValue_IsEncrypted()
    {
        var result = Resolve("brand-new", _secrets.Protect("original"));

        Assert.True(SecretProtector.IsProtected(result));
        Assert.Equal("brand-new", _secrets.Unprotect(result));
    }

    [Fact]
    public void EmptyValue_ClearsTheSecret()
    {
        var result = Resolve(string.Empty, _secrets.Protect("original"));

        Assert.Empty(result);
    }

    [Fact]
    public void AProtectedValueIsNotTheSecretInTheClear()
    {
        var result = Resolve("hunter2", null);

        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
    }

    // MARK: Pairing kept passwords across renames

    private static PageApiRoute Route(string name, string url, string password)
        => new PageApiRoute { Name = name, Url = url, Password = password };

    private static CustomPage PageWith(string slug, params PageApiRoute[] routes)
        => new CustomPage { Slug = slug, ApiRoutes = new List<PageApiRoute>(routes) };

    private static PluginConfiguration Config(params CustomPage[] pages)
        => new PluginConfiguration { Pages = new List<CustomPage>(pages) };

    private string Saved(PluginConfiguration config, int page, int route)
        => _secrets.Unprotect(config.Pages[page].ApiRoutes[route].Password);

    private const string Kept = SecretProtector.KeptSentinel;

    [Fact]
    public void Protect_UnchangedRoute_KeepsTheStoredSecret()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", Kept)));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("pw", Saved(incoming, 0, 0));
    }

    [Fact]
    public void Protect_RenamedSlug_KeepsTheStoredSecret()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("transmission", Route("rpc", "http://nas:9091/rpc", Kept)));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("pw", Saved(incoming, 0, 0));
    }

    [Fact]
    public void Protect_RenamedRoute_KeepsTheStoredSecret()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx", Route("session", "http://nas:9091/rpc", Kept)));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("pw", Saved(incoming, 0, 0));
    }

    [Fact]
    public void Protect_RenamedSlugAndRoute_KeepsEachRoutesOwnSecret()
    {
        var stored = Config(
            PageWith("keep", Route("a", "http://other:1/", _secrets.Protect("other"))),
            PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("one")), Route("web", "http://nas:9091/web", _secrets.Protect("two"))));
        var incoming = Config(
            PageWith("keep", Route("a", "http://other:1/", Kept)),
            PageWith("transmission", Route("rpc2", "http://nas:9091/rpc", Kept), Route("web2", "http://nas:9091/web", Kept)));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("other", Saved(incoming, 0, 0));
        Assert.Equal("one", Saved(incoming, 1, 0));
        Assert.Equal("two", Saved(incoming, 1, 1));
    }

    [Fact]
    public void Protect_RenamedRouteNowPointingElsewhere_IsRefusedNotCarriedOver()
    {
        // A kept password is sent to the route's target, so it never follows a rename onto another host.
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx", Route("session", "http://evil.example/rpc", Kept)));

        Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, stored, _secrets));
    }

    [Fact]
    public void Protect_AmbiguousRename_IsRefusedRatherThanGuessed()
    {
        // One route deleted and its neighbour renamed: both stored routes are candidates and the counts no
        // longer line up, so picking either could hand the deleted route's password to the survivor.
        var stored = Config(PageWith("tx",
            Route("first", "http://nas:9091/a", _secrets.Protect("one")),
            Route("second", "http://nas:9091/b", _secrets.Protect("two"))));
        var incoming = Config(PageWith("tx", Route("renamed", "http://nas:9091/b", Kept)));

        Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, stored, _secrets));
    }

    [Fact]
    public void Protect_KeptSentinelWithNothingStored_IsRefusedNotBlanked()
    {
        var incoming = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", Kept)));

        var error = Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, Config(), _secrets));
        Assert.Contains("rpc", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Protect_NewPasswordOnARenamedRoute_IsJustEncrypted()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("old"))));
        var incoming = Config(PageWith("transmission", Route("session", "http://elsewhere/rpc", "new")));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("new", Saved(incoming, 0, 0));
    }

    [Fact]
    public void Protect_ARouteMatchedByNameIsNotAlsoHandedToARenamedOne()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx",
            Route("rpc", "http://nas:9091/rpc", Kept),
            Route("copy", "http://nas:9091/rpc", Kept)));

        Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, stored, _secrets));
    }

    [Fact]
    public void Check_ReportsTheRefusalWithoutChangingAnything()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx", Route("moved", "http://elsewhere/rpc", Kept)));

        var message = RouteSecrets.Check(incoming, stored);

        Assert.NotNull(message);
        Assert.Contains("moved", message, StringComparison.Ordinal);
        Assert.Equal(Kept, incoming.Pages[0].ApiRoutes[0].Password);
    }

    [Fact]
    public void Check_AcceptsAConfigurationEveryKeptPasswordPairsIn()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("transmission", Route("rpc", "http://nas:9091/rpc", Kept)));

        Assert.Null(RouteSecrets.Check(incoming, stored));
    }

    [Fact]
    public void Protect_RefusedSave_LeavesEveryIncomingPasswordUntouched()
    {
        var stored = Config(PageWith("tx", Route("rpc", "http://nas:9091/rpc", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx",
            Route("rpc", "http://nas:9091/rpc", Kept),
            Route("orphan", "http://elsewhere/x", Kept)));

        Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, stored, _secrets));
        Assert.Equal(Kept, incoming.Pages[0].ApiRoutes[0].Password);
    }

    [Fact]
    public void Protect_KeptPasswordOnARouteWhoseHostChanged_IsRefused()
    {
        // Same name, new host, password untouched: the NAS password must not be sent to the other host.
        var stored = Config(PageWith("tx", Route("x", "http://nas:5000/api", _secrets.Protect("nas-pw"))));
        var incoming = Config(PageWith("tx", Route("x", "http://other:8080/api", Kept)));

        var error = Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, stored, _secrets));
        Assert.Contains("'x'", error.Message, StringComparison.Ordinal);
        Assert.NotNull(RouteSecrets.Check(incoming, stored));
    }

    [Fact]
    public void Protect_RouteRenamedIntoADeletedRoutesName_IsRefused()
    {
        // Route a (host X) deleted and route b (host Y) renamed to a. The name now matches a's stored
        // password, but it would be sent to b's host.
        var stored = Config(PageWith("tx",
            Route("a", "http://host-x/rpc", _secrets.Protect("x-pw")),
            Route("b", "http://host-y/rpc", _secrets.Protect("y-pw"))));
        var incoming = Config(PageWith("tx", Route("a", "http://host-y/rpc", Kept)));

        Assert.Throws<ArgumentException>(() => RouteSecrets.Protect(incoming, stored, _secrets));
        Assert.NotNull(RouteSecrets.Check(incoming, stored));
    }

    [Fact]
    public void Protect_NameMatchOnTheSameHostWithANewPath_KeepsTheSecret()
    {
        var stored = Config(PageWith("tx", Route("x", "http://nas:5000/api/v1", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx", Route("x", "http://NAS:5000/api/v2", Kept)));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("pw", Saved(incoming, 0, 0));
    }

    [Fact]
    public void Protect_NewPasswordOnARouteWhoseHostChanged_IsAccepted()
    {
        var stored = Config(PageWith("tx", Route("x", "http://nas:5000/api", _secrets.Protect("nas-pw"))));
        var incoming = Config(PageWith("tx", Route("x", "http://other:8080/api", "other-pw")));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("other-pw", Saved(incoming, 0, 0));
    }

    [Fact]
    public void Protect_NameMatchesClaimTheirRouteBeforeAnyRenameIsPaired()
    {
        // The renamed route sits before the unchanged one, so pairing it first by position would take
        // rpc's password. Name matches are settled first, leaving the renamed route web's.
        var stored = Config(PageWith("tx",
            Route("rpc", "http://nas:9091/rpc", _secrets.Protect("rpc-pw")),
            Route("web", "http://nas:9091/web", _secrets.Protect("web-pw"))));
        var incoming = Config(PageWith("tx",
            Route("renamed", "http://nas:9091/web", Kept),
            Route("rpc", "http://nas:9091/rpc", Kept)));

        RouteSecrets.Protect(incoming, stored, _secrets);

        Assert.Equal("web-pw", Saved(incoming, 0, 0));
        Assert.Equal("rpc-pw", Saved(incoming, 0, 1));
    }

    [Fact]
    public void Check_NamesTheFirstRouteToFixInPageOrder()
    {
        // The first route needs a rename fallback that fails, the second a name match whose host moved.
        // The message points at the first one, whichever pass found it.
        var stored = Config(PageWith("tx", Route("second", "http://nas/a", _secrets.Protect("pw"))));
        var incoming = Config(PageWith("tx",
            Route("first", "http://elsewhere/b", Kept),
            Route("second", "http://moved/a", Kept)));

        var message = RouteSecrets.Check(incoming, stored);

        Assert.Contains("'first'", message, StringComparison.Ordinal);
    }
}
