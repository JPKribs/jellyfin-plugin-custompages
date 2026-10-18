# Authoring

## Writing a Page

Each page has a slug, title, visibility tier, and its content. Content can be written two ways, chosen with the **Single file** toggle:

* **Separate files**: Write the HTML body, CSS, and JavaScript independently. On serve they are merged into one document and presented to the user.
* **Single file**: Provide one complete HTML document that is served exactly as written. CSS and JS can be included in their respective elements.

The page editor with the HTML pane of a separate files page open:

![Page editor](screenshots/Page%20Editor.png)

Pages live in the plugin's configuration, so they are captured by your normal Jellyfin config backups.

## How Pages Are Served

A page is reachable at `/pages/{slug}`, handled by `CustomPagesController`. When the URL is requested, the plugin builds your page document and embeds it inside a tiny host page as a **sandboxed `<iframe>`** via the `srcdoc` attribute. The iframe is sandboxed to an opaque origin (no `allow-same-origin`), so your page can run scripts, submit forms, and open links in new tabs, but **cannot** reach Jellyfin, its access token, cookies, or storage. This attempts to keep a custom page isolated from your Jellyfin session. I expand on this more in [Security](Security.md).

Pages may be framed by the Jellyfin origin itself, so you can embed one into another custom page or into a dashboard that lives on the same server. Third party sites cannot frame them.

A page that needs to talk to Jellyfin or to other services can opt out of the sandbox. See [System Access](SystemAccess.md).

Next: [Visibility](Visibility.md).
