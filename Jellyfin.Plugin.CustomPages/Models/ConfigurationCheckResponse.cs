namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// The body returned by <c>/pages/config/check</c>: whether a configuration would be accepted, and why
/// not when it would be refused.
/// </summary>
public class ConfigurationCheckResponse
{
    /// <summary>Gets or sets a value indicating whether saving the configuration would succeed.</summary>
    public bool Ok { get; set; }

    /// <summary>Gets or sets the reason the save would be refused, or <c>null</c> when it would succeed.</summary>
    public string? Message { get; set; }
}
