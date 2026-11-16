# Screenshot Kit

Captures every screenshot in `docs/screenshots/` from the repository's own files and generic sample data, with no Jellyfin server. It is not part of the plugin build or its package.

| File | Does |
| :--- | :--- |
| `server.py` | Serves the harness, the tab HTML and JS from `Jellyfin.Plugin.CustomPages/Configuration`, Base's assets from the NuGet package, and jellyfin-web. It also serves `/pages/{slug}`, answering the shell's content fetch as a signed in user |
| `harness.html` | Loads jellyfin-web's stylesheets in load order, then mounts one tab. The server fills in the stylesheet links |
| `upgrade.js` | Adds the classes and markup Jellyfin's `emby-*` elements add on upgrade, so the real CSS applies |
| `stub.js` | A stand-in `ApiClient` with generic pages, stores, users, and images |
| `public_pages.py` | Renders the served pages from `Jellyfin.Plugin.CustomPages/Templates` and Base's status template, with sample content |
| `shoot.mjs` | Opens each subject, sets it up, and writes its PNG to `docs/screenshots/` |

## Requirements

* Python 3 and Node.js.
* playwright-core and a Chromium build for it.
* jellyfin-web, which the macOS Jellyfin app includes.
* Base restored into the NuGet package folder at the version the csproj names. A `dotnet restore` or `./build.sh` does this.

| Variable | Default | Points at |
| :--- | :--- | :--- |
| `PLAYWRIGHT_CORE` | `playwright-core` | playwright-core's `index.mjs` |
| `CHROMIUM` | `~/Library/Caches/ms-playwright/chromium-1148/chrome-mac/Chromium.app/Contents/MacOS/Chromium` | The Chromium binary |
| `JELLYFIN_WEB` | `/Applications/Jellyfin.app/Contents/Resources/jellyfin-web` | The jellyfin-web folder |
| `NUGET_PACKAGES` | `~/.nuget/packages` | The NuGet package folder |

## Running

Start the server, then take every shot.

```bash
python3 -I tools/screenshots/server.py 8811 &
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node tools/screenshots/shoot.mjs
```

Name files to retake only those.

```bash
node tools/screenshots/shoot.mjs "Page Editor" Stores
```

The shots are **Pages**, **Page Editor**, **Visibility**, **System Access**, **Restricted Page**, **Assets**, **Stores**, **House Rules Page**, **Requests Page**, and **Not Authorized**.

Each shot prints its size and any page error. Open every PNG it writes and look at it before committing it. A shot that matches the committed one byte for byte needs no commit. **Restricted Page** and **Stores** can draw a descender in a text field a pixel apart between runs. That difference alone needs no commit.

New sample data goes in `stub.js` for the tabs and in `public_pages.py` for the served pages. A new subject is one more entry in `tabs` or `served` in `shoot.mjs`.
