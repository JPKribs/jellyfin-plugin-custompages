# Custom Pages

![Custom Pages](assets/logo.png){ width="160" }

Custom Pages is a simple Jellyfin plugin that publishes custom pages on your server at `/pages/{slug}`. These pages utilize Jellyfin's authorization to optionally gate access to only users or administrators.

## How It Works

Each page has a slug, title, visibility tier, and its content. You write the content as separate HTML, CSS, and JavaScript, or as one complete HTML document. See [Authoring](Authoring.md).

A page is reachable at `/pages/{slug}`. The plugin embeds your page inside a sandboxed `<iframe>`, so it can run scripts, submit forms, and open links in new tabs, but cannot reach Jellyfin, its access token, cookies, or storage. See [Authoring](Authoring.md#how-pages-are-served) and [Security](Security.md).

Every page declares who may view it: administrators only, any signed in account, or anyone. A user tier page can be narrowed further to specific accounts. See [Visibility](Visibility.md) and [Restricting Access](RestrictingAccess.md).

A page you trust can opt out of the sandbox with **Allow system access**, which lets it call the Jellyfin API, reach services on your network through server side routes, and keep records in a store on the server. See [System Access](SystemAccess.md) and [Storing Data](StoringData.md).

Images are uploaded on the **Assets** tab and referenced from a page with a relative `asset/{name}` path. See [Images and Assets](Assets.md).

Pages and assets live in the plugin's configuration, so they are captured by your normal Jellyfin config backups. Store records do not. See [Storing Data](StoringData.md#limits-and-storage).

!!! tip
    I personally advise only exposing these pages to known parties via local networks or VPNs to minimize your footprint for malicious actors. **Only publish content you trust!**

Next: [Installation](Installation.md).
