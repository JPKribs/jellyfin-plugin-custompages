using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.CustomPages.Api;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for <see cref="CustomPagesController"/>, which is where the visibility tiers are actually
/// enforced and where the response hardening is applied.
/// </summary>
public class CustomPagesControllerTests
{
    private static (CustomPagesController Controller, IPageService Pages) Create()
    {
        var pages = Substitute.For<IPageService>();
        var controller = new CustomPagesController(pages)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return (controller, pages);
    }

    private static CustomPage Page(PageVisibility visibility, string slug = "p")
        => new CustomPage { Slug = slug, Visibility = visibility, Enabled = true };

    private static PageAsset Asset(PageVisibility visibility, string name = "logo.png")
        => new PageAsset { Name = name, Visibility = visibility, ContentType = "image/png" };

    // MARK: Response hardening

    [Fact]
    public void Get_AnonymousPage_AppliesHardeningHeaders()
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(PageVisibility.Anonymous));
        pages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");

        controller.Get("p");
        var headers = controller.Response.Headers;

        Assert.False(string.IsNullOrEmpty(headers["Content-Security-Policy"]));
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("noindex, nofollow", headers["X-Robots-Tag"]);
    }

    [Fact]
    public void Get_AllowsEmbeddingBySameOriginOnly()
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(PageVisibility.Anonymous));
        pages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");

        controller.Get("p");

        Assert.Equal("SAMEORIGIN", controller.Response.Headers["X-Frame-Options"]);
        Assert.Contains(
            "frame-ancestors 'self'",
            controller.Response.Headers["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The anonymous path and the auth shell must carry byte-identical policies. A srcdoc frame inherits
    /// its embedder's policy, so any divergence makes the same page behave differently by tier.
    /// </summary>
    [Fact]
    public void Csp_IsIdenticalForAnonymousAndProtectedPaths()
    {
        var (anonController, anonPages) = Create();
        anonPages.Find("a").Returns(Page(PageVisibility.Anonymous, "a"));
        anonPages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");
        anonController.Get("a");

        var (shellController, shellPages) = Create();
        shellPages.Find("b").Returns(Page(PageVisibility.User, "b"));
        shellPages.GetShellHtml(Arg.Any<string>(), Arg.Any<PageVisibility>()).Returns("<html></html>");
        shellController.Get("b");

        var (userController, userPages) = Create();
        userPages.Find("c").Returns(Page(PageVisibility.User, "c"));
        userPages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");
        userController.UserContent("c");

        var anonCsp = anonController.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Equal(anonCsp, shellController.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Equal(anonCsp, userController.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public void Csp_DoesNotRestrictFormSubmission()
    {
        // form-action 'none' silently broke every form on an anonymous page while the shell allowed
        // them, so the unified policy must not reintroduce the directive on one path only.
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(PageVisibility.Anonymous));
        pages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");

        controller.Get("p");

        Assert.DoesNotContain(
            "form-action",
            controller.Response.Headers["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("object-src 'none'")]
    [InlineData("base-uri 'none'")]
    public void Csp_KeepsDirectivesThatProtectTheFramingDocument(string directive)
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(PageVisibility.Anonymous));
        pages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");

        controller.Get("p");

        Assert.Contains(
            directive,
            controller.Response.Headers["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ServesHtmlAsUtf8()
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(PageVisibility.Anonymous));
        pages.Render(Arg.Any<CustomPage>()).Returns("<html></html>");

        var result = controller.Get("p");

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
    }

    // MARK: Entry point routing

    [Fact]
    public void Get_UnknownSlug_Returns404WithHardenedNotFoundBody()
    {
        var (controller, pages) = Create();
        pages.Find("nope").Returns((CustomPage?)null);
        pages.NotFoundHtml("nope").Returns("<html>missing</html>");

        var result = controller.Get("nope");

        Assert.Equal(StatusCodes.Status404NotFound, controller.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        Assert.False(string.IsNullOrEmpty(controller.Response.Headers["Content-Security-Policy"]));
    }

    [Theory]
    [InlineData(PageVisibility.User)]
    [InlineData(PageVisibility.Admin)]
    public void Get_ProtectedPage_ServesShellRatherThanContent(PageVisibility visibility)
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(visibility));
        pages.GetShellHtml("p", visibility).Returns("<html>shell</html>");

        var result = controller.Get("p");

        Assert.Equal("<html>shell</html>", result.Content);
        pages.DidNotReceive().Render(Arg.Any<CustomPage>());
    }

    // MARK: Tier gating on the content endpoints

    [Theory]
    [InlineData(PageVisibility.Anonymous)]
    [InlineData(PageVisibility.Admin)]
    public void UserContent_RefusesPagesThatAreNotUserTier(PageVisibility visibility)
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(visibility));

        Assert.IsType<NotFoundResult>(controller.UserContent("p"));
        pages.DidNotReceive().Render(Arg.Any<CustomPage>());
    }

    [Theory]
    [InlineData(PageVisibility.Anonymous)]
    [InlineData(PageVisibility.User)]
    public void AdminContent_RefusesPagesThatAreNotAdminTier(PageVisibility visibility)
    {
        var (controller, pages) = Create();
        pages.Find("p").Returns(Page(visibility));

        Assert.IsType<NotFoundResult>(controller.AdminContent("p"));
        pages.DidNotReceive().Render(Arg.Any<CustomPage>());
    }

    [Fact]
    public void ContentEndpoints_ReturnNotFoundForUnknownSlug()
    {
        var (controller, pages) = Create();
        pages.Find(Arg.Any<string>()).Returns((CustomPage?)null);

        Assert.IsType<NotFoundResult>(controller.UserContent("nope"));
        Assert.IsType<NotFoundResult>(controller.AdminContent("nope"));
    }

    // MARK: Asset endpoint

    [Theory]
    [InlineData(PageVisibility.User)]
    [InlineData(PageVisibility.Admin)]
    public void Asset_NeverServesGatedAssetsByUrl(PageVisibility visibility)
    {
        // Gated assets are embedded into their pages instead. Returning 404 rather than 403 also keeps
        // the public endpoint from disclosing which gated names exist.
        var (controller, pages) = Create();
        pages.FindAsset("logo.png").Returns(Asset(visibility));

        Assert.IsType<NotFoundResult>(controller.Asset("logo.png"));
        pages.DidNotReceive().GetAssetBytes(Arg.Any<PageAsset>());
    }

    [Fact]
    public void Asset_ServesAnonymousAssetSandboxed()
    {
        var (controller, pages) = Create();
        var asset = Asset(PageVisibility.Anonymous);
        pages.FindAsset("logo.png").Returns(asset);
        pages.GetAssetBytes(asset).Returns(new byte[] { 1, 2, 3 });

        var result = Assert.IsType<FileContentResult>(controller.Asset("logo.png"));

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("default-src 'none'; sandbox", controller.Response.Headers["Content-Security-Policy"]);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public void Asset_NonImageIsServedAsOpaqueDownload()
    {
        var (controller, pages) = Create();
        var asset = new PageAsset
        {
            Name = "notes.txt",
            Visibility = PageVisibility.Anonymous,
            ContentType = "text/html"
        };
        pages.FindAsset("notes.txt").Returns(asset);
        pages.GetAssetBytes(asset).Returns(new byte[] { 1 });

        var result = Assert.IsType<FileContentResult>(controller.Asset("notes.txt"));

        Assert.Equal("application/octet-stream", result.ContentType);
    }

    [Fact]
    public void Asset_UndecodableAssetReturnsNotFound()
    {
        var (controller, pages) = Create();
        var asset = Asset(PageVisibility.Anonymous);
        pages.FindAsset("logo.png").Returns(asset);
        pages.GetAssetBytes(asset).Returns((byte[]?)null);

        Assert.IsType<NotFoundResult>(controller.Asset("logo.png"));
    }

    [Fact]
    public void Favicon_ReturnsNotFoundWhenUnavailable()
    {
        var (controller, pages) = Create();
        pages.GetFavicon().Returns(((byte[], string)?)null);

        Assert.IsType<NotFoundResult>(controller.Favicon());
    }

    // MARK: Authorization attributes
    //
    // The tier checks above only matter if the framework is still gating these actions, so assert the
    // attributes themselves rather than trusting that nobody deletes one during a refactor.

    [Theory]
    [InlineData(nameof(CustomPagesController.Get))]
    [InlineData(nameof(CustomPagesController.Asset))]
    [InlineData(nameof(CustomPagesController.Favicon))]
    public void PublicActions_AreExplicitlyAnonymous(string action)
        => Assert.NotNull(Method(action).GetCustomAttribute<AllowAnonymousAttribute>());

    [Fact]
    public void UserContent_RequiresAnAuthenticatedUser()
    {
        var authorize = Method(nameof(CustomPagesController.UserContent)).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Null(Method(nameof(CustomPagesController.UserContent)).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void AdminContent_RequiresElevation()
    {
        var authorize = Method(nameof(CustomPagesController.AdminContent)).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("RequiresElevation", authorize!.Policy);
    }

    private static MethodInfo Method(string name)
        => typeof(CustomPagesController).GetMethods().First(m => m.Name == name);
}
