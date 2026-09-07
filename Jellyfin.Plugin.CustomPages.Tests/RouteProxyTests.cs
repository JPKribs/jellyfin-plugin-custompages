using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.CustomPages.Models;
using Jellyfin.Plugin.CustomPages.Services;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for <see cref="RouteProxy"/>, the server side forwarder behind a page's named routes.
/// </summary>
public class RouteProxyTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public CapturingHandler(HttpResponseMessage response) => _response = response;

        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(_response);
        }
    }

    private static IHttpClientFactory FactoryFor(CapturingHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    [Fact]
    public async Task ForwardAsync_RelaysStatusBodyAndResponseHeadersAndAddsBasicAuth()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Headers.TryAddWithoutValidation("X-Session-Token", "SID-9");
        response.Content = new StringContent("payload");
        var handler = new CapturingHandler(response);

        var route = new PageApiRoute
        {
            Name = "backend",
            Url = "http://backend.test/rpc",
            Username = "user"
        };

        var result = await RouteProxy.ForwardAsync(
            FactoryFor(handler), route, "POST",
            System.Text.Encoding.UTF8.GetBytes("{\"method\":\"session-get\"}"),
            "application/json", null, "pw", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(409, result!.StatusCode);
        Assert.Equal("payload", System.Text.Encoding.UTF8.GetString(result.Body));
        Assert.True(result.Headers.ContainsKey("X-Session-Token"));
        Assert.Equal("SID-9", result.Headers["X-Session-Token"]);

        var auth = handler.Seen!.Headers.Authorization;
        Assert.Equal("Basic", auth!.Scheme);
        Assert.Equal("user:pw", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    [Fact]
    public async Task ForwardAsync_DropsHopByHopHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
        response.Content = new StringContent("ok");
        var handler = new CapturingHandler(response);

        var route = new PageApiRoute { Name = "r", Url = "http://x.test/y" };
        var result = await RouteProxy.ForwardAsync(FactoryFor(handler), route, "GET", null, null, null, string.Empty, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Headers.ContainsKey("Transfer-Encoding"));
    }

    [Fact]
    public async Task ForwardAsync_ForwardsTargetHeadersButNotTheJellyfinToken()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var route = new PageApiRoute { Name = "backend", Url = "http://x.test/rpc" };

        var headers = new[]
        {
            new KeyValuePair<string, string>("X-Session-Token", "SID-7"),
            new KeyValuePair<string, string>("Authorization", "MediaBrowser Token=\"viewer\""),
            new KeyValuePair<string, string>("X-Emby-Token", "viewer")
        };

        await RouteProxy.ForwardAsync(FactoryFor(handler), route, "POST",
            System.Text.Encoding.UTF8.GetBytes("{}"), "application/json", headers, string.Empty, CancellationToken.None);

        Assert.True(handler.Seen!.Headers.TryGetValues("X-Session-Token", out var sid));
        Assert.Contains("SID-7", sid!);

        // The viewer's Jellyfin credentials must never be handed to a route target.
        Assert.False(handler.Seen.Headers.Contains("X-Emby-Token"));
        Assert.Null(handler.Seen.Headers.Authorization);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("not-a-url")]
    [InlineData("ftp://host/x")]
    public async Task ForwardAsync_RefusesNonHttpTargets(string url)
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var route = new PageApiRoute { Name = "r", Url = url };

        var result = await RouteProxy.ForwardAsync(FactoryFor(handler), route, "GET", null, null, null, string.Empty, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(handler.Seen);
    }
}
