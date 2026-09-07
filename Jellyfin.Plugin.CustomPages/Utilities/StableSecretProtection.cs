using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomPages.Utilities;

/// <summary>
/// Builds a Data Protection provider whose encrypted secrets survive changes in how the Jellyfin host
/// is launched. Keys live in a fixed directory under the Jellyfin data folder and the application name
/// is constant, so the discriminator does not move when the install updates or switches between the
/// desktop app, a service, and Docker.
/// </summary>
public static class StableSecretProtection
{
    // A constant application name keeps the Data Protection discriminator stable regardless of the
    // host's content root.
    private const string ApplicationName = "Jellyfin.Plugin.CustomPages";

    // The key-ring container manages the on-disk keys for the process lifetime. Hold a reference so it
    // is never collected out from under the protector.
    private static IServiceProvider? _keyRingContainer;

    /// <summary>
    /// Returns a provider that encrypts with a plugin-managed, launch-independent key stored under the
    /// Jellyfin data folder, or <c>null</c> when that store cannot be created.
    /// </summary>
    /// <param name="keyDirectory">Fixed key-storage directory under the data folder.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>A provider to hand to <c>SecretProtector</c>, or <c>null</c> when unavailable.</returns>
    public static IDataProtectionProvider? Build(string keyDirectory, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            Directory.CreateDirectory(keyDirectory);
            var services = new ServiceCollection();
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
                .SetApplicationName(ApplicationName);

#pragma warning disable CA2000 // The container owns the key ring for the process lifetime; kept alive via _keyRingContainer.
            var container = services.BuildServiceProvider();
#pragma warning restore CA2000
            _keyRingContainer = container;
            return container.GetRequiredService<IDataProtectionProvider>();
        }
        catch (IOException ex)
        {
            return Degrade(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Degrade(ex);
        }
        catch (ArgumentException ex)
        {
            return Degrade(ex);
        }
        catch (NotSupportedException ex)
        {
            return Degrade(ex);
        }
        catch (InvalidOperationException ex)
        {
            return Degrade(ex);
        }

        // A key store the host will not give us is not fatal: the caller falls back to plaintext rather
        // than reaching outside the data folder for keys.
        IDataProtectionProvider? Degrade(Exception ex)
        {
            logger.LogWarning(ex, "Could not initialize plugin-managed Data Protection keys at {Path}; secrets will be stored in plaintext.", keyDirectory);
            return null;
        }
    }
}
