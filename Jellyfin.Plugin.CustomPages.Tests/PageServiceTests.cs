using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using MediaBrowser.Controller;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for the pure rendering and validation logic in <see cref="PageService"/>.
/// </summary>
public class PageServiceTests
{
    private static PageService CreateService()
        => new PageService(Substitute.For<IServerApplicationPaths>());

    // MARK: Slug validation

    [Theory]
    [InlineData("abc")]
    [InlineData("a-b_c")]
    [InlineData("page1")]
    [InlineData("ABC")]
    public void IsValidSlug_AllowsSafeNames(string slug)
        => Assert.True(PageService.IsValidSlug(slug));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("../etc")]
    [InlineData("a!b")]
    public void IsValidSlug_RejectsUnsafeNames(string? slug)
        => Assert.False(PageService.IsValidSlug(slug));

    // MARK: Asset name validation

    [Theory]
    [InlineData("logo.png")]
    [InlineData("my-image_2.jpg")]
    [InlineData("a.b.c.svg")]
    public void IsValidAssetName_AllowsSafeNames(string name)
        => Assert.True(PageService.IsValidAssetName(name));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b.png")]
    [InlineData("a\\b.png")]
    [InlineData("a b.png")]
    public void IsValidAssetName_RejectsUnsafeNames(string? name)
        => Assert.False(PageService.IsValidAssetName(name));

    // MARK: Sandbox isolation (security invariant)

    [Fact]
    public void Render_WrapsContentInSandboxedIframeWithoutSameOrigin()
    {
        var html = CreateService().Render(new CustomPage { Title = "t", Html = "<p>hi</p>" });

        Assert.Contains("<iframe", html, System.StringComparison.Ordinal);
        Assert.Contains("sandbox=", html, System.StringComparison.Ordinal);
        Assert.Contains("srcdoc=", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EncodesAuthorContentIntoSrcdoc_NoTopLevelInjection()
    {
        var page = new CustomPage
        {
            Title = "<script>alert(1)</script>",
            Html = "<b>body</b>"
        };

        var html = CreateService().Render(page);

        // Neither the malicious title nor the author body appear as live top-level markup.
        Assert.DoesNotContain("<script>alert(1)</script>", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("<b>body</b>", html, System.StringComparison.Ordinal);
        // They are present only in HTML-encoded form (inside the srcdoc / title).
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, System.StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;body&lt;/b&gt;", html, System.StringComparison.Ordinal);
    }

    // MARK: Composed vs single-file rendering

    [Fact]
    public void Render_ComposedMode_IncludesHtmlCssAndJs()
    {
        var page = new CustomPage
        {
            SingleFile = false,
            Html = "BODYMARK",
            Css = "CSSMARK",
            Js = "JSMARK"
        };

        var html = CreateService().Render(page);

        Assert.Contains("BODYMARK", html, System.StringComparison.Ordinal);
        Assert.Contains("CSSMARK", html, System.StringComparison.Ordinal);
        Assert.Contains("JSMARK", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SingleFileMode_UsesDocumentAndIgnoresComponents()
    {
        var page = new CustomPage
        {
            SingleFile = true,
            Document = "DOCMARK",
            Html = "BODYMARK",
            Css = "CSSMARK",
            Js = "JSMARK"
        };

        var html = CreateService().Render(page);

        Assert.Contains("DOCMARK", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("BODYMARK", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("CSSMARK", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("JSMARK", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_TemplateFillIsSinglePass_AuthorPlaceholdersNotReSubstituted()
    {
        // The body literally contains the {{TITLE}} token; single-pass fill must not expand it.
        var page = new CustomPage { Title = "RealTitle", Html = "{{TITLE}}" };

        var html = CreateService().Render(page);

        Assert.Contains("RealTitle", html, System.StringComparison.Ordinal);
        Assert.Contains("{{TITLE}}", html, System.StringComparison.Ordinal);
    }

    // MARK: Auth shell

    [Theory]
    [InlineData(PageVisibility.User, "'user'")]
    [InlineData(PageVisibility.Admin, "'admin'")]
    public void GetShellHtml_BakesInTier(PageVisibility visibility, string expected)
    {
        var html = CreateService().GetShellHtml("mypage", visibility);

        Assert.Contains(expected, html, System.StringComparison.Ordinal);
        Assert.Contains("mypage", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GetShellHtml_JsEscapesSlug()
    {
        var html = CreateService().GetShellHtml("a'b</script>", PageVisibility.User);

        // The single quote and angle brackets are escaped so they cannot break out of the JS string.
        Assert.Contains("a\\'b\\x3C/script\\x3E", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("'a'b", html, System.StringComparison.Ordinal);
    }

    // MARK: Gated asset inlining

    private static PageAsset MakeAsset(
        string name,
        PageVisibility visibility,
        string contentType = "image/png",
        string? dataBase64 = null)
        => new PageAsset
        {
            Name = name,
            Visibility = visibility,
            ContentType = contentType,
            DataBase64 = dataBase64 ?? System.Convert.ToBase64String(new byte[] { 1, 2, 3 })
        };

    [Theory]
    [InlineData(PageVisibility.User, PageVisibility.User)]
    [InlineData(PageVisibility.Admin, PageVisibility.User)]
    [InlineData(PageVisibility.Admin, PageVisibility.Admin)]
    public void InlineGatedAssets_EmbedsAssetCoveredByPageTier(PageVisibility pageTier, PageVisibility assetTier)
    {
        var asset = MakeAsset("secret.png", assetTier);
        var html = PageService.InlineGatedAssets("<img src=\"asset/secret.png\">", pageTier, new[] { asset });

        Assert.DoesNotContain("asset/secret.png", html, System.StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64," + asset.DataBase64, html, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PageVisibility.Anonymous, PageVisibility.User)]
    [InlineData(PageVisibility.Anonymous, PageVisibility.Admin)]
    [InlineData(PageVisibility.User, PageVisibility.Admin)]
    public void InlineGatedAssets_NeverEmbedsAssetAbovePageTier(PageVisibility pageTier, PageVisibility assetTier)
    {
        var asset = MakeAsset("secret.png", assetTier);
        var html = PageService.InlineGatedAssets("<img src=\"asset/secret.png\">", pageTier, new[] { asset });

        Assert.Contains("asset/secret.png", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_IgnoresAnonymousAssets()
    {
        // Anonymous assets stay URL served so the browser can cache them.
        var asset = MakeAsset("logo.png", PageVisibility.Anonymous);
        var html = PageService.InlineGatedAssets("<img src=\"asset/logo.png\">", PageVisibility.Admin, new[] { asset });

        Assert.Contains("asset/logo.png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_SkipsNonImageContentTypes()
    {
        var asset = MakeAsset("page.html", PageVisibility.User, contentType: "text/html");
        var html = PageService.InlineGatedAssets("<a href=\"asset/page.html\">x</a>", PageVisibility.User, new[] { asset });

        Assert.Contains("asset/page.html", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("data:text/html", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_SkipsInvalidBase64()
    {
        var asset = MakeAsset("bad.png", PageVisibility.User, dataBase64: "not base64!");
        var html = PageService.InlineGatedAssets("<img src=\"asset/bad.png\">", PageVisibility.User, new[] { asset });

        Assert.Contains("asset/bad.png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_MatchesReferencesCaseInsensitively()
    {
        var asset = MakeAsset("secret.png", PageVisibility.User);
        var html = PageService.InlineGatedAssets("<img src=\"Asset/Secret.PNG\">", PageVisibility.User, new[] { asset });

        Assert.DoesNotContain("Asset/Secret.PNG", html, System.StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", html, System.StringComparison.Ordinal);
    }

    // MARK: Asset byte decoding

    [Fact]
    public void GetAssetBytes_DecodesValidBase64()
    {
        var asset = MakeAsset("logo.png", PageVisibility.Anonymous);

        var bytes = CreateService().GetAssetBytes(asset);

        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public void GetAssetBytes_ReturnsNullForInvalidBase64()
    {
        var asset = MakeAsset("bad.png", PageVisibility.Anonymous, dataBase64: "not base64!");

        Assert.Null(CreateService().GetAssetBytes(asset));
    }

    [Fact]
    public void GetAssetBytes_ReturnsCachedBytesForRepeatedRequests()
    {
        var service = CreateService();
        var asset = MakeAsset("logo.png", PageVisibility.Anonymous);

        var first = service.GetAssetBytes(asset);
        var second = service.GetAssetBytes(asset);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // MARK: Visibility tier ordering

    [Theory]
    [InlineData(PageVisibility.Anonymous, PageVisibility.Anonymous, true)]
    [InlineData(PageVisibility.User, PageVisibility.Anonymous, true)]
    [InlineData(PageVisibility.Admin, PageVisibility.User, true)]
    [InlineData(PageVisibility.Anonymous, PageVisibility.User, false)]
    [InlineData(PageVisibility.User, PageVisibility.Admin, false)]
    public void Covers_FollowsTierOrdering(PageVisibility tier, PageVisibility required, bool expected)
        => Assert.Equal(expected, tier.Covers(required));

    // MARK: Not-found fallback

    [Fact]
    public void NotFoundHtml_ValidSlug_IsReflectedEncoded()
    {
        var html = CreateService().NotFoundHtml("missing-page");
        Assert.Contains("/pages/missing-page", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NotFoundHtml_InvalidSlug_IsNotReflected()
    {
        var html = CreateService().NotFoundHtml("<script>bad</script>");

        Assert.DoesNotContain("<script>bad</script>", html, System.StringComparison.Ordinal);
        Assert.Contains("this address", html, System.StringComparison.Ordinal);
    }
}
