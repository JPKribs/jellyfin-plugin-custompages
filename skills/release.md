# Release

How to cut a release of this plugin, from the version number to the manifest every server reads.

## Versions

A version is `JJ.JJ.F.B`.

| Part | Means | Raise it when |
| :--- | :--- | :--- |
| `JJ.JJ` | The Jellyfin version the release targets, such as `12.4` | The plugin moves to a new Jellyfin version |
| `F` | The feature release | The release adds or changes behavior a user will notice |
| `B` | The bug release within that feature | The release only fixes bugs |

Raising a part sets every part after it to `0`. `12.4.2.4` becomes `12.4.3.0` for a feature and `12.4.2.5` for a fix.

## Branch

A feature release is cut on `unstable`, then **Sync Branches** with `promote` moves `master` to it. A bug release is cut on `master`, and the sync carries it to `unstable`. See [branches.md](branches.md).

## Before the Release

1. **Base.** When the release needs a Base change, release Base first with its **Release** workflow, then raise `JPKribs.Jellyfin.Base` in `Jellyfin.Plugin.CustomPages/Jellyfin.Plugin.CustomPages.csproj` and delete any local copy Base replaces. Wait until NuGet serves the version, and never release against a local package. Raise Base on a branch, and merge it once every check below has passed on it.
2. **Stored data.** When the shape of the plugin configuration or a store file changed, check that a configuration and a store file from the last release still load. Either change is a [stop and ask](../starthere.md#stop-and-ask) item.
3. **Build.**

    ```bash
    dotnet build
    ```

4. **Tests.**

    ```bash
    dotnet test
    ```

    Check each dashboard script with `node --check`, per [testing.md](testing.md#script-syntax).

5. **Test server.** Install the build on a test server and open each tab with a hard reload. Open a page of each tier signed out, as a user, and as an administrator, and read and write a store.

    ```bash
    ./build.sh
    ```

    Every check must run on the commit that is released. Run them again after any later change.

6. **Docs.** Every page describes this release. Retake any screenshot whose subject changed, and all of them when the Base version changed, with the [screenshot kit](documentation.md#screenshot-kit). Open each new PNG and look at it. Update [Security](../docs/Security.md) when a protection changed, and [Versioning](../docs/Versioning.md) when the Jellyfin target changed.

    ```bash
    mkdocs build --strict
    ```

7. **Review.** Run [review.md](review.md) over everything since the last release.

## Set the Release

Edit `build.yaml`.

1. Set `version` to the new version.
2. Replace the whole `changelog` with this release's entries. Each release lists only its own changes, so drop every line that shipped in the last release and every line a later change in this release replaced. See [documentation.md](documentation.md#release-notes) for how to write them.
3. Say in `changed` when a page, an asset, a route, or a store behaves differently for visitors.
4. Name the Base version in `developer` when it changed, such as "Uses JPKribs.Jellyfin.Base 2026.11.18", with what it replaced locally.
5. Keep `description` and `overview` true for this release. `manifest.json` takes its own description, so update both together.

Commit it on its own, on `master` with every change for the release already merged.

```bash
git commit -am "Set the release to 12.4.3.0"
```

```bash
git push
```

## Publish

Follow these steps in order, and skip none.

1. **Draft.** The **Create/Update Release Draft** workflow builds the zip and writes a draft release named `v<version>` with the changelog as its notes. A push that carries any file besides `build.yaml` starts it. A push that only changes `build.yaml` does not, so start it from the Actions tab only then. Draft builds run one at a time, so a push and a manual start that overlap build one after the other.

    ```bash
    gh run list -L 8
    ```

2. **Check.** Wait until no draft build is queued or running, since each one replaces the zip. Then list the releases and open the draft on GitHub. The notes must match `build.yaml`, and the zip and its `.md5` must be attached.

    ```bash
    gh release list -L 5
    ```

3. **Release.** Publish the draft. The **Publish Plugin** workflow waits for any draft build still running, hashes the attached zip, checks it against the `.md5`, adds the version to `manifest.json`, and commits "Update manifest for release v<version>". A draft build never replaces the assets of a published release.
4. **Confirm.** Check that the manifest commit landed and that the checksum of the new version in `manifest.json` matches the `.md5` on the release.
5. **Pull.**

    ```bash
    git pull --ff-only
    ```

6. **Verify.** On a test server, add the repository URL `https://raw.githubusercontent.com/JPKribs/jellyfin-plugin-custompages/master/manifest.json`, install the release from the catalog, restart, and open each tab.

## When a Release Is Wrong

* Never edit or reuse a published version. Fix it in a new bug release.
* When a release breaks servers, publish the fix as the next bug release at once.
* When the manifest entry itself is wrong, run **Publish Plugin** by hand with the release tag after fixing the cause.

## Checklist

* [ ] The version follows `JJ.JJ.F.B`, and later parts are `0`.
* [ ] Base is released and referenced, with local copies deleted.
* [ ] A configuration and a store file from the last release still load.
* [ ] `dotnet build`, `dotnet test`, and the script checks pass on the released commit.
* [ ] Each tab and each tier of served page was checked on a test server.
* [ ] Docs, screenshots, Security, and Versioning describe this release.
* [ ] `mkdocs build --strict` passes.
* [ ] The changelog lists only this release, in the four keys.
* [ ] The draft matches `build.yaml` and has the zip and `.md5` before it is published.
* [ ] The manifest checksum matches the release's `.md5`.
* [ ] The manifest commit is pulled.
