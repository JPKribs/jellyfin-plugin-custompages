# ![Custom Pages](Jellyfin.Plugin.CustomPages/Assets/Logo.png)

**A simple Jellyfin plugin that publishes custom pages on your server at `/pages/{slug}`. These pages utilize Jellyfin's authorization to optionally gate access to only users or administrators.**

## How It Works

### Authoring a page

Each page has a slug, title, visibility tier, and its content. Content can be written two ways, chosen with the **Single file** toggle:

* **Separate files** — Write the HTML body, CSS, and JavaScript independently. On serve they are merged into one document and presented to the user.
* **Single file** — Provide one complete HTML document that is served exactly as written. CSS and JS can be included in their respective elements.

Pages live in the plugin's configuration, so they are captured by your normal Jellyfin config backups.

### How pages are served

A page is reachable at `/pages/{slug}`, handled by `CustomPagesController`. When the URL is requested, the plugin builds your page document and embeds it inside a tiny host page as a **sandboxed `<iframe>`** via the `srcdoc` attribute. The iframe is sandboxed to an opaque origin (no `allow-same-origin`), so your page can run scripts, submit forms, and open links in new tabs, but **cannot** reach Jellyfin, its access token, cookies, or storage. This attempts to keep a custom page isolated from your Jellyfin session. I expand on this more in the [Security](#security) section.

Pages may be framed by the Jellyfin origin itself, so you can embed one into another custom page or into a dashboard that lives on the same server. Third-party sites cannot frame them.

### Allow system access

Each page has an **Allow system access** toggle, off by default. Leave it off and the page keeps the isolation described above. Turn it on and the plugin serves the page without the `sandbox` attribute, so the page runs on your Jellyfin origin and its scripts can call the Jellyfin API, read the viewer's session, and reach anything else the web client can reach on that origin. That is what makes a page useful as a small control panel or a dashboard that talks to the server.

Understand the trade before you use it. An unsandboxed page has the same reach as the Jellyfin web client itself, so any script it runs can act as whoever is viewing it. A public or user tier page that pulls in a third party script hands that script the viewer's session. Only turn this on for pages whose source you wrote and control, and prefer to pin any external library you do use to a copy you host yourself.

The toggle drops the whole `sandbox` attribute rather than adding `allow-same-origin` to it. A frame that holds `allow-same-origin` and `allow-scripts` at once can reach into the wrapper and strip its own sandbox anyway, so a partial sandbox would only look like a boundary without being one.

Everything else about serving is unchanged. Your content is still HTML encoded into the wrapper's `srcdoc` attribute, still gated by the same visibility tier, and still served under the same Content-Security-Policy.

### Allow local resource calls

A page can be given named **routes** the server calls on its behalf. Turn on **Allow local resource
calls**, add a route with a name and an absolute target URL, and optionally a username and password.

Inside the page, the plugin injects a helper:

```js
serverFetch('name', { method: 'POST', headers: { ... }, body: '...' })
```

That reaches `/pages/{slug}/api/{name}` on the Jellyfin origin, and the server forwards it to the
route's target, adding the stored credentials. It returns a normal `fetch` promise, so the target's
status, body, and response headers all come back to your script unchanged.

This exists because a browser usually cannot call another machine on your network directly. That call
is cross origin, most services send no CORS headers, and on an HTTPS page a plain HTTP call is blocked
as mixed content. Routing through the server sidesteps all three, with no reverse proxy to configure.

Requirements and behaviour:

* Routes live under **Allow system access**, which is the single permission a page needs. The helper
  reads the viewer's token from same origin storage and calls the route on the Jellyfin origin, and
  neither works from the sandbox's opaque origin, so routes and the unsandboxed page are one decision.
  A page may turn that toggle on and define no routes at all, which simply gives its scripts same
  origin access to Jellyfin's own API.
* A route reaches exactly the audience of its page. The tier is enforced first, then the page's allowed
  user list, and only then is anything forwarded.
* The target URL is fixed in configuration and never comes from the caller, so this is not an open
  proxy. A page can only reach what you wired up for it. Only absolute `http` and `https` targets are
  accepted.
* Your request headers are forwarded to the target, so a session or CSRF token a target hands back can
  be echoed on the next call. The viewer's Jellyfin credentials are stripped and never forwarded.
* Route passwords are encrypted at rest and never sent to a browser. The configuration page shows a
  placeholder and posts it back unchanged unless you type a replacement.

The plugin has no knowledge of any particular service. What a route talks to, and what your page sends
it, is entirely yours.

### Storing data

A page can keep records on the server. Add a **store** on the **Stores** tab, give it a name, and any
page with **Allow system access** turned on can read and write it:

```js
pageStore.write('queue', { url: value })
  .then(function (record) { console.log(record.id); });

pageStore.read('queue')
  .then(function (records) { console.log(records); });
```

A store is a list of records. Everything except the payload is assigned by the server, so a page cannot
forge an identity, a timestamp, or an owner on something it writes:

```json
{
  "id": "4752bb755ed34da79b118484d480288c",
  "createdUtc": "2026-09-07T04:03:33.6360120Z",
  "updatedUtc": "2026-09-07T04:03:44.3140730Z",
  "userId": "8f2c...",
  "data": { "url": "https://example.com/a", "status": "downloaded" }
}
```

The shape of `data` is entirely yours. The plugin never looks inside it.

#### Who can read and who can write

A store belongs to the plugin rather than to one page, so a page that collects submissions and a page
that reports on them can work against the same records. Access is decided by the store's own settings
and never by which page is calling, which is what keeps one page from quietly widening another's
audience.

* **Read** is the tier required to read the store at all. It is definitive. A viewer below it reads
  nothing, and no other setting can let them back in.
* **Scope** decides how much of the store a viewer who cleared that tier is shown. **All records** shows
  everything. **User records** narrows each viewer to the rows they created, which is what turns a store
  into a submission queue where people watch their own entry and nobody else's.
* **Write** is the tier that may add a record. It is independent of Read, so a write only drop box, one
  people submit to and nobody reads back, is a valid store.
* **Writers can edit and delete their own records** is off by default, so a record carrying a status an
  administrator wrote cannot be rewritten by the person who submitted it.

**Administrators are shown every record whatever Scope says**, the same way they are always admitted to
a page restricted to specific users. That is what lets one store serve both halves of a workflow, and an
administrator can read the store's file off disk regardless, so narrowing them here would only look like
a restriction. Scope is therefore hidden on a store whose read tier is already **Admin**.

Changing a record needs both tiers. Write is what admits a caller to the store, and Read is what decides
which records are theirs to touch, so a caller can never rewrite something the store would refuse to
show them.

An anonymous caller owns nothing, so **User records** shows one an empty list rather than everything. If
a store is open to anonymous writes, understand what that means: anybody who can reach your server can
add records to it without signing in, and the record limit is the only thing bounding what they add.

#### A worked example

To collect URLs from your users and track what happened to each one, make a store with **Read** set to
**User**, **Scope** set to **User records**, and **Write** set to **User**.

A **User** tier page submits and shows the submitter their own queue:

```js
pageStore.write('queue', { url: input.value, status: 'queued' }, { label: input.value });
pageStore.read('queue').then(render);
```

An **Admin** tier page sees every submission and writes the outcome back onto one:

```js
pageStore.read('queue').then(render);
pageStore.write('queue', { url: row.data.url, status: 'done' }, { id: row.id });
```

The submitter watches their own row change status without ever seeing anyone else's, and cannot set
the status themselves. The admin page needs no separate store, because Scope stops narrowing at the
administrator. Whatever actually does the work can be a
[server side route](#allow-local-resource-calls) the admin page calls, or something outside Jellyfin
holding an API key.

#### The helper

`pageStore` is injected into any page running with **Allow system access**. It is a convenience and
never a grant. Every call it makes is authorized on the server against the store's own tiers, so a page
holding the helper reaches exactly the stores its viewer was already entitled to.

| Call | Does |
| --- | --- |
| `pageStore.read(name)` | Returns the records you may see, newest first. |
| `pageStore.read(name, id)` | Returns one record. |
| `pageStore.readAll(name)` | Returns the full response, including `count`, `limit`, and whether you were shown `all` records or only your `own`. |
| `pageStore.write(name, data)` | Creates a record and returns it. |
| `pageStore.write(name, data, id)` | Replaces that record's payload and returns it. |
| `pageStore.write(name, data, { id, label })` | Same, with the record's primary element. |
| `pageStore.remove(name, id)` | Deletes one record. |

Each returns a promise, and rejects with an `Error` carrying a `status` when the server refuses.

The same endpoints are reachable directly at `/pages/store/{name}/read`, `/write`, and `/delete` for
anything calling from outside a page. A Jellyfin API key is treated as the administrator tier with no
user identity, so a script or a background worker can pick work up and write results back, and can
never own a record or benefit from the own-record settings.

#### Retention

A store keeps records permanently by default. Set **Retention** and records are removed once they reach
that age, measured from when a record was created rather than when it was last written, so a status
update from an administrator does not extend a submission's lifetime.

Retention is applied two ways so it always holds. Reading or writing a store drops its expired records
first, which means a read can never return something the policy has already expired. A scheduled task,
**Apply Custom Pages store retention**, runs every six hours for the stores nobody is touching, which
would otherwise keep their last records forever whatever retention said. Expiring records also frees
room against the record limit.

#### Notifying administrators

Turn on **Notify administrators when records are added or removed** and both write entries to
Jellyfin's activity log, where the dashboard already surfaces plugin events.

Pass a **label** when you write and the entry names the record instead of counting it:

```js
pageStore.write('queue', { url: value, status: 'queued' }, { label: 'Holiday photos' });
```

That reads as `Holiday photos was added to queue`, and the same label comes back as
`Holiday photos was removed from queue` when the record is deleted, so the page only supplies it
once. An update carrying no label keeps the one the record already has, since writing a status onto a
record is not renaming it. Without a label the entry falls back to `A record was added to queue`.

Entries are rate limited to one a minute per store, and additions and removals share that window, so
what an administrator gets is one line saying what happened rather than two racing each other. A batch
reports counts, because it has no single thing to name: `4 items added to queue`, `3 items removed
from queue`, or `4 items added and 3 items removed from queue`. The label is capped and
flattened before it reaches the feed, so a page cannot write a line break into it.

#### Limits and storage

Every store caps how many records it holds, and no single record's payload may exceed 64 KB. Writes
past the record limit are refused rather than silently dropping the oldest row. These caps apply to
every store whatever its tiers, because they are the only thing bounding a store that accepts
anonymous writes.

Records are kept as one JSON file per store under Jellyfin's data directory, at
`data/custompages/stores/{name}.json`. They are data rather than settings, so unlike pages and assets
they do **not** live in the plugin configuration and a configuration backup will not bring them back.
Removing a store from the dashboard leaves its file alone, so recreating a store with the same name
picks the old records back up. Use **Clear** when you actually want them gone.

If a store's file is ever unreadable, the store answers as empty and refuses every write rather than
overwriting whatever the file holds. Repair or remove the file and restart Jellyfin.

### Images and assets

Upload images on the **Assets** tab. Each image is stored as `Base64-encoded` in the plugin configuration. Reference one from your page's HTML or CSS using the relative path **`asset/{name}`**. For example:

```
<img src="asset/logo.png">
```

```
background: url('asset/logo.png')
```

Because your page renders at `/pages/{slug}`, that relative path resolves to `/pages/asset/{name}` automatically, regardless of the base Jellyfin URL or subfolder.

Like pages, every asset has a visibility tier:

* **Anyone** — Served publicly at `/pages/asset/{name}`. Anyone who can reach your server can fetch the image, even signed out and even if no page references it.
* **User / Admin** — Never served by URL. Browsers fetch images without your Jellyfin token, so a gated asset cannot be delivered to an `<img>` tag directly. Instead, when a page of an equal or higher tier renders, its `asset/{name}` references are replaced with inline `data:` URIs, so the image bytes only ever travel inside a response the viewer was already authorized to receive. A lower-tier page referencing a gated asset shows a broken image rather than leaking it.

#### Referencing a gated asset

You reference a gated asset exactly the same way as a public one. No special syntax is needed:

```
<img src="asset/floorplan.png">
```

```
background: url('asset/floorplan.png')
```

The plugin tells the two apart at render time. Public references are left as URLs and fetched from `/pages/asset/{name}`, while gated references are swapped for the embedded image data before the page is delivered. The only rules to remember:

1. The page's visibility tier must be equal to or higher than the asset's tier. A **Users** asset works on **Users** and **Administrators** pages, but appears broken on an **Anyone** page.
2. Requesting a gated asset's URL directly returns `404`, even when signed in. The image is only available inside its pages.
3. Only image content types are embedded. A gated non-image asset is never delivered anywhere.

Anonymous assets are served with `nosniff` and a sandbox `content-security-policy`, and any non-image upload is delivered as a download rather than rendered so size your images accordingly. Custom pages reuse your server's real favicon, which the plugin exposes at `/pages/favicon.ico` so if you override your original favicon this should cascade to your custom pages.

One caveat on public assets: they are cached by browsers and by any shared cache sitting in front of your server for up to five minutes. Raising an asset's tier from **Anyone** to a gated tier does not retract copies that were already handed out, so if an image must become unreachable immediately, delete it rather than re-tiering it.

### Visibility

Each page declares who may view it, enforced by Jellyfin's authorization policies:

* **Admin** — Administrators only.
* **User** — Any authenticated Jellyfin account.
* **Anyone** — Public. Reachable by typing `/pages/{slug}`, even while signed out.

Because Jellyfin authenticates with a token rather than a browser session, protected pages are delivered through a small authentication shell. Visiting `/pages/{slug}` creates a loader that re-fetches the content using your signed-in token, then renders it. Anonymous pages are served directly. If you open a protected page while signed out, you will be prompted to sign in. Opening a page with an underprivileged user will inform the user they are not authorized to view this page.

### Restricting a page to specific users

A **User** page defaults to **Access: All Users**, meaning every signed in account. Switch **Access**
to **Specific Users**, pick accounts, and the page is served to those accounts and nobody else.

The check runs on the content endpoint, after Jellyfin has authenticated the request and after the
tier check, and before the page body is composed. A viewer the list does not name gets a 403 and the
sign in shell shows them the not authorized card. They never receive the page's HTML, CSS, or
JavaScript, so anything the page's source contains is only ever transmitted to an account you picked.

Four details worth knowing:

* **Administrators always have access** and are not offered in the picker. They author these pages and
  can read any page's source from the dashboard regardless, so excluding one would be a restriction the
  dashboard could not actually keep.
* The list is only accepted on the **User** tier. Saving one against **Anyone** or **Admin** is refused,
  because it would be ignored at serve time while the dashboard implied the page was restricted.
* An empty list means all users at the tier. The dashboard will not let you save **Specific Users**
  with nobody picked, so the two states cannot be confused.
* An API key authenticates a caller without identifying a user, so a restricted page refuses API key
  requests. If a stored list is somehow unreadable, it admits nobody rather than everybody.

This narrows who receives a page. It does not hide the page's source from the people who do receive
it, since a browser has to be given the source to render it. Anything in a page is readable by every
account you grant access to.

## Security

**I personally advise only exposing these pages to known parties via local networks or VPNs to minimize your footprint for malicious actors.** Pages are handled with several protections:

* **Administrators only** - Pages can be authored only by administrators.
* **Sandboxed rendering.** Page content runs inside a `sandbox`ed iframe with an opaque origin (no `allow-same-origin`). Author scripts therefore **cannot** read the Jellyfin origin's access token, cookies, or local storage, and cannot call the Jellyfin API as the viewer. This sandbox, not the Content-Security-Policy, is the boundary that protects your session. A page that opts in to [Allow system access](#allow-system-access) gives that boundary up on purpose, which is why the toggle is off by default and per page.
* **Authorization on every request.** The `/user` and `/admin` content endpoints are gated by Jellyfin's own policies. The shell's choice of endpoint cannot bypass them and each endpoint also verifies the page's declared tier, then applies the page's [per-user allow list](#restricting-a-page-to-specific-users) before rendering anything.
* **Asset tiers.** Only assets marked **Anyone** are reachable at `/pages/asset/{name}`. Gated assets are never URL-addressable and are embedded only into pages of an equal or higher visibility tier, so their bytes travel exclusively inside authorized responses.
* **Server side routes.** A page's [named routes](#allow-local-resource-calls) forward only to targets fixed in configuration, gated to that page's audience before anything is sent, with the viewer's Jellyfin credentials stripped from the forwarded request and route passwords encrypted at rest.
* **Store tiers.** A [store](#storing-data) is gated by its own read tier, read scope, and write tier on every call, resolved server side from the caller's credentials rather than from the page that called. The read tier is definitive, changing a record needs both tiers, and record and payload caps bound every store. A store open to anonymous writes is opt in and marked as such in the dashboard.
* **Hardening headers.** Served pages set `Content-Security-Policy`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, and `X-Robots-Tag: noindex`. Slugs are restricted to `[a-z0-9_-]`.
* **Popups escape the sandbox.** `allow-popups-to-escape-sandbox` is set so that a link to an external site opens as a normal page instead of a crippled sandboxed one. The trade-off is that author JavaScript can open and drive an unsandboxed window, which is the widest hole in the sandbox.

### What a page is allowed to do

Every page is served under a **single Content-Security-Policy**, byte-identical for public and protected pages. This is deliberate: a `srcdoc` iframe inherits the policy of the document that frames it, so one policy necessarily governs both the plugin's wrapper and your content. Two policies would mean the same page behaved differently depending on its visibility tier.

Your page **may**:

* load images, fonts, stylesheets, scripts, and media from your own server or any external host
* run inline scripts and styles, including libraries that rely on `eval`
* submit forms, and open links or popups in new tabs
* embed other pages in an iframe

Your page **may not**:

* use `<object>` or `<embed>` plugin content
* set a `<base>` tag — this is what keeps `asset/{name}` resolving to `/pages/asset/{name}`
* be framed by a third-party site, though the Jellyfin origin itself may frame it

Tightening the resource directives further would buy very little in practice. Content that escaped the sandbox could exfiltrate simply by navigating, which no CSP directive governs. The sandbox is the real boundary; the policy's job is to protect the document that frames it.

These steps alone cannot prevent all issues so HTTPS, TLS, and Reverse Proxies or VPNs are always recommended *if* you choose to expose this publicly. Page content is author-supplied and may load external resources (images, fonts, third-party scripts). **Only publish content you trust!**

---

## Versioning

Releases use a four-part version, `JJ.JJ.F.B`, that matches the supported Jellyfin version with the plugin's own feature/bug count:

```
12.0.1.0
└──┘ └┬┘
 │    └── 1 = Plugin feature release
 │        0 = Plugin bug/patch release within that feature
 │
 └─── 12.0 = Jellyfin version this build was tested/released for
```

Targets **Jellyfin 12.0.x** (`net10.0`, ABI `12.0.0.0`).

## Installation

### Step 1: Add Plugin Repository

* Open Jellyfin and navigate to Dashboard → Plugins → Repositories
* Click Add Repository
* Enter the following repository URL: `https://raw.githubusercontent.com/JPKribs/jellyfin-plugin-custompages/master/manifest.json`
* Click Save

### Step 2: Install Plugin

* Go to the Catalog tab in the Plugins section
* Find Custom Pages in the catalog
* Click Install
* Wait for installation to complete

### Step 3: Restart Jellyfin

* Restart your Jellyfin server completely
* Wait for Jellyfin to fully start up

### Verification Check

* After restart, navigate to Dashboard → Plugins → Custom Pages to confirm the configuration page loads, create a page, and open its URL.

---

## AI Disclaimer

Claude Code was utilized in the initial structure of this project and first drafts of documentation. All code has been manually reviewed, tested, and revised after its generation. This disclaimer exists in the interest of transparency.

**All code was written, or code reviewed and tested, by humans.**
