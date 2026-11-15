# Code

How to write, comment, and test code in this plugin. It covers C#, JavaScript, HTML, CSS, shell, and YAML.

## Base First

[JPKribs.Jellyfin.Base](https://github.com/JPKribs/jellyfin-plugin-base) holds everything two plugins could share. Its README lists every component and helper. Read it before you write anything new.

### Use What Base Has

* Use the Base type or component when one fits. Never write a local version, a wrapper that renames it, or a CSS override of a `base-` class.
* When Base almost fits, change Base. Never patch around it locally.
* Delete a local copy as soon as Base offers the same thing. Do it in the commit that moves to that Base version.
* Never shadow a Base name. A local `FormatUtilities` beside Base's `FormatUtilities` is ambiguous.
* Read Base from the package this plugin builds against. The C# source is in `~/.nuget/packages/jpkribs.jellyfin.base/<version>/src/`, and the JS typings are in `assets/jpkribs_shared.d.ts` beside it.
* Treat the local Base checkout as read only. Base has its own owner and its own release, so report a gap and wait for the version that closes it.
* When Base differs from the local code it replaces, keep the local behavior in a small step around the Base call, and cover it with a test. `StoreService` creates the stores folder before it asks `AtomicFile` to write, since `AtomicFile` does not create folders.

| Need | Use from Base |
| :--- | :--- |
| Plugin class and config lock | `PluginBase<TPlugin, TConfiguration>` |
| Dashboard pages and their shared CSS and JS | `GetSharedPages("custompages")` |
| Scheduled task | `PluginScheduledTask` |
| Outbound HTTP | `JpkHttp` and `HttpResult` |
| HTTP handlers | `JpkRetryHandler` with `JpkRetryOptions`, and `RedirectRefusingHandler` |
| Addresses a user supplied | `SsrfGuard` |
| Secrets | `SecretProtector`, `AddPluginSecretProtector`, and `StableSecretProtection` |
| Comparing a presented secret | `SecretCompare.FixedTimeEquals` |
| Writing a file whole or not at all | `AtomicFile`, with `ownerOnly: true` for a key or secret |
| Paths inside a folder | `PathContainment` |
| Streams | `CappedReadStream`, `StreamLimits`, and `StreamUtilities` |
| The clock | `TimeProvider`, and `UtcTime` for stored times |
| Activity log | `ActivityLogger`, `ActivitySummary`, `ActivityText` |
| Served standalone pages | `PublicPage`, `StatusPage`, and `TemplateLoader` |
| JSON state file | `JsonFileStore<T>` |
| Formatting and names | `FormatUtilities`, `FileNameSanitizer` |
| Config page lifecycle | `createConfigPage` |
| Page helpers | The `shared` bag `createConfigPage` returns |
| Record picker | `base-selector` with `base-selector-dropdown` and `base-selector-actions` |
| Counts | `shared.renderCards` with `base-cards compact` |
| Uploads and stored files | `createFilePicker`, and `base-asset-list` for what is stored |
| Users | `createUserMultiSelector` |
| A used amount of a limit | `createUsageMeter` |
| Lists and row editors | `createCheckboxList`, `createRepeater`, `createSortableCardList` |
| Dialogs | `openDialog`, `confirmDialog`, `promptDialog`, `openEditorDialog`, `bindDialog` |

### Move Code Into Base

Code belongs in Base when another plugin could use it unchanged. A helper with no Custom Pages types in its signature is a candidate.

1. Add it to `../Shared/jellyfin-plugin-base/src/`, one file per component, following Base's [code guide](https://github.com/JPKribs/jellyfin-plugin-base/blob/main/skills/code.md).
2. Add tests in `JPKribs.Jellyfin.Base.Tests/`, and JS tests in `JPKribs.Jellyfin.Base.Tests/js/`.
3. Document it in Base's docs site inventory.
4. Pack it locally with `scripts/pack.sh` and build this plugin against it with `-p:RestoreAdditionalProjectSources=<base>/nupkg`.
5. Commit and push to Base, then run Base's **Release** workflow.
6. Raise the `JPKribs.Jellyfin.Base` version in this plugin's csproj, delete the local copy, and add a `developer` changelog line naming the Base version.

Never commit this plugin against an unreleased Base package.

## Comments

### The Rules

* Every class, method, function, and component gets a header comment.
* The header is the name, then a summary of one or two sentences, leaning to one.
* List inputs and outputs when there are any. One line each, as `name [type]: What or why`.
* Write no inline comments, with one exception. A branch whose reason is not obvious gets one short line above it.
* A branch comment says why, not what. Keep it under 15 words.
* Never comment what the code already says. `// Check if values differ` is noise.
* Never leave commented out code, TODO, FIXME, or HACK. Open an issue instead.
* Never describe history. "Used to", "now", and "was changed to" belong in commit messages.
* Use no em dashes or semicolons in comments.

```text
<Name>
<What it does, in one sentence.>

Inputs
name [type]: What it is or why it is needed.

Output
[type]: What comes back.
```

Leave out **Inputs** when there are none, and **Output** when nothing comes back.

### C#

C# uses XML docs so the compiler and IntelliSense read them. The member name is the title. The type comes from the signature, so a `param` gives only the what or why.

```csharp
/// <summary>
/// Resolves how much of a store a caller may read.
/// </summary>
/// <param name="store">The store being read.</param>
/// <param name="caller">The resolved caller.</param>
/// <returns>The read access level.</returns>
public static StoreAccessLevel ResolveRead(PageStore store, StoreCaller caller)
{
    ArgumentNullException.ThrowIfNull(store);
```

* Every type and every member that is not private gets `<summary>`, every `<param>`, and `<returns>` when it returns a value.
* Private members get a `<summary>` when the name does not say enough.
* Use `<inheritdoc/>` on an interface or override member.
* Write `true` and `false` as `<c>true</c>` and `<c>false</c>`.
* A constructor summary is "Initializes a new instance of the <see cref="Name"/> class."
* Skip tautologies. `<param name="item">The item.</param>` says nothing, so write what the item is for.
* Put one type in each file, named after the type. A small enum or record used by one class may share its file, as `StoreAccessLevel` and `StoreCaller` share `StoreAccess.cs`.
* Match the namespace to the folder. The controller folder is `API/` and its namespace is `Jellyfin.Plugin.CustomPages.Api`.

### JavaScript

```js
// addAssetFiles
// Reads each picked file and saves it as an asset, replacing one with the same name.
//
// Inputs
// files [File[]]: The files the picker accepted, each already under the size limit.
// refused [string]: The picker's refusal sentences for the files it turned away, or an empty string.
function addAssetFiles(files, refused) {
```

* Comment every top level function, every function assigned to `shared`, and every exported function.
* Do not comment the methods inside a returned handle. List them in the Output line instead.
* Use no `// ====` banners.
* Each page script imports what it uses from Base by the relative path `./configurationpage?name=custompages_jpkribs_shared.js`. There is no plugin shared module.

### HTML

```html
<!-- Store counts -->
<div id="storeCounts" class="base-cards compact"></div>
```

* One comment line naming each section or dialog where it starts, and an optional second line saying what it is for.
* Use no inline `style` attributes. The route rows on the **Pages** tab still have some, per [style.md](style.md#hand-built-pieces).
* The templates in `Templates/` are served to visitors, not to the dashboard. They use the classes of Base's status page, such as `base-status-card` and `base-btn`, and no dashboard CSS.

### CSS

```css
/* custompages-example */
/* What it looks like, in one line. */
/* Example: <div class="custompages-example"></div> */
.custompages-example { ... }
```

* This plugin ships no CSS file. Add one only for layout that Base cannot hold, and register it in `Plugin.GetPages`.
* Three lines above each component, the name, what it looks like, and one example.
* Prefix every class with `custompages-` in kebab case. `base-` is reserved for Base.
* Use Base tokens such as `--base-border` and `--base-fg` for colors. Never write a hex value for something Base already colors.

### Shell and YAML

* A shell script opens with a comment saying what it does and how to run it.
* A YAML file opens with a comment saying what it is for. Comment a step only when its reason is not obvious.

## C# Conventions

* Target `net10.0` with `Nullable` on, `TreatWarningsAsErrors` on, and `AnalysisMode` set to `AllEnabledByDefault`.
* Keep `jellyfin.ruleset` identical across plugins. Raise a rule change in every plugin at once.
* Suppress a rule only at the narrowest scope, with a reason.
* Use file scoped namespaces, `sealed` classes, and records for data.
* Name private fields `_camelCase`.
* End every async method name in `Async`, pass the `CancellationToken` through, and use `ConfigureAwait(false)` on every await.
* Guard public arguments with `ArgumentNullException.ThrowIfNull`.
* Log through `ILogger<T>` with static, named templates. Pass untrusted text through `FormatUtilities.SanitizeForLog`.
* Catch a specific exception, or filter with `when`. Rethrow `OperationCanceledException` when the token was cancelled.
* Read the clock once in a method and reuse the value. Two reads can straddle a second and disagree.
* Take a `TimeProvider` wherever time decides an outcome, so a test can move it. `StoreService` takes one for record times and retention.
* Register services in `PluginServiceRegistrator`. `StoreService` and `StoreActivity` are singletons, since each holds state for the process.

## JavaScript Conventions

* Each config page is an ES module whose default export takes the view and calls `createConfigPage` with the plugin ID, `tabsKey: 'custompages'`, its tab index, the `TABS` list, `bind`, and `load`.
* The page scripts use `'use strict'`, `var`, and function declarations. Keep new code in the same style as the file around it.
* Keep one handle for each Base component a page draws, such as `userPicker` and `usageMeter`. Draw it once in `bind`, and call `setValue` when only its values change.
* Escape every value placed in HTML with `Shared.escapeHtml`.
* Report results with `Shared.setStatus` on the status line beside the action.
* Save through `Shared.getConfig` and `Shared.saveConfig`, changing only the list the tab owns, so a save never drops another tab's edits.
* Hold edits in the in memory records until **Save**, and read the editor into the current record before the picker moves to another one.

## Security

* Store every route password with `SecretProtector`, through `RouteSecrets.Protect`. Never write a secret to the config, a file, or a log in plain text.
* Send a stored route password to the browser only as `SECRET_KEPT`. The server keeps the stored value when the sentinel comes back.
* Pass every value from a request, a page, or a file through `FormatUtilities.SanitizeForLog` before logging it.
* Forward a route only to the target fixed in configuration, through the named client `RouteProxy.HttpClientName`, and strip the viewer's Jellyfin credentials before it is sent.
* Interpret store tiers only in `StoreAccess`. Every store route asks it, so the routes cannot drift apart.
* Guard every administrator route with `[Authorize(Policy = "RequiresElevation")]`, and every User page route with `[Authorize]`.
* Check the page's tier and its allow list before rendering anything for a User or Admin page.
* Validate every configuration save in `ConfigurationValidator`, since a configuration can arrive from any API client.
* Write store files with `AtomicFile`, so a reader sees the old records or the new ones and never half of either.
* Build a store file path only from a name that passes `StoreService.IsValidStoreName`, and accept an asset name only when it passes `PageService.IsValidAssetName`. Use `PathContainment` for any other path built from a request.
* Escape every value placed in HTML, in the dashboard and in the templates.
* Set the hardening headers on every served page with `PublicPage.ApplyHardeningHeaders`, and never loosen the Content Security Policy or the sandbox without approval.

## Tests

Every source file has a test file. See [testing.md](testing.md) for each layer and how to write tests.

## Checklist

* [ ] Base's source and typings were searched, nothing here duplicates Base, and anything reusable went to Base first.
* [ ] Every class, method, function, and component has a header.
* [ ] Inline comments appear only above branches, and say why.
* [ ] Every changed source file has tests that pass, per [testing.md](testing.md).
* [ ] The [security](#security) rules hold.
* [ ] `dotnet build` shows no warnings.
