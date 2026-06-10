using System;
using Jellyfin.Plugin.CustomPages.Configuration;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for the server side configuration validation applied before a save is persisted.
/// </summary>
public class ConfigurationValidatorTests
{
    private static PluginConfiguration Config(params object[] items)
    {
        var config = new PluginConfiguration();
        foreach (var item in items)
        {
            if (item is CustomPage page)
            {
                config.Pages.Add(page);
            }
            else if (item is PageAsset asset)
            {
                config.Assets.Add(asset);
            }
        }

        return config;
    }

    private static PageAsset Asset(string name, string? dataBase64 = null)
        => new PageAsset
        {
            Name = name,
            ContentType = "image/png",
            DataBase64 = dataBase64 ?? Convert.ToBase64String(new byte[] { 1, 2, 3 })
        };

    // MARK: Pages

    [Fact]
    public void Validate_AcceptsDistinctSlugs()
        => ConfigurationValidator.Validate(Config(
            new CustomPage { Slug = "alpha" },
            new CustomPage { Slug = "beta" }));

    [Fact]
    public void Validate_RejectsDuplicateSlugsCaseInsensitively()
        => Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(Config(
            new CustomPage { Slug = "alpha" },
            new CustomPage { Slug = "ALPHA" })));

    [Fact]
    public void Validate_AllowsUnreachableDraftPages()
        => ConfigurationValidator.Validate(Config(
            new CustomPage { Slug = string.Empty },
            new CustomPage { Slug = string.Empty },
            new CustomPage { Slug = "alpha" }));

    // MARK: Assets

    [Fact]
    public void Validate_AcceptsValidAssets()
        => ConfigurationValidator.Validate(Config(Asset("logo.png"), Asset("banner.jpg")));

    [Fact]
    public void Validate_RejectsInvalidAssetName()
        => Assert.Throws<ArgumentException>(() =>
            ConfigurationValidator.Validate(Config(Asset("../escape.png"))));

    [Fact]
    public void Validate_RejectsDuplicateAssetNamesCaseInsensitively()
        => Assert.Throws<ArgumentException>(() =>
            ConfigurationValidator.Validate(Config(Asset("logo.png"), Asset("LOGO.PNG"))));

    [Fact]
    public void Validate_RejectsInvalidBase64()
        => Assert.Throws<ArgumentException>(() =>
            ConfigurationValidator.Validate(Config(Asset("logo.png", dataBase64: "not base64!"))));

    [Fact]
    public void Validate_RejectsAssetOverSizeCap()
    {
        var oversized = Convert.ToBase64String(new byte[ConfigurationValidator.MaxAssetBytes + 1]);
        Assert.Throws<ArgumentException>(() =>
            ConfigurationValidator.Validate(Config(Asset("big.png", dataBase64: oversized))));
    }

    [Fact]
    public void Validate_AcceptsAssetAtSizeCap()
    {
        var atCap = Convert.ToBase64String(new byte[ConfigurationValidator.MaxAssetBytes]);
        ConfigurationValidator.Validate(Config(Asset("big.png", dataBase64: atCap)));
    }
}
