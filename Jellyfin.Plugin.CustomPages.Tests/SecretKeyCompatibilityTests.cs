using System;
using System.IO;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Pins the key folder, Data Protection application name, and purpose the route credentials are
/// protected under. The plugin used to build these by hand; if the shared helper ever derived anything
/// different, every credential already saved would stop decrypting.
/// </summary>
public class SecretKeyCompatibilityTests : IDisposable
{
    private readonly string _root;
    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretKeyCompatibilityTests"/> class with a fresh
    /// plugin configurations folder, so the shared helper's per folder provider cache starts empty.
    /// </summary>
    public SecretKeyCompatibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "custompages-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = Substitute.For<IApplicationPaths>();
        _paths.PluginConfigurationsPath.Returns(_root);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void KeyFolder_IsTheOneThePluginAlwaysUsed()
        => Assert.Equal(
            Path.Combine(_root, "Jellyfin.Plugin.CustomPages.Keys"),
            StableSecretProtection.KeyDirectory(_paths, Plugin.SecretsName));

    [Fact]
    public void SecretSavedByThePreviousBuild_StillDecrypts()
    {
        // The construction the plugin's own copy used before the shared helper existed.
        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_root, "Jellyfin.Plugin.CustomPages.Keys")))
            .SetApplicationName("Jellyfin.Plugin.CustomPages");
        using var container = services.BuildServiceProvider();
        var legacy = new SecretProtector(
            "Jellyfin.Plugin.CustomPages.Secrets.v1",
            NullLogger.Instance,
            container.GetRequiredService<IDataProtectionProvider>());
        var stored = legacy.Protect("route-password");

        var current = StableSecretProtection.CreateProtector(_paths, Plugin.SecretsName, NullLogger.Instance);

        Assert.True(current.IsAvailable);
        Assert.Equal("route-password", current.Unprotect(stored));
    }
}
