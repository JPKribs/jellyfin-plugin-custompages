# ![Custom Pages](Jellyfin.Plugin.CustomPages/Assets/Logo.png)

A simple Jellyfin plugin that publishes custom pages on your server at `/pages/{slug}`. These pages utilize Jellyfin's authorization to optionally gate access to only users or adminstrators.

---

**All plugins are made for my personal use cases. I've made these publicly available for anyone who has the same use cases and can benefit from this work. I have no desire to advertise or market for these plugins as these are for personal usage only.**

**Thank you,**

*Joe Kribs*

---

## How It Works

Custom Pages runs entirely inside Jellyfin using the same runtimes and resources as the server. When the plugin loads, Jellyfin discovers the plugin's API controller (`CustomPagesController`) and mounts its `/pages` routes onto the existing Jellyfin web API. Everything below is served from there.

### Authoring a page

Each page has a slug, title, visibility tier, and its content. Content can be written two ways, chosen with the **Single file** toggle:

* **Separate files** — Write the HTML body, CSS, and JavaScript independently. On serve they are merged into one document and presented to the user.
* **Single file** — Provide one complete HTML document that is served exactly as written. CSS and JS can be included in their respective elements.

Pages live in the plugin's configuration, so they are captured by your normal Jellyfin config backups.

### How pages are served

A page is reachable at `/pages/{slug}`, handled by `CustomPagesController`. When the URL is requested, the plugin builds your page document and embeds it inside a tiny host page as a **sandboxed `<iframe>`** via the `srcdoc` attribute. The iframe is sandboxed to an opaque origin (no `allow-same-origin`), so your page can run scripts, submit forms, and open links, but **cannot** reach the Jellyfin, its access token, cookies, or storage. This attempts to keep a custom page isolated from your Jellyfin session. I expand on this more in the [Security model](#security-model) section.

### Images and assets

Upload images on the **Assets** tab. Each image is stored (Base64-encoded) in the plugin configuration and served at `/pages/asset/{name}`. Reference one from your page's HTML or CSS using the relative path **`asset/{name}`**. For example:

`<img src="asset/logo.png">`
`background: url('asset/logo.png')`

Because your page renders at `/pages/{slug}`, that relative path resolves to `/pages/asset/{name}` automatically, regardless of the base Jellyfin URL or subfolder.

Assets are served with `nosniff` and a sandbox content-security-policy, and any non-image upload is delivered as a download rather than rendered. Custom pages reuse your server's real favicon, which the plugin exposes at `/pages/favicon.ico` so if you override your original favicon this should cascade to your custom pages.

### Visibility

Each page declares who may view it, enforced by Jellyfin's authorization policies:

* **Anyone** — Public. Reachable by typing `/pages/{slug}`, even while signed out.
* **Signed-in users** — Any authenticated Jellyfin account.
* **Administrators** — Administrators only.

Because Jellyfin authenticates with a token rather than a browser session, protected pages are delivered through a small auth shell: visiting `/pages/{slug}` loads a loader that re-fetches the content using your signed-in token, then renders it. Anonymous pages are served directly. If you open a protected page while signed out, you will be prompted to sign in.

## Security model

I am no security expert. While I personal advise only exposing these pages to known parties via local networks or VPNs, I have attempted to secure this as best as possible. 

Pages can only be authored only by administrators and are served with several protections:

* **Sandboxed rendering.** Page content runs inside a `sandbox`ed iframe with an opaque origin (no `allow-same-origin`). Author scripts therefore **cannot** read the Jellyfin origin's access token, cookies, or local storage, and cannot call the Jellyfin API as the viewer. They may run scripts, submit forms, and open links.
* **Authorization on every request.** The `/user` and `/admin` content endpoints are gated by Jellyfin's own policies; the shell's choice of endpoint cannot bypass them. Each endpoint also verifies the page's declared tier.
* **Hardening headers.** Served pages set `Content-Security-Policy`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `X-Robots-Tag: noindex`. Slugs are restricted to `[a-z0-9_-]`. 

These steps alone cannot prevent all issues so HTTPS, TLS, and Reverse Proxies or VPNs are always recommended. Page content is author-supplied and may load external resources (images, fonts, third-party scripts). **Only publish content you trust!**

*I am always interested in doing this better. Please feel free to reach out to me directly if you believe there are ways I can be doing this better and more securely!*

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
