# Security

**I personally advise only exposing these pages to known parties via local networks or VPNs to minimize your footprint for malicious actors.** Pages are handled with several protections:

* **Administrators only.** Pages can be authored only by administrators.
* **Sandboxed rendering.** Page content runs inside a `sandbox`ed iframe with an opaque origin (no `allow-same-origin`). Author scripts therefore **cannot** read the Jellyfin origin's access token, cookies, or local storage, and cannot call the Jellyfin API as the viewer. This sandbox, not the Content-Security-Policy, is the boundary that protects your session. A page that opts in to [Allow system access](SystemAccess.md#allow-system-access) gives that boundary up on purpose, which is why the toggle is off by default and per page.
* **Authorization on every request.** The `/user` and `/admin` content endpoints are gated by Jellyfin's own policies. The shell's choice of endpoint cannot bypass them and each endpoint also verifies the page's declared tier, then applies the page's [per user allow list](RestrictingAccess.md) before rendering anything.
* **Asset tiers.** Only assets marked **Anyone** are reachable at `/pages/asset/{name}`. Gated assets are never URL addressable and are embedded only into pages of an equal or higher visibility tier, so their bytes travel exclusively inside authorized responses.
* **Server side routes.** A page's [named routes](SystemAccess.md#allow-local-resource-calls) forward only to targets fixed in configuration, gated to that page's audience before anything is sent, with the viewer's Jellyfin credentials stripped from the forwarded request and route passwords encrypted at rest.
* **Store tiers.** A [store](StoringData.md) is gated by its own read tier, read scope, and write tier on every call, resolved server side from the caller's credentials rather than from the page that called. The read tier is definitive, changing a record needs both tiers, and record and payload caps bound every store. A store open to anonymous writes is opt in and marked as such in the dashboard.
* **Hardening headers.** Served pages set `Content-Security-Policy`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, and `X-Robots-Tag: noindex`. Slugs are restricted to `[a-z0-9_-]`.
* **Popups escape the sandbox.** `allow-popups-to-escape-sandbox` is set so that a link to an external site opens as a normal page instead of a crippled sandboxed one. The trade off is that author JavaScript can open and drive an unsandboxed window, which is the widest hole in the sandbox.

## What a Page Is Allowed to Do

Every page is served under a **single Content-Security-Policy**, byte identical for public and protected pages. This is deliberate. A `srcdoc` iframe inherits the policy of the document that frames it, so one policy necessarily governs both the plugin's wrapper and your content. Two policies would mean the same page behaved differently depending on its visibility tier.

Your page **may**:

* load images, fonts, stylesheets, scripts, and media from your own server or any external host
* run inline scripts and styles, including libraries that rely on `eval`
* submit forms, and open links or popups in new tabs
* embed other pages in an iframe

Your page **may not**:

* use `<object>` or `<embed>` plugin content
* set a `<base>` tag, which is what keeps `asset/{name}` resolving to `/pages/asset/{name}`
* be framed by a third party site, though the Jellyfin origin itself may frame it

Tightening the resource directives further would buy very little in practice. Content that escaped the sandbox could exfiltrate simply by navigating, which no CSP directive governs. The sandbox is the real boundary. The policy's job is to protect the document that frames it.

!!! warning "Only publish content you trust"
    These steps alone cannot prevent all issues so HTTPS, TLS, and Reverse Proxies or VPNs are always recommended *if* you choose to expose this publicly. Page content is author supplied and may load external resources (images, fonts, third party scripts). **Only publish content you trust!**
