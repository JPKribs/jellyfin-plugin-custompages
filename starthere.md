# Start Here

Read this file before you change anything in this repository. It applies to people and to AI assistants alike.

## The Guides

Each guide in [skills](skills/) covers one kind of work. Read the guide that matches the task before you start it.

| Guide | Read it before you |
| :--- | :--- |
| [skills/glossary.md](skills/glossary.md) | Name anything, in the UI, the docs, or code. |
| [skills/style.md](skills/style.md) | Build or change anything a user sees, including labels, messages, and layout. |
| [skills/code.md](skills/code.md) | Write or change code or comments in any language. |
| [skills/testing.md](skills/testing.md) | Write or run tests. |
| [skills/profiling.md](skills/profiling.md) | Find or fix slow C#, or change code on a hot path. Profile only for a reported symptom, a hot path change, or a number that grew. |
| [skills/documentation.md](skills/documentation.md) | Write or change the README, the docs site, screenshots, or release notes. |
| [skills/review.md](skills/review.md) | Call any change done. |
| [skills/release.md](skills/release.md) | Cut a release. |
| [skills/branches.md](skills/branches.md) | Pick a branch, sync the two, and promote unstable. |
| [skills/translations.md](skills/translations.md) | Add or change a string a person reads. |

A task that touches several areas needs every matching guide. A new store setting, for example, needs the glossary, style, code, testing, documentation, and review guides.

Read [Security](docs/Security.md) before changing how a page, an asset, a route, or a store is served.

## Ground Rules

* **Base comes first.** Anything reusable lives in [JPKribs.Jellyfin.Base](https://github.com/JPKribs/jellyfin-plugin-base). Search it before you build anything, use what it has, and never copy or override it locally.
* **Describe the current release only.** Docs, labels, and comments say what the plugin does today. Release notes are the only place for what changed.
* **Keep it short.** Every label, message, and comment is as brief as it can be while staying clear.
* **One word per concept.** Use the term in the glossary, everywhere.
* **Prove it with a test.** A change is not done until a test shows that its inputs give the right output.

## Verify Before Done

Work is done when it has been checked, not when it has been written.

* Run every check the change needs, and read its output. A check that did not run has not passed, and neither has one that ran before the last change.
* Look at every changed tab on a test server, and every changed served page while signed out, as a user, and as an administrator.
* Read the whole diff before reporting, using [skills/review.md](skills/review.md).
* Check every new string against the style checklist and the glossary.
* Check every claim in a doc against the code or the running plugin.
* Report what was checked, what was not, and why. Never call work done while a check fails.

## Stop and Ask

Stop and ask before any of these, even when the task seems to call for it.

* **Store files.** A change to the record format in `data/custompages/stores/{name}.json`, or anything that resets, moves, or deletes a store file.
* **Served pages.** A change to who can reach a page, an asset, or a store, including the visibility tiers, the allow list, the sandbox, the Content Security Policy, the hardening headers, or the authentication shell.
* **Routes and secrets.** A change to the route proxy, the headers it relays or strips, how route passwords are encrypted, kept, or cleared, or the key folder `SecretsName` names.
* **Data loss.** Anything that deletes or replaces pages, assets, or records, or loosens a record, size, or retention limit.
* **Base.** Any change to Base, since every plugin takes it.
* **Settings.** Renaming, removing, or changing the default of a setting users already have, or the shape of the plugin configuration.
* **Releases.** Raising the version, publishing, or pushing to `master`.
* **The rules.** Any case where following a guide would make the result worse.

Explain what you would change, why, and what it risks. Wait for an answer before you act.

## The Repository

| Path | What it holds |
| :--- | :--- |
| `Jellyfin.Plugin.CustomPages/` | The plugin source. |
| `Jellyfin.Plugin.CustomPages/Api/` | `CustomPagesController`, every route under `/pages`. |
| `Jellyfin.Plugin.CustomPages/Configuration/` | The plugin configuration class. |
| `Jellyfin.Plugin.CustomPages/Web/` | The dashboard tabs, as HTML and JS. |
| `Jellyfin.Plugin.CustomPages/Web/Templates/` | The wrapper, inner, and shell templates a served page is built from. |
| `Jellyfin.Plugin.CustomPages/Assets/` | The plugin logo. |
| `Jellyfin.Plugin.CustomPages/Localization/` | The plugin's own strings, in `en-us.json`. |
| `Jellyfin.Plugin.CustomPages/Models/` | The configuration records and the route responses. |
| `Jellyfin.Plugin.CustomPages/Services/` | Page rendering, the configuration check, the route proxy, route secrets, and stores. |
| `Jellyfin.Plugin.CustomPages/Tasks/` | The store retention task. |
| `Jellyfin.Plugin.CustomPages/Utilities/` | Extension methods, such as the page visibility tiers. |
| `Jellyfin.Plugin.CustomPages.Tests/` | The xUnit tests. |
| `docs/` | The documentation site, built with MkDocs. |
| `build.yaml` | The version, the artifacts, and the release notes. |
| `build.sh` | Builds the plugin zip into `dist/`. |
| `skills/` | The guides. |
| `tools/` | Development tools that never ship, such as the screenshot harness. |
| `../Shared/jellyfin-plugin-base/` | A local checkout of Base, when present. Read it, and never change it from work on this plugin. |
