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
* **Signed-in users / Administrators** — Never served by URL. Browsers fetch images without your Jellyfin token, so a gated asset cannot be delivered to an `<img>` tag directly. Instead, when a page of an equal or higher tier renders, its `asset/{name}` references are replaced with inline `data:` URIs, so the image bytes only ever travel inside a response the viewer was already authorized to receive. A lower-tier page referencing a gated asset shows a broken image rather than leaking it.

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

* **Anyone** — Public. Reachable by typing `/pages/{slug}`, even while signed out.
* **Signed-in users** — Any authenticated Jellyfin account.
* **Administrators** — Administrators only.

Because Jellyfin authenticates with a token rather than a browser session, protected pages are delivered through a small authentication shell. Visiting `/pages/{slug}` creates a loader that re-fetches the content using your signed-in token, then renders it. Anonymous pages are served directly. If you open a protected page while signed out, you will be prompted to sign in. Opening a page with an underprivileged user will inform the user they are not authorized to view this page.

### Restricting a page to specific users

A gated page defaults to **All users**, meaning everyone its tier already admits. Switch **Who can
view** to **Only the users I pick** and choose accounts, and the page is served to those accounts
and nobody else.

The check runs on the content endpoint, after Jellyfin has authenticated the request and after the
tier check, and before the page body is composed. A viewer the list does not name gets a 403 and the
sign in shell shows them the not authorized card. They never receive the page's HTML, CSS, or
JavaScript, so anything the page's source contains is only ever transmitted to an account you picked.

Three details worth knowing:

* The list is only accepted on a tier that requires signing in. Saving one against an **Anyone** page
  is refused, because an allow list there would be ignored at serve time while the dashboard implied
  the page was restricted.
* An empty list means all users at the tier. The dashboard will not let you save **Only the users I
  pick** with nobody picked, so the two states cannot be confused.
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
10.11.1.0
└───┘ └┬┘
  │    └── 1 = Plugin feature release
  │        0 = Plugin bug/patch release within that feature
  │
  └─── 10.11 = Jellyfin version this build was tested/released for
```

Targets **Jellyfin 10.11.x** (`net9.0`, ABI `10.11.10.0`).

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
