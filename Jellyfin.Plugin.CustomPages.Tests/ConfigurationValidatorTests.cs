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

    [Fact]
    public void Validate_RejectsMissingAssetData()
    {
        var asset = Asset("logo.png");
        asset.DataBase64 = null!;

        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(Config(asset)));
    }

    // MARK: Visibility tiers
    //
    // An out of range tier is not equal to User or Admin, so the gated endpoints refuse it, yet it
    // compares as outranking every tier when deciding which gated assets a page may embed.

    [Fact]
    public void Validate_RejectsUnknownPageVisibility()
        => Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(Config(
            new CustomPage { Slug = "alpha", Visibility = (PageVisibility)7 })));

    [Fact]
    public void Validate_RejectsUnknownAssetVisibility()
    {
        var asset = Asset("logo.png");
        asset.Visibility = (PageVisibility)7;

        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(Config(asset)));
    }

    // MARK: Content types
    //
    // The content type reaches a response header and a data: URI, so it must not be able to carry
    // header control characters or break out of an attribute in the dashboard's preview markup.

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    [InlineData("font/woff2")]
    public void Validate_AcceptsOrdinaryContentTypes(string contentType)
    {
        var asset = Asset("logo.png");
        asset.ContentType = contentType;

        ConfigurationValidator.Validate(Config(asset));
    }

    [Theory]
    [InlineData("")]
    [InlineData("image")]
    [InlineData("image/png\r\nX-Injected: 1")]
    [InlineData("image/png; charset=\"x\"")]
    [InlineData("x\" onerror=\"alert(1)")]
    [InlineData("image/png, text/html")]
    public void Validate_RejectsMalformedContentTypes(string contentType)
    {
        var asset = Asset("logo.png");
        asset.ContentType = contentType;

        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(Config(asset)));
    }

    [Fact]
    public void Validate_RejectsNullContentType()
    {
        var asset = Asset("logo.png");
        asset.ContentType = null!;

        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(Config(asset)));
    }
}
