using System;
using System.Collections.Generic;
using System.IO;
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

    // MARK: Response header relay

    // A handler that answers by calling a delegate, for the failure and slow target cases.
    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _send(request, cancellationToken);
    }

    // A body that never produces a byte until it is cancelled, standing in for a target that sends its
    // headers promptly and then trickles or stalls the body.
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A non seekable body of a given size with no declared length, so the cap has to be enforced while
    // reading rather than from Content-Length.
    private sealed class UnsizedStream : Stream
    {
        private long _remaining;

        public UnsizedStream(long size) => _remaining = size;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _remaining);
            Array.Fill(buffer, (byte)'x', offset, n);
            _remaining -= n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static IHttpClientFactory FactoryFor(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ =>
        {
            var client = new HttpClient(handler, disposeHandler: false);
            if (timeout is not null)
            {
                client.Timeout = timeout.Value;
            }

            return client;
        });
        return factory;
    }

    private static Task<ForwardResult?> Get(IHttpClientFactory factory, CancellationToken cancellationToken = default)
        => RouteProxy.ForwardAsync(
            factory, new PageApiRoute { Name = "r", Url = "http://x.test/y" }, "GET", null, null, null, string.Empty, cancellationToken);

    [Fact]
    public async Task ForwardAsync_RelaysOnlyAllowListedResponseHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=target; Path=/");
        response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
        response.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");
        response.Headers.TryAddWithoutValidation("Content-Security-Policy", "default-src *");
        response.Headers.TryAddWithoutValidation("X-Frame-Options", "ALLOWALL");
        response.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=31536000");
        response.Headers.TryAddWithoutValidation("Strict-Transport-Security", "max-age=1");
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"x\"");
        response.Headers.TryAddWithoutValidation("X-Accel-Redirect", "/internal/secret");
        response.Headers.TryAddWithoutValidation("X-Transmission-Session-Id", "SID-1");
        response.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
        response.Headers.TryAddWithoutValidation("Retry-After", "5");
        response.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=a.txt");
        response.Content.Headers.TryAddWithoutValidation("Expires", "Thu, 01 Jan 2099 00:00:00 GMT");

        var result = await Get(FactoryFor(new CapturingHandler(response)));

        Assert.NotNull(result);
        var relayed = result!.Headers;
        foreach (var dropped in new[]
        {
            "Set-Cookie", "Access-Control-Allow-Origin", "Access-Control-Allow-Credentials", "Content-Security-Policy",
            "X-Frame-Options", "Cache-Control", "Strict-Transport-Security", "WWW-Authenticate", "X-Accel-Redirect", "Expires"
        })
        {
            Assert.False(relayed.ContainsKey(dropped), dropped + " must not be relayed");
        }

        Assert.Equal("SID-1", relayed["X-Transmission-Session-Id"]);
        Assert.Equal("\"v1\"", relayed["ETag"]);
        Assert.Equal("5", relayed["Retry-After"]);
        Assert.Equal("attachment; filename=a.txt", relayed["Content-Disposition"]);
    }

    [Theory]
    [InlineData("ETag", true)]
    [InlineData("Last-Modified", true)]
    [InlineData("Content-Disposition", true)]
    [InlineData("Content-Language", true)]
    [InlineData("Location", true)]
    [InlineData("Retry-After", true)]
    [InlineData("X-Session-Token", true)]
    [InlineData("x-csrf-token", true)]
    [InlineData("Set-Cookie", false)]
    [InlineData("Set-Cookie2", false)]
    [InlineData("Access-Control-Allow-Origin", false)]
    [InlineData("Content-Security-Policy", false)]
    [InlineData("Content-Security-Policy-Report-Only", false)]
    [InlineData("Cache-Control", false)]
    [InlineData("Pragma", false)]
    [InlineData("Vary", true)]
    [InlineData("Content-Type", false)]
    [InlineData("Content-Length", false)]
    [InlineData("Transfer-Encoding", false)]
    [InlineData("Connection", false)]
    [InlineData("Refresh", false)]
    [InlineData("Link", true)]
    [InlineData("X-Frame-Options", false)]
    [InlineData("X-Content-Type-Options", false)]
    [InlineData("X-XSS-Protection", false)]
    [InlineData("X-Accel-Redirect", false)]
    [InlineData("X-Accel-Buffering", false)]
    [InlineData("X-Sendfile", false)]
    [InlineData("X-Sendfile2", false)]
    [InlineData("X-Lighttpd-Send-File", false)]
    [InlineData("X-Reproxy-URL", false)]
    [InlineData("X-Forwarded-For", false)]
    [InlineData("X-Emby-Token", false)]
    [InlineData("X-Jellyfin-Anything", false)]
    [InlineData("", false)]
    public void IsRelayable_FollowsTheAllowList(string name, bool expected)
    {
        Assert.Equal(expected, RouteProxy.IsRelayable(name));
    }

    // MARK: Target failures

    [Fact]
    public async Task ForwardAsync_UnreachableTarget_IsBadGatewayNotAnException()
    {
        var handler = new DelegateHandler((_, _) => throw new HttpRequestException("connection refused"));

        var result = await Get(FactoryFor(handler));

        Assert.NotNull(result);
        Assert.Equal(ForwardError.Unreachable, result!.Error);
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public async Task ForwardAsync_SlowTarget_IsGatewayTimeoutNotAnException()
    {
        var handler = new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await Get(FactoryFor(handler, TimeSpan.FromMilliseconds(200)));

        Assert.NotNull(result);
        Assert.Equal(ForwardError.TimedOut, result!.Error);
        Assert.Equal(504, result.StatusCode);
    }

    [Fact]
    public async Task ForwardAsync_TargetThatStallsItsBody_StillTimesOut()
    {
        // The client's own timeout stops at the headers, so the body read needs a deadline of its own.
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }));

        var forward = Get(FactoryFor(handler, TimeSpan.FromMilliseconds(200)));
        var finished = await Task.WhenAny(forward, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(forward, finished);
        Assert.Equal(ForwardError.TimedOut, (await forward)!.Error);
    }

    [Fact]
    public async Task ForwardAsync_ViewerCancelling_Propagates()
    {
        // A client that went away is not a gateway failure, so it is not turned into a 504.
        var handler = new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var viewer = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Get(FactoryFor(handler), viewer.Token));
    }

    // MARK: Response size

    [Fact]
    public async Task ForwardAsync_DeclaredOversizedResponse_IsRefusedUnread()
    {
        var content = new StreamContent(new StallingStream());
        content.Headers.ContentLength = RouteProxy.MaxResponseBytes + 1L;
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var result = await Get(FactoryFor(handler));

        Assert.Equal(ForwardError.ResponseTooLarge, result!.Error);
        Assert.Equal(502, result.StatusCode);
        Assert.Empty(result.Body);
    }

    [Fact]
    public async Task ForwardAsync_UndeclaredOversizedResponse_IsCappedWhileReading()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnsizedStream(RouteProxy.MaxResponseBytes + 1L))
        });

        var result = await Get(FactoryFor(handler));

        Assert.Equal(ForwardError.ResponseTooLarge, result!.Error);
    }

    [Fact]
    public async Task ForwardAsync_ResponseAtTheCap_IsRelayed()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnsizedStream(RouteProxy.MaxResponseBytes))
        });

        var result = await Get(FactoryFor(handler));

        Assert.Equal(ForwardError.None, result!.Error);
        Assert.Equal(RouteProxy.MaxResponseBytes, result.Body.Length);
    }

    // MARK: Which responses are sandboxed

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("not a type", true)]
    [InlineData("text/html", true)]
    [InlineData("text/html; charset=utf-8", true)]
    [InlineData("TEXT/HTML", true)]
    [InlineData("application/xhtml+xml", true)]
    [InlineData("text/xml", true)]
    [InlineData("application/xml", true)]
    [InlineData("image/svg+xml", true)]
    [InlineData("application/rss+xml", true)]
    [InlineData("application/pdf", false)]
    [InlineData("application/json", false)]
    [InlineData("text/plain; charset=utf-8", false)]
    [InlineData("image/png", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData("video/mp4", false)]
    public void NeedsSandbox_CoversTypesABrowserRendersAsAScriptableDocument(string? contentType, bool expected)
    {
        Assert.Equal(expected, RouteProxy.NeedsSandbox(contentType));
    }
}
