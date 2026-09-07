using System;
using System.IO;
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
/// Tests for the store endpoints on <see cref="CustomPagesController"/>. The store's tiers are resolved
/// here from the calling credentials, so this is where an anonymous caller, a user, an administrator,
/// and an API key are told apart.
/// </summary>
public class StoreEndpointTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private enum Credential
    {
        None,
        User,
        Admin,
        ApiKey,
        Invalid
    }

    private static (CustomPagesController Controller, IStoreService Stores) Create(Credential credential)
    {
        var pages = Substitute.For<IPageService>();
        var stores = Substitute.For<IStoreService>();
        var authorization = Substitute.For<IAuthorizationContext>();

        if (credential == Credential.Invalid)
        {
            authorization.GetAuthorizationInfo(Arg.Any<HttpRequest>())
                .Returns<Task<AuthorizationInfo>>(_ => throw new SecurityException("bad token"));
        }
        else
        {
            User? user = null;
            if (credential is Credential.User or Credential.Admin)
            {
                user = new User("caller", "Default", "Default") { Id = Alice };
                user.SetPermission(PermissionKind.IsAdministrator, credential == Credential.Admin);
            }

            authorization.GetAuthorizationInfo(Arg.Any<HttpRequest>())
                .Returns(Task.FromResult(new AuthorizationInfo
                {
                    User = user,
                    IsApiKey = credential == Credential.ApiKey
                }));
        }

        var controller = new CustomPagesController(
            pages, stores, authorization, Substitute.For<System.Net.Http.IHttpClientFactory>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return (controller, stores);
    }

    private static PageStore Store(
        PageVisibility read = PageVisibility.Admin,
        PageVisibility write = PageVisibility.User,
        StoreScope scope = StoreScope.All,
        bool editOwn = false)
        => new PageStore
        {
            Name = "downloads",
            ReadAccess = read,
            ReadScope = scope,
            WriteAccess = write,
            AllowEditOwn = editOwn
        };

    private static void SetBody(CustomPagesController controller, string json)
    {
        controller.Response.Body = new MemoryStream();
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        ObjectResult obj => obj.StatusCode ?? 200,
        ContentResult => 200,
        _ => -1
    };

    // MARK: Unknown stores

    [Fact]
    public async Task Read_UnknownStore_NotFound()
    {
        var (controller, _) = Create(Credential.Admin);
        Assert.Equal(404, StatusOf(await controller.StoreRead("nope", null)));
    }

    // MARK: Read authorization

    [Fact]
    public async Task Read_AnonymousCallerOnGatedStore_AsksForSignIn()
    {
        // 401 rather than 403, so the page's own sign in prompt is the natural next step.
        var (controller, stores) = Create(Credential.None);
        stores.Find("downloads").Returns(Store());

        Assert.Equal(401, StatusOf(await controller.StoreRead("downloads", null)));
    }

    [Fact]
    public async Task Read_SignedInButBelowTier_IsForbidden()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store());

        Assert.Equal(403, StatusOf(await controller.StoreRead("downloads", null)));
    }

    [Fact]
    public async Task Read_OwnScope_IsNarrowedToTheirOwnRecords()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store(read: PageVisibility.User, scope: StoreScope.Own));

        await controller.StoreRead("downloads", null);

        stores.Received(1).Read(Arg.Any<PageStore>(), Alice);
    }

    [Fact]
    public async Task Read_OwnScope_StillShowsAnAdministratorEverything()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store(read: PageVisibility.User, scope: StoreScope.Own));

        await controller.StoreRead("downloads", null);

        stores.Received(1).Read(Arg.Any<PageStore>(), null);
    }

    [Fact]
    public async Task Read_Administrator_SeesEveryRecord()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());

        await controller.StoreRead("downloads", null);

        stores.Received(1).Read(Arg.Any<PageStore>(), null);
    }

    [Fact]
    public async Task Read_ApiKey_SeesEveryRecord()
    {
        // A Jellyfin API key is administrator issued and already carries administrator reach, and a
        // store exists so something outside the browser can pick work up and write results back.
        var (controller, stores) = Create(Credential.ApiKey);
        stores.Find("downloads").Returns(Store());

        await controller.StoreRead("downloads", null);

        stores.Received(1).Read(Arg.Any<PageStore>(), null);
    }

    [Fact]
    public async Task Read_InvalidToken_FallsBackToAnonymous()
    {
        var (controller, stores) = Create(Credential.Invalid);
        stores.Find("downloads").Returns(Store());

        Assert.Equal(401, StatusOf(await controller.StoreRead("downloads", null)));
    }

    [Fact]
    public async Task Read_SetsNoStoreHeaders()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());

        await controller.StoreRead("downloads", null);

        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"]);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
    }

    // MARK: Write authorization

    [Fact]
    public async Task Write_BelowWriteTier_IsRefused()
    {
        var (controller, stores) = Create(Credential.None);
        stores.Find("downloads").Returns(Store());
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(401, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        await Task.CompletedTask;
        stores.DidNotReceive().Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task Write_NoId_CreatesOwnedByCaller()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store());
        stores.Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid>())
            .Returns((StoreWriteResult.Ok, new StoreRecord { Id = "r1" }));
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(200, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        stores.Received(1).Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Alice);
    }

    [Fact]
    public async Task Write_AnonymousStore_AcceptsUnauthenticatedCreate()
    {
        var (controller, stores) = Create(Credential.None);
        stores.Find("suggestions").Returns(Store(read: PageVisibility.Admin, write: PageVisibility.Anonymous));
        stores.Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid>())
            .Returns((StoreWriteResult.Ok, new StoreRecord { Id = "r1" }));
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(200, StatusOf(await controller.StoreWrite("suggestions", CancellationToken.None)));
        stores.Received(1).Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Guid.Empty);
    }

    [Fact]
    public async Task Write_WithId_ByASubmitterWithoutEditOwn_IsForbidden()
    {
        // The submitter may create a record and read it back, but the status an administrator writes
        // onto it is not theirs to rewrite.
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store(read: PageVisibility.User, scope: StoreScope.Own));
        SetBody(controller, "{\"id\":\"r1\",\"data\":{\"status\":\"done\"}}");

        Assert.Equal(403, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        stores.DidNotReceive().Update(
            Arg.Any<PageStore>(), Arg.Any<string>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Write_WithId_ByASubmitterWithEditOwn_IsNarrowedToTheirOwn()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store(read: PageVisibility.User, scope: StoreScope.Own, editOwn: true));
        stores.Update(Arg.Any<PageStore>(), "r1", Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns((StoreWriteResult.Ok, new StoreRecord { Id = "r1" }));
        SetBody(controller, "{\"id\":\"r1\",\"data\":{\"note\":\"typo\"}}");

        await controller.StoreWrite("downloads", CancellationToken.None);

        stores.Received(1).Update(Arg.Any<PageStore>(), "r1", Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Alice);
    }

    [Fact]
    public async Task Write_WithId_ByAnAdministrator_ReachesEveryRecord()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());
        stores.Update(Arg.Any<PageStore>(), "r1", Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns((StoreWriteResult.Ok, new StoreRecord { Id = "r1" }));
        SetBody(controller, "{\"id\":\"r1\",\"data\":{\"status\":\"done\"}}");

        await controller.StoreWrite("downloads", CancellationToken.None);

        stores.Received(1).Update(Arg.Any<PageStore>(), "r1", Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), null);
    }

    [Fact]
    public async Task Write_IsCaseInsensitiveAboutTheEnvelope()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());
        stores.Update(Arg.Any<PageStore>(), "r1", Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns((StoreWriteResult.Ok, new StoreRecord { Id = "r1" }));
        SetBody(controller, "{\"Id\":\"r1\",\"Data\":{\"status\":\"done\"}}");

        await controller.StoreWrite("downloads", CancellationToken.None);

        stores.Received(1).Update(Arg.Any<PageStore>(), "r1", Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), null);
    }

    [Fact]
    public async Task Write_MalformedBody_IsBadRequest()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());
        SetBody(controller, "{ not json");

        Assert.Equal(400, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
    }

    [Fact]
    public async Task Write_OversizedBody_IsRefusedBeforeParsing()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());
        SetBody(controller, "{\"data\":\"" + new string('x', StoreService.MaxRecordBytes + 8192) + "\"}");

        Assert.Equal(413, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        stores.DidNotReceive().Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task Write_FullStore_IsConflict()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store());
        stores.Create(Arg.Any<PageStore>(), Arg.Any<System.Text.Json.Nodes.JsonNode?>(), Arg.Any<string?>(), Arg.Any<Guid>())
            .Returns((StoreWriteResult.Full, (StoreRecord?)null));
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(409, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
    }

    // MARK: Delete

    [Fact]
    public async Task Delete_WithoutModifyAccess_IsForbidden()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store());
        SetBody(controller, "{\"id\":\"r1\"}");

        Assert.Equal(403, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
        stores.DidNotReceive().Delete(Arg.Any<PageStore>(), Arg.Any<string>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Delete_WithoutAnId_IsBadRequest()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());
        SetBody(controller, "{}");

        Assert.Equal(400, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
    }

    [Fact]
    public async Task Delete_ByOwner_IsNarrowedToTheirOwn()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Find("downloads").Returns(Store(read: PageVisibility.User, scope: StoreScope.Own, editOwn: true));
        stores.Delete(Arg.Any<PageStore>(), "r1", Arg.Any<Guid?>()).Returns(true);
        SetBody(controller, "{\"id\":\"r1\"}");

        Assert.Equal(204, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
        stores.Received(1).Delete(Arg.Any<PageStore>(), "r1", Alice);
    }

    [Fact]
    public async Task Delete_MissingRecord_IsNotFound()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Find("downloads").Returns(Store());
        stores.Delete(Arg.Any<PageStore>(), "r1", Arg.Any<Guid?>()).Returns(false);
        SetBody(controller, "{\"id\":\"r1\"}");

        Assert.Equal(404, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
    }
}
