# Testing

How to test this plugin, from a single method to a page on a test server. A change is done when the tests for every layer it touches pass.

## The Rule

Every source file has a test file, and every test proves that given inputs produce the expected output. A bug fix starts with a test that fails without the fix.

## Layers

Test each behavior at the lowest layer that can prove it. Go up a layer only for what the lower one cannot reach.

| Layer | Proves | Runs | Time |
| :--- | :--- | :--- | :--- |
| Unit | One class or function gives the right output for each input | `dotnet test` | Seconds |
| Store | Records, limits, retention, and the store file against a real temp folder | `dotnet test` | Seconds |
| Controller | A route's status codes, guards, headers, and response shape | `dotnet test` | Seconds |
| Script syntax | Each dashboard script parses as a module | `node --check` | Seconds |
| Harness | A tab or a served page renders and behaves right under Jellyfin's real CSS | The [harness](#harness) in headless Chromium | Minutes |
| Manual | A tab or a served page looks and behaves right on a real Jellyfin | A test server | Minutes |

| A change to | Needs |
| :--- | :--- |
| A model, the validator, page rendering, or `StoreAccess` | Unit |
| `StoreService`, retention, or the store file | Store |
| `CustomPagesController`, a route attribute, or the hardening headers | Controller |
| A dashboard script | Script syntax, Harness, and Manual |
| A template in `Web/Templates/` | Unit for `PageService`, Harness, and Manual signed out, as a user, and as an administrator |

The repository has no JavaScript test runner. Logic a test must reach belongs in C#, where `dotnet test` covers it.

## Layout

The test project is flat. Each test file is named after the class it covers.

| Source | Test |
| :--- | :--- |
| `Jellyfin.Plugin.CustomPages/Services/StoreAccess.cs` | `Jellyfin.Plugin.CustomPages.Tests/StoreAccessTests.cs` |
| `Jellyfin.Plugin.CustomPages/Services/RouteProxy.cs` | `Jellyfin.Plugin.CustomPages.Tests/RouteProxyTests.cs` |
| `Jellyfin.Plugin.CustomPages/Api/CustomPagesController.cs` | `Jellyfin.Plugin.CustomPages.Tests/CustomPagesControllerTests.cs`, with the route groups in `RouteEndpointTests.cs`, `StoreEndpointTests.cs`, and `FaviconTests.cs` |

## C# Tests

Tests use xUnit with plain `Assert`, and NSubstitute for Jellyfin interfaces such as `IAuthorizationContext`, `IServerApplicationPaths`, and `IActivityManager`.

```csharp
[Fact]
public void UnreadableFile_IsNotOverwritten()
{
    // Starting empty on a parse failure would let the next write destroy whatever the file held,
    // so the store answers as empty and refuses to write until an administrator looks at it.
    var store = Define();
    var file = Path.Combine(_dataPath, "custompages", "stores", "downloads.json");
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, "{ this is not json");

    var service = Create();
    var (result, _) = service.Create(store, Data("a"), null, Alice);

    Assert.Equal(StoreWriteResult.Failed, result);
    Assert.Equal("{ this is not json", File.ReadAllText(file));
}
```

### Naming and Comments

* Name the class `<Subject>Tests` and each method `Method_Scenario_Expectation`, or `Subject_Expectation` when there is one method.
* Give the class a one sentence summary of what its subject must guarantee.
* Give a test a short comment when the reason for its expectation is not obvious from its name.

### Writing a Test

* Arrange, act, and assert, separated by blank lines.
* Assert one behavior per test. Several asserts are fine when they check one outcome.
* Use `[Theory]` with `[InlineData]` for one rule over several inputs, such as every slug the pattern refuses.
* Test the edges. Null, empty, the largest value, a cancelled token, a target that answers with an error, and a caller with no identity.
* Never sleep. Pass a `TimeProvider` in and move it from the test.
* A test never reaches the network. Use a stub `HttpMessageHandler` behind the `IHttpClientFactory` the route proxy takes.
* Test every tier on every guarded route. A store or page check that passes for one tier proves nothing about the others.

### Stores

* Give each test its own temp data folder, and delete it in `Dispose`.
* Build the service over that folder so the test reads and writes the same store file the plugin does.
* Test a file from an earlier version by writing it by hand, then reading it, as `Load_ReadsAnIndentedFileFromAnEarlierVersion` does.

```bash
dotnet test
```

## Script Syntax

The dashboard scripts are ES modules that only Jellyfin can load. Check that each one parses, from a copy with an `.mjs` name so node reads it as a module.

```bash
for f in Jellyfin.Plugin.CustomPages/Web/*.js; do cp "$f" "$TMPDIR/$(basename "$f" .js).mjs" && node --check "$TMPDIR/$(basename "$f" .js).mjs"; done
```

Check that every name a script imports from `custompages_jpkribs_shared.js` is exported in Base's typings.

```bash
grep -E "export (function|const) createFilePicker\b" ~/.nuget/packages/jpkribs.jellyfin.base/<version>/assets/jpkribs_shared.d.ts
```

## Harness

The harness is `tools/screenshots/harness.html`, the page the [screenshot kit](documentation.md#screenshot-kit) serves. It loads a tab the way the dashboard does, with no Jellyfin server behind it.

* It loads Jellyfin's real stylesheets from the installed jellyfin-web, `index.html`'s first two, then the dark theme and the chunks. Base's CSS alone hides real bugs, such as Jellyfin's `.card` rule or its widened inputs.
* It mimics the upgrade of each `emby-*` element, so labels, select arrows, and checkboxes are drawn as the dashboard draws them.
* It serves the tab's HTML and JS from the repository and Base's assets from the NuGet package the plugin builds against, with stub API answers and generic data.

Click through each changed tab like a user, at 1280 and at 375 px wide.

* Type real keystrokes into every picker and lookup, and check that the list fills.
* Open every tab, every collapsible, and every dialog.
* Take a screenshot of each state and look at it.

A harness run never makes a change done. It finds problems before the test server does.

## Manual

Build the plugin, install it on a Jellyfin 12.4 test server, and restart it. See [documentation.md](documentation.md#installing-a-build-by-hand).

* Open each changed tab with a hard reload after installing, since the dashboard caches scripts.
* Open a changed served page signed out, as a user, and as an administrator, and check that each sees what its tier allows.
* Look at each changed tab at 375 px wide.
* Never test against a server that holds real pages, assets, or store records.

## Checklist

* [ ] Every changed source file has tests.
* [ ] A bug fix has a test that fails without it.
* [ ] Edges are covered, including null, empty, cancelled, and every tier.
* [ ] `dotnet test` passes.
* [ ] Every changed script passes `node --check`, and its Base imports exist.
* [ ] Every changed tab was clicked through in the harness at 1280 and 375 px, and each screenshot was looked at.
* [ ] Every changed tab and served page was checked on a test server after a hard reload.
