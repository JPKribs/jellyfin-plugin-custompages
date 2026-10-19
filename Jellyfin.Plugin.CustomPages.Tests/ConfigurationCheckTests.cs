using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.CustomPages.Api;
using Jellyfin.Plugin.CustomPages.Services;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Tests for <c>/pages/config/check</c>, which is how the dashboard learns why a configuration save
/// would be refused. Jellyfin itself answers a refused save with a generic error.
/// </summary>
public class ConfigurationCheckTests
{
    private static CustomPagesController Create(string body)
    {
        var controller = new CustomPagesController(
            Substitute.For<IPageService>(), Substitute.For<IStoreService>(), Substitute.For<IAuthorizationContext>(),
            Substitute.For<System.Net.Http.IHttpClientFactory>(), Substitute.For<MediaBrowser.Controller.IServerApplicationPaths>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return controller;
    }

    private static async Task<JsonElement> Check(string body)
    {
        var result = await Create(body).CheckConfiguration(CancellationToken.None);
        var content = Assert.IsType<ContentResult>(result);
        return JsonDocument.Parse(content.Content!).RootElement;
    }

    // The shape the dashboard posts: Pascal cased, tiers as names.
    private static string Page(string slug, string routeName, string password)
        => "{\"Slug\":\"" + slug + "\",\"Visibility\":\"Anonymous\",\"Enabled\":true,\"Unsandboxed\":true,"
            + "\"ApiRoutes\":[{\"Name\":\"" + routeName + "\",\"Url\":\"http://nas:9091/rpc\",\"Password\":\"" + password + "\"}]}";

    [Fact]
    public async Task UnpairableKeptPassword_ReportsTheActualReason()
    {
        var json = await Check("{\"Pages\":[" + Page("tx", "rpc", JPKribs.Jellyfin.Base.SecretProtector.KeptSentinel) + "]}");

        Assert.False(json.GetProperty("ok").GetBoolean());
        var message = json.GetProperty("message").GetString();
        Assert.Contains("rpc", message, System.StringComparison.Ordinal);
        Assert.Contains("Enter the password again", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidConfiguration_IsAccepted()
    {
        var json = await Check("{\"Pages\":[" + Page("tx", "rpc", "a new password") + "]}");

        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ValidatorRefusal_ReportsTheValidatorsReason()
    {
        var json = await Check("{\"Pages\":[" + Page("dup", "a", "x") + "," + Page("dup", "b", "y") + "]}");

        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Contains("dup", json.GetProperty("message").GetString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumericTiers_AreReadToo()
    {
        var json = await Check("{\"Pages\":[{\"Slug\":\"p\",\"Visibility\":0,\"Enabled\":true}]}");

        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task UnreadableBody_IsReportedNotThrown()
    {
        var json = await Check("{ not json");

        Assert.False(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Check_RequiresElevation()
    {
        var method = typeof(CustomPagesController).GetMethod(nameof(CustomPagesController.CheckConfiguration))!;
        var authorize = method.GetCustomAttributes<AuthorizeAttribute>().Single();

        Assert.Equal("RequiresElevation", authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }
}
