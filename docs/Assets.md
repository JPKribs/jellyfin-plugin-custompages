# Images and Assets

Upload images on the **Assets** tab. Each image is stored as `Base64-encoded` in the plugin configuration. Reference one from your page's HTML or CSS using the relative path **`asset/{name}`**. For example:

```
<img src="asset/logo.png">
```

```
background: url('asset/logo.png')
```

Because your page renders at `/pages/{slug}`, that relative path resolves to `/pages/asset/{name}` automatically, regardless of the base Jellyfin URL or subfolder.

## Asset Visibility

Like pages, every asset has a visibility tier:

* **Anyone**: Served publicly at `/pages/asset/{name}`. Anyone who can reach your server can fetch the image, even signed out and even if no page references it.
* **User / Admin**: Never served by URL. Browsers fetch images without your Jellyfin token, so a gated asset cannot be delivered to an `<img>` tag directly. Instead, when a page of an equal or higher tier renders, its `asset/{name}` references are replaced with inline `data:` URIs, so the image bytes only ever travel inside a response the viewer was already authorized to receive. A lower tier page referencing a gated asset shows a broken image rather than leaking it.

## Referencing a Gated Asset

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
3. Only image content types are embedded. A gated non image asset is never delivered anywhere.

## Delivery and Caching

Anonymous assets are served with `nosniff` and a sandbox `content-security-policy`, and any non image upload is delivered as a download rather than rendered so size your images accordingly. Custom pages reuse your server's real favicon, which the plugin exposes at `/pages/favicon.ico` so if you override your original favicon this should cascade to your custom pages.

!!! warning "Public assets are cached"
    Public assets are cached by browsers and by any shared cache sitting in front of your server for up to five minutes. Raising an asset's tier from **Anyone** to a gated tier does not retract copies that were already handed out, so if an image must become unreachable immediately, delete it rather than re-tiering it.
