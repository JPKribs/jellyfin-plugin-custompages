# System Access

## Allow System Access

Each page has an **Allow system access** toggle, off by default. Leave it off and the page keeps the isolation described in [How Pages Are Served](Authoring.md#how-pages-are-served). Turn it on and the plugin serves the page without the `sandbox` attribute, so the page runs on your Jellyfin origin and its scripts can call the Jellyfin API, read the viewer's session, and reach anything else the web client can reach on that origin. That is what makes a page useful as a small control panel or a dashboard that talks to the server.

The toggle and its route list in the page editor:

![Allow system access](screenshots/System%20Access.png)

!!! warning "Understand the trade before you use it"
    An unsandboxed page has the same reach as the Jellyfin web client itself, so any script it runs can act as whoever is viewing it. A public or user tier page that pulls in a third party script hands that script the viewer's session. Only turn this on for pages whose source you wrote and control, and prefer to pin any external library you do use to a copy you host yourself.

The toggle drops the whole `sandbox` attribute rather than adding `allow-same-origin` to it. A frame that holds `allow-same-origin` and `allow-scripts` at once can reach into the wrapper and strip its own sandbox anyway, so a partial sandbox would only look like a boundary without being one.

Everything else about serving is unchanged. Your content is still HTML encoded into the wrapper's `srcdoc` attribute, still gated by the same visibility tier, and still served under the same Content-Security-Policy.

## Allow Local Resource Calls

A page can be given named **routes** the server calls on its behalf. Turn on **Allow local resource calls**, add a route with a name and an absolute target URL, and optionally a username and password.

Inside the page, the plugin injects a helper:

```js
serverFetch('name', { method: 'POST', headers: { ... }, body: '...' })
```

That reaches `/pages/{slug}/api/{name}` on the Jellyfin origin, and the server forwards it to the route's target, adding the stored credentials. It returns a normal `fetch` promise, so the target's status and body come back to your script unchanged, along with the response headers that describe the payload.

This exists because a browser usually cannot call another machine on your network directly. That call is cross origin, most services send no CORS headers, and on an HTTPS page a plain HTTP call is blocked as mixed content. Routing through the server sidesteps all three, with no reverse proxy to configure.

### Requirements and Behaviour

* Routes live under **Allow system access**, which is the single permission a page needs. The helper reads the viewer's token from same origin storage and calls the route on the Jellyfin origin, and neither works from the sandbox's opaque origin, so routes and the unsandboxed page are one decision. A page may turn that toggle on and define no routes at all, which simply gives its scripts same origin access to Jellyfin's own API.
* A route reaches exactly the audience of its page. The tier is enforced first, then the page's allowed user list, and only then is anything forwarded.
* The target URL is fixed in configuration and never comes from the caller, so this is not an open proxy. A page can only reach what you wired up for it. Only absolute `http` and `https` targets are accepted.
* The helper sends the viewer's token for this server only, matched by server ID or by an address on this origin. A token the browser saved for another Jellyfin server is never sent.
* Your request headers are forwarded to the target, so a session or CSRF token a target hands back can be echoed on the next call. The viewer's Jellyfin credentials are stripped and never forwarded.
* Only some of the target's response headers come back: `ETag`, `Last-Modified`, `Content-Type`, `Content-Disposition`, `Content-Language`, `Content-Encoding`, `Content-Range`, `Accept-Ranges`, `Location`, `Retry-After`, `Link`, `Vary`, and custom `X-` headers such as a session token. The response is served from the Jellyfin origin, so anything that would act on that origin, such as `Set-Cookie`, CORS, `Content-Security-Policy`, `X-Frame-Options`, or caching headers, is dropped. Route responses are never cached, and a response a browser would render as a page (HTML, XHTML, XML, SVG, or no content type) carries `Content-Security-Policy: sandbox`, so it cannot run script on the Jellyfin origin if someone opens the route directly. Other types, such as a PDF, an image, or a download, are served without it so the browser can open them normally. Reading a route with `serverFetch` is unaffected.
* A request body may be up to 4 MB and a target's response up to 16 MB. A larger request is refused with `413`, and a larger response becomes `502`. A target that cannot be reached answers `502`, and one that takes longer than 30 seconds answers `504`.
* Route passwords are encrypted at rest and never sent to a browser. The configuration page shows a placeholder and posts it back unchanged unless you type a replacement. Renaming the page or the route keeps the password as long as the target stays on the same host. Changing a route's target to a different host, or giving a route the name of one you deleted, needs the password entered again, so a password is never sent somewhere it was not entered for. If the server cannot tell which saved password a renamed route had, the save is refused and the Pages tab says which route needs its password entered again, rather than quietly saving the route without one.

The plugin has no knowledge of any particular service. What a route talks to, and what your page sends it, is entirely yours.

A page with **Allow system access** can also read and write server side stores. See [Storing Data](StoringData.md).
