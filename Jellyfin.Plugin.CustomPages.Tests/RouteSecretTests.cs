using System;
using System.IO;
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

    // Mirrors the resolution Plugin.UpdateConfiguration applies to every route password.
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
}
