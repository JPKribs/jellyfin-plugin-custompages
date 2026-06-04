using System;
using System.IO;
using Jellyfin.Plugin.CustomPages.Services;
using MediaBrowser.Controller;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for favicon resolution in <see cref="PageService"/>, including the path-containment guard.
/// </summary>
public class FaviconTests
{
    private static PageService ServiceWithWebPath(string webPath)
    {
        var paths = Substitute.For<IServerApplicationPaths>();
        paths.WebPath.Returns(webPath);
        return new PageService(paths);
    }

    [Fact]
    public void GetFavicon_ResolvesHashedFaviconFromIndexHtml()
    {
        var web = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(web, "index.html"), "<link rel=\"shortcut icon\" href=\"favicon.abc123.ico\">");
            File.WriteAllBytes(Path.Combine(web, "favicon.abc123.ico"), new byte[] { 1, 2, 3, 4 });

            var favicon = ServiceWithWebPath(web).GetFavicon();

            Assert.NotNull(favicon);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, favicon!.Value.Bytes);
            Assert.Equal("image/x-icon", favicon.Value.ContentType);
        }
        finally
        {
            Directory.Delete(web, true);
        }
    }

    [Fact]
    public void GetFavicon_FallsBackToGlobWhenNoIndexLink()
    {
        var web = NewTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(web, "favicon.fallback.ico"), new byte[] { 9 });

            var favicon = ServiceWithWebPath(web).GetFavicon();

            Assert.NotNull(favicon);
            Assert.Equal(new byte[] { 9 }, favicon!.Value.Bytes);
        }
        finally
        {
            Directory.Delete(web, true);
        }
    }

    [Fact]
    public void GetFavicon_RejectsHrefThatEscapesWebRoot()
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
            var favicon = ServiceWithWebPath(web).GetFavicon();

            Assert.Null(favicon);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetFavicon_ReturnsNullWhenWebPathMissing()
    {
        var favicon = ServiceWithWebPath(Path.Combine(Path.GetTempPath(), "cp-does-not-exist-" + Guid.NewGuid())).GetFavicon();
        Assert.Null(favicon);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cp-fav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
