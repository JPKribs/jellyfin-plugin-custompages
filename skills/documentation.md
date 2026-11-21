# Documentation

How to write the README, the docs site, screenshots, and release notes for this plugin. It applies to every page under `docs/`, to `README.md`, and to the `changelog` in `build.yaml`.

## The One Rule

Documentation describes the release it ships with. It reads as if this version were the only one that ever existed.

* Write what the plugin does. Never write what it used to do, what it no longer does, or what it now does.
* Write how a feature works. Never write that a bug was fixed, a problem is gone, or a limit was lifted.
* History belongs in the release notes. See [Release Notes](#release-notes).

| Do not write | Write |
| :--- | :--- |
| Stores now keep records across a restart. | Stores keep records across a restart. |
| The old upload button was removed. | Leave it out. |
| No longer serves gated assets by URL. | Gated assets are never served by URL. |
| Fixed an issue where retention missed idle stores. | A scheduled task applies Retention to stores nobody reads or writes. |
| New in 12.4.3.0, a store can... | A store can... |

"Now" and "no longer" are fine when they describe state rather than history. "This page is no longer available" describes what the visitor sees, so it stays.

A version number belongs in docs only when the reader needs it to act, such as the minimum Jellyfin version or the oldest release that can upgrade directly. Keep those facts in [Installation](../docs/Installation.md) and [Versioning](../docs/Versioning.md), and name the version outright instead of "this release" or "an older release".

The plugin requires Jellyfin 12.4, so leave out notes about older Jellyfin versions.

## Writing Style

### Sentences

* Use present tense and plain declarative sentences.
* Describe the system in the third person. "Custom Pages serves the page."
* Address the reader in the second person only in steps. "Turn on **Allow system access** for the page."
* State one fact per sentence. Aim for 20 words or fewer and split anything over 30.
* Use no contractions. Write "cannot", not "can't".
* Use no em dashes, en dashes as dashes, or semicolons. Split the sentence, or join it with a comma and "and".
* Use colons only to introduce a list or a code block.
* Use parentheses rarely. Move the aside into its own sentence.
* End every sentence with a period.
* Write compounds as separate words the way the UI does, such as "signed in" and "write only".
* Write one paragraph per line. Never hard wrap prose.
* Use no marketing words such as "powerful", "seamless", "easily", or "simply".
* Write examples with generic names, such as the `rules` page, the `queue` store, and `logo.png`, never a real server, person, or address.

### Headings

* Use Title Case for every heading. "## Who Can Read and Who Can Write".
* Page titles are `#` and appear once per page.
* Name the thing, not the action, unless the section is a procedure. "## Asset Visibility" and "## Add the Repository" are both correct.
* Never rename a heading without fixing its anchors. `mkdocs build --strict` fails on a broken anchor.

### UI Names

* Write a label exactly as the page shows it, in bold. **Retention (days)**.
* Write a path through the UI with `>` between the steps, in bold. **Dashboard > Plugins > Custom Pages**.
* Write a tab as its label, in bold. **Pages**, **Assets**, **Stores**.
* Quote activity log names and on-screen messages in double quotes. "Records added".
* Write routes, files, settings keys, and values in code. `POST /pages/store/{name}/write`.

A docs page and the UI must use the same word for the same thing. When a label changes, search `docs/` for the old one and change it everywhere in the same commit.

## Site Structure

The docs site is MkDocs with the Material theme, built from `docs/` by `mkdocs.yml` and published to `https://jpkribs.github.io/jellyfin-plugin-custompages/`.

### Navigation

Keep the nav in this order.

1. **Overview** (`index.md`). What the plugin is, how it works, and the advice on exposure.
2. **Getting Started**. Installation.
3. **Pages**. Authoring, Visibility, Restricting Access, and System Access.
4. **Features**. Storing Data, and Images and Assets.
5. **Reference**. Security and Versioning.

Name page files in PascalCase with no spaces, such as `StoringData.md`.

## Security Page

[docs/Security.md](../docs/Security.md) lists every protection a served page, an asset, a route, and a store has. It is the page a reader checks before exposing a page publicly.

Review it after every change, and update it in the same commit when the change affects any of these.

* Who can reach a page, an asset, a route, or a store.
* The sandbox, the Content Security Policy, or a hardening header.
* What the route proxy forwards, strips, or relays.
* A record, size, or response limit.

### Page Templates

A feature page opens with one sentence on what the feature is, then one section per question a reader asks, then **Limits** and **Security** where they apply.

An API route follows this shape.

````markdown
### POST /pages/store/{name}/write

One sentence on what the route does and who may call it.

```json
{ "data": { "url": "https://example.com" }, "label": "Holiday photos" }
```

| Status | Meaning |
| :--- | :--- |
| `200` | The record was saved |
````

### Settings References

List settings as bullets in the order the page shows them. Start each with the label in bold and follow it with a verb.

```markdown
* **Write** is the tier that may add a record.
```

When a page has many settings with defaults, use a table instead.

| Setting | What it does | Default |
| :--- | :--- | :--- |
| **Record limit** | Refuses writes past this count | 1000 |

Table cells have no closing period. Keep them to one sentence.

### Troubleshooting

Write trouble as a table of what the reader sees and what to do.

| Reason | What to do |
| :--- | :--- |
| Another page already uses that slug. | Change the **Slug**, or delete the other page |

Put the exact on-screen text in the first column. Use italics for a placeholder inside it, such as *server*.

### Admonitions

Give every admonition a short title.

```markdown
!!! warning "Use at your own risk"
    One or two sentences.
```

* `note` for a detail the reader needs.
* `tip` for a better way to do something.
* `warning` for anything that can lose data or break a server.
* `info` for a requirement, such as both servers running the plugin.

### Links

* Link pages relatively with their `.md` name, such as `[Storing Data](StoringData.md#limits-and-storage)`.
* End a Getting Started page with one "Next" line, such as `Next: [Authoring](Authoring.md).`
* Link to Base's README for a shared component instead of describing it again.

## README

The README is a landing page. The docs site holds the detail.

1. The icon, `<img src="docs/assets/icon.png" alt="Custom Pages" width="96" align="left">`, then a one paragraph pitch and `<br clear="left">`.
2. `## Documentation`. One sentence on what the docs cover, a bold link to the site, then the repository URL for Jellyfin in a code block.
3. Any other section the owner keeps, as it is.

Use `##` for README headings. Keep the README under 40 lines.

## Screenshots

### What to Capture

* Capture every tab and dialog a docs page describes, and each state of a served page.
* Capture the dark theme only, which is Jellyfin's default.
* Capture from the [screenshot kit](#screenshot-kit) with its generic data. Never capture from a live server or its data.
* Fill lists, counts, and records first, so nothing shows empty unless the shot is of the empty state.
* Hide nothing with editing. When a value is private, change the test data instead.

### Sizes

Every capture is headless Chromium at 2x.

| Kind | Size | How |
| :--- | :--- | :--- |
| Dashboard tab | 1428 wide | A 720 px window, clipped to the 714 px content area from the first element of the shot to its last |
| Served page | 1920 wide | A 960 px window, whole |

Save every capture as PNG, with no browser frame and no Jellyfin header.

### Screenshot Kit

The kit in `tools/screenshots/` captures every screenshot in `docs/screenshots/` from the repository's own files, with no Jellyfin server. Its [README](../tools/screenshots/README.md) lists its files, what it needs, and how to set each path.

| File | Does |
| :--- | :--- |
| `server.py` | Serves the [harness](testing.md#harness), the tab HTML and JS from the repository, Base's assets from the NuGet package at the csproj's version, and jellyfin-web. It also serves `/pages/{slug}`, answering the shell's content fetch as a signed in user |
| `harness.html`, `upgrade.js`, `stub.js` | Load a tab under Jellyfin's real stylesheets, upgrade its `emby-*` elements, and answer its API calls with generic pages, stores, users, and images |
| `public_pages.py` | Renders the served pages from the repository's templates and Base's status template, with sample content |
| `shoot.mjs` | Captures every tab and served page into `docs/screenshots/`, or only the files it is given by name |

Run it from the repository root.

```bash
python3 -I tools/screenshots/server.py 8811 &
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node tools/screenshots/shoot.mjs
```

`node tools/screenshots/shoot.mjs "Page Editor" Stores` retakes only the named files. Each shot prints any page error. Open every PNG it writes and look at it before committing it. A shot that matches the committed one byte for byte needs no commit.

### Names and Placement

* Save to `docs/screenshots/`.
* Name the file in Title Case after what it shows, with spaces. `Page Editor.png`, `Not Authorized.png`.
* Link with `%20` for spaces and alt text that names the subject. `![Page editor](screenshots/Page%20Editor.png)`.
* Place two related shots side by side in a table whose header row holds the captions.

### When to Retake

Retake a screenshot in the same commit as any change that alters what it shows, including a label, a tab, a layout, or a count card's color. A new Base version changes how every component looks, so retake every screenshot when it is raised. A screenshot that shows a tab or label the plugin does not have is a bug.

## Installing a Build by Hand

```bash
./build.sh
```

The zip lands in `dist/` as `jellyfin-plugin-custompages-<version>.zip`. Unzip it into `<config>/plugins/Custom Pages_<version>/` on any Jellyfin 12.4 server and restart the server.

## Building the Docs

```bash
pip install mkdocs==1.6.1 mkdocs-material==9.7.7
```

```bash
mkdocs serve -a 127.0.0.1:8765
```

```bash
mkdocs build --strict
```

Run the strict build before every docs commit. The **Publish Docs** workflow builds the site from `master` and publishes it to GitHub Pages.

## Release Notes

Release notes live in the `changelog` of `build.yaml`, under four keys. The publish workflow copies them into `manifest.json`, so every line reaches users' plugin catalogs.

| Key | Holds | Voice |
| :--- | :--- | :--- |
| `added` | A feature that did not exist | The feature name, then what it does |
| `changed` | Behavior a user will notice | The new behavior, in present tense |
| `fixed` | A bug | What went wrong in past tense, then the fix in present tense |
| `developer` | Routes, schema, tests, and tooling | Plain statements for other developers |

```yaml
changelog:
  changed:
    - The Stores tab shows a Records bar of how many records a saved store holds out of its Record limit
  fixed:
    - The arrows on collapsible sections are back to their usual size
```

* This is the one place that may say "now", "no longer", and "was".
* List only this release. Drop a line that shipped in the last release, and a line a later change in this release replaced.
* Write full sentences with no closing period.
* Write one entry per change a user would notice. Fold internal steps into the change they serve.
* Name UI labels exactly as the page shows them, without bold or backticks.
* Name the Base version in `developer`, with what it replaced locally.
* Give a new or changed route or query parameter a `developer` line, and document it on the docs page for its feature in the same commit.

## Commit Messages

* Write an imperative sentence in sentence case with no closing period. "Sum store activity entries per outcome and give them one shape".
* Describe the behavior, not the files. Use no prefixes such as `fix:` or `feat:`.
* Add a body of plain paragraphs, wrapped at 72 characters, when the reason is not obvious.
* Commit docs and screenshots with the change they describe.

## Checklist

* [ ] Nothing describes an earlier version, a removed feature, or a fixed bug.
* [ ] Every UI label matches the page exactly and is bold.
* [ ] Every heading is Title Case.
* [ ] No em dashes, semicolons, or contractions.
* [ ] Every screenshot matches the current UI, came from the screenshot kit, and was looked at.
* [ ] [Security](../docs/Security.md) was reviewed and matches the code.
* [ ] Every term matches [glossary.md](glossary.md).
* [ ] `mkdocs build --strict` passes.
* [ ] `build.yaml` has a changelog entry for any change a user would notice.
