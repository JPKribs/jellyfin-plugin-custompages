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

## Security

**I personally advise only exposing these pages to known parties via local networks or VPNs to minimize your footprint for malicious actors.** Pages are handled with several protections:

* **Administrators only** - Pages can be authored only by administrators.
* **Sandboxed rendering.** Page content runs inside a `sandbox`ed iframe with an opaque origin (no `allow-same-origin`). Author scripts therefore **cannot** read the Jellyfin origin's access token, cookies, or local storage, and cannot call the Jellyfin API as the viewer. This sandbox, not the Content-Security-Policy, is the boundary that protects your session.
* **Authorization on every request.** The `/user` and `/admin` content endpoints are gated by Jellyfin's own policies. The shell's choice of endpoint cannot bypass them and each endpoint also verifies the page's declared tier.
* **Asset tiers.** Only assets marked **Anyone** are reachable at `/pages/asset/{name}`. Gated assets are never URL-addressable and are embedded only into pages of an equal or higher visibility tier, so their bytes travel exclusively inside authorized responses.
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
