using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.CustomPages.Api;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for the server side route endpoint on <see cref="CustomPagesController"/>: who may call a
/// route, what reaches the target, and what of the target's answer reaches the caller.
/// </summary>
public class RouteEndpointTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public enum Caller
    {
        None,
        User,
        Admin,
        ApiKey
    }

    // Records every request the target saw and answers with whatever the test configured.
    private sealed class Target : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; }
            = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("from target") });

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Answer(request, cancellationToken);
        }
    }

    private static (CustomPagesController Controller, Target Target) Create(
        CustomPage page, Caller caller, Guid? userId = null, TimeSpan? timeout = null)
    {
        var pages = Substitute.For<IPageService>();
        pages.Find(page.Slug).Returns(page);

        User? user = null;
        if (caller is Caller.User or Caller.Admin)
        {
            user = new User("caller", "Default", "Default") { Id = userId ?? Alice };
            user.SetPermission(PermissionKind.IsAdministrator, caller == Caller.Admin);
        }

        var authorization = Substitute.For<IAuthorizationContext>();
        authorization.GetAuthorizationInfo(Arg.Any<HttpRequest>())
            .Returns(Task.FromResult(new AuthorizationInfo { User = user, IsApiKey = caller == Caller.ApiKey }));

        var target = new Target();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ =>
        {
            var client = new HttpClient(target, disposeHandler: false);
            if (timeout is not null)
            {
                client.Timeout = timeout.Value;
            }

            return client;
        });

        var controller = new CustomPagesController(
            pages, Substitute.For<IStoreService>(), authorization, factory,
            Substitute.For<MediaBrowser.Controller.IServerApplicationPaths>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Method = "GET";

        return (controller, target);
    }

    private static CustomPage Page(PageVisibility visibility, params Guid[] allowed)
    {
        var page = new CustomPage
        {
            Slug = "p",
            Visibility = visibility,
            Enabled = true,
            ApiRoutes = new List<PageApiRoute> { new PageApiRoute { Name = "backend", Url = "http://backend.test/rpc" } }
        };

        foreach (var id in allowed)
        {
            page.AllowedUserIds.Add(id.ToString());
        }

        return page;
    }

    private static int StatusOf(CustomPagesController controller, IActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        ObjectResult obj => obj.StatusCode ?? 200,
        FileResult => controller.Response.StatusCode,
        _ => -1
    };

    private static Task<IActionResult> Call(CustomPagesController controller)
        => controller.ApiRoute("p", "backend", CancellationToken.None);

    // MARK: Who may call a route

    [Theory]
    [InlineData(PageVisibility.Anonymous, Caller.None, 200)]
    [InlineData(PageVisibility.Anonymous, Caller.User, 200)]
    [InlineData(PageVisibility.Anonymous, Caller.Admin, 200)]
    [InlineData(PageVisibility.User, Caller.None, 401)]
    [InlineData(PageVisibility.User, Caller.ApiKey, 401)]
    [InlineData(PageVisibility.User, Caller.User, 200)]
    [InlineData(PageVisibility.User, Caller.Admin, 200)]
    [InlineData(PageVisibility.Admin, Caller.None, 401)]
    [InlineData(PageVisibility.Admin, Caller.ApiKey, 401)]
    [InlineData(PageVisibility.Admin, Caller.User, 403)]
    [InlineData(PageVisibility.Admin, Caller.Admin, 200)]
    public async Task ApiRoute_ReachesExactlyThePageAudience(PageVisibility tier, Caller caller, int expected)
    {
        var (controller, target) = Create(Page(tier), caller);

        var status = StatusOf(controller, await Call(controller));

        Assert.Equal(expected, status);
        Assert.Equal(expected == 200 ? 1 : 0, target.Calls);
    }

    [Theory]
    [InlineData(Caller.User, "11111111-1111-1111-1111-111111111111", 200)]
    [InlineData(Caller.User, "22222222-2222-2222-2222-222222222222", 403)]
    [InlineData(Caller.Admin, "22222222-2222-2222-2222-222222222222", 200)]
    public async Task ApiRoute_HonoursTheAllowList(Caller caller, string callerId, int expected)
    {
        var (controller, target) = Create(Page(PageVisibility.User, Alice), caller, Guid.Parse(callerId));

        Assert.Equal(expected, StatusOf(controller, await Call(controller)));
        Assert.Equal(expected == 200 ? 1 : 0, target.Calls);
    }

    [Fact]
    public async Task ApiRoute_UnknownRoute_IsNotFound()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);

        var result = await controller.ApiRoute("p", "nope", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, target.Calls);
    }

    // MARK: What of the target's answer reaches the caller

    [Fact]
    public async Task ApiRoute_DropsTargetHeadersThatWouldActOnTheJellyfinOrigin()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        target.Answer = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "jellyfin=stolen; Path=/");
            response.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=31536000");
            response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
            response.Headers.TryAddWithoutValidation("Content-Security-Policy", "default-src *");
            response.Headers.TryAddWithoutValidation("X-Frame-Options", "ALLOWALL");
            response.Headers.TryAddWithoutValidation("X-Content-Type-Options", "none");
            response.Headers.TryAddWithoutValidation("X-Session-Token", "SID-3");
            response.Headers.TryAddWithoutValidation("Location", "/rpc/5");
            return Task.FromResult(response);
        };

        var result = await Call(controller);
        var headers = controller.Response.Headers;

        Assert.Equal(201, StatusOf(controller, result));
        Assert.False(headers.ContainsKey("Set-Cookie"));
        Assert.False(headers.ContainsKey("Access-Control-Allow-Origin"));
        Assert.False(headers.ContainsKey("X-Frame-Options"));
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("SID-3", headers["X-Session-Token"]);
        Assert.Equal("/rpc/5", headers["Location"]);

        // The target's own policy never makes it through. JSON is not rendered as a document, so no
        // sandbox is added in its place either.
        Assert.False(headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public async Task ApiRoute_SandboxesTheResponseEvenWithoutATargetPolicy()
    {
        // A target answering HTML must not run script on the Jellyfin origin if the route is opened
        // directly, so the sandbox applies to every relayed response.
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        target.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<script>alert(1)</script>", Encoding.UTF8, "text/html")
        });

        await Call(controller);

        Assert.Equal("sandbox", controller.Response.Headers["Content-Security-Policy"]);
    }

    [Theory]
    [InlineData("text/html; charset=utf-8", true)]
    [InlineData("image/svg+xml", true)]
    [InlineData("application/xml", true)]
    [InlineData(null, true)]
    [InlineData("application/pdf", false)]
    [InlineData("image/png", false)]
    public async Task ApiRoute_SandboxesOnlyDocumentTypes(string? contentType, bool sandboxed)
    {
        // A sandboxed PDF cannot open in the browser's own viewer, so only types a browser renders as a
        // scriptable document, or that it might sniff as one, are sandboxed.
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        target.Answer = (_, _) =>
        {
            var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
            if (contentType is not null)
            {
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };

        await Call(controller);

        Assert.Equal(sandboxed, controller.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"]);
    }

    [Fact]
    public async Task ApiRoute_UnreachableTarget_IsBadGateway()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        target.Answer = (_, _) => throw new HttpRequestException("connection refused");

        Assert.Equal(502, StatusOf(controller, await Call(controller)));
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"]);
    }

    [Fact]
    public async Task ApiRoute_SlowTarget_IsGatewayTimeout()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None, timeout: TimeSpan.FromMilliseconds(200));
        target.Answer = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        Assert.Equal(504, StatusOf(controller, await Call(controller)));
    }

    [Fact]
    public async Task ApiRoute_OversizedResponse_IsBadGateway()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        target.Answer = (_, _) =>
        {
            var content = new ByteArrayContent(Array.Empty<byte>());
            content.Headers.ContentLength = RouteProxy.MaxResponseBytes + 1L;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };

        Assert.Equal(502, StatusOf(controller, await Call(controller)));
    }

    // MARK: Request body

    [Fact]
    public async Task ApiRoute_OversizedRequestBody_IsRefusedBeforeForwarding()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        controller.Request.Method = "POST";
        controller.Request.Body = new MemoryStream(new byte[RouteProxy.MaxRequestBytes + 1]);

        Assert.Equal(413, StatusOf(controller, await Call(controller)));
        Assert.Equal(0, target.Calls);
    }

    [Fact]
    public async Task ApiRoute_DeclaredOversizedRequestBody_IsRefusedUnread()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        controller.Request.Method = "POST";
        controller.Request.ContentLength = RouteProxy.MaxRequestBytes + 1L;
        controller.Request.Body = new MemoryStream(Array.Empty<byte>());

        Assert.Equal(413, StatusOf(controller, await Call(controller)));
        Assert.Equal(0, target.Calls);
    }

    [Fact]
    public async Task ApiRoute_BodyWithinTheCap_IsForwarded()
    {
        var (controller, target) = Create(Page(PageVisibility.Anonymous), Caller.None);
        string? seen = null;
        target.Answer = async (request, token) =>
        {
            seen = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        };
        controller.Request.Method = "POST";
        controller.Request.ContentType = "application/json";
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"method\":\"session-get\"}"));

        Assert.Equal(200, StatusOf(controller, await Call(controller)));
        Assert.Equal("{\"method\":\"session-get\"}", seen);
    }
}
