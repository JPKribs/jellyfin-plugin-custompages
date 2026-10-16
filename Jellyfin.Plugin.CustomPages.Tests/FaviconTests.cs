using System;
using System.IO;
using Jellyfin.Plugin.CustomPages.Api;
using Jellyfin.Plugin.CustomPages.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for the <c>/pages/favicon.ico</c> endpoint, including the path-containment guard.
/// </summary>
public class FaviconTests
{
    private static CustomPagesController ControllerWithWebPath(string webPath)
    {
        var paths = Substitute.For<IServerApplicationPaths>();
        paths.WebPath.Returns(webPath);
        return new CustomPagesController(
            Substitute.For<IPageService>(),
            Substitute.For<IStoreService>(),
            Substitute.For<IAuthorizationContext>(),
            Substitute.For<System.Net.Http.IHttpClientFactory>(),
            paths)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public void Favicon_ResolvesHashedFaviconFromIndexHtml()
    {
        var web = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(web, "index.html"), "<link rel=\"shortcut icon\" href=\"favicon.abc123.ico\">");
            File.WriteAllBytes(Path.Combine(web, "favicon.abc123.ico"), new byte[] { 1, 2, 3, 4 });

            var controller = ControllerWithWebPath(web);
            var favicon = Assert.IsType<FileContentResult>(controller.Favicon());

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, favicon.FileContents);
            Assert.Equal("image/x-icon", favicon.ContentType);
            Assert.Equal("public, max-age=86400", controller.Response.Headers["Cache-Control"].ToString());
        }
        finally
        {
            Directory.Delete(web, true);
        }
    }

    [Fact]
    public void Favicon_FallsBackToGlobWhenNoIndexLink()
    {
        var web = NewTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(web, "favicon.fallback.ico"), new byte[] { 9 });

            var favicon = Assert.IsType<FileContentResult>(ControllerWithWebPath(web).Favicon());

            Assert.Equal(new byte[] { 9 }, favicon.FileContents);
        }
        finally
        {
            Directory.Delete(web, true);
        }
    }

    [Fact]
    public void Favicon_RejectsHrefThatEscapesWebRoot()
    {
        var root = NewTempDir();
        try
        {
            var web = Path.Combine(root, "web");
            Directory.CreateDirectory(web);

            // A traversal href plus a real file just outside the web root.
            File.WriteAllText(Path.Combine(web, "index.html"), "<link rel=\"icon\" href=\"../evil.ico\">");
            File.WriteAllBytes(Path.Combine(root, "evil.ico"), new byte[] { 6, 6, 6 });

            // No favicon*.ico inside web, so a rejected href must yield no favicon at all.
            Assert.IsType<NotFoundResult>(ControllerWithWebPath(web).Favicon());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Favicon_ReturnsNullWhenWebPathMissing()
    {
        var controller = ControllerWithWebPath(Path.Combine(Path.GetTempPath(), "cp-does-not-exist-" + Guid.NewGuid()));
        Assert.IsType<NotFoundResult>(controller.Favicon());
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cp-fav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
