using System.Collections.Generic;

namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// A single user-authored page served at <c>/pages/{slug}</c>.
/// </summary>
public class CustomPage
{
    /// <summary>Gets or sets the URL slug the page is served at (the <c>abc</c> in <c>/pages/abc</c>).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Gets or sets the page title used for the document title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the authorization tier required to view the page.</summary>
    public PageVisibility Visibility { get; set; } = PageVisibility.Anonymous;

    /// <summary>Gets or sets the page body markup.</summary>
    public string Html { get; set; } = string.Empty;

    /// <summary>Gets or sets the page styles, inlined into a <c>&lt;style&gt;</c> element on serve.</summary>
    public string Css { get; set; } = string.Empty;

    /// <summary>Gets or sets the page script, inlined into a <c>&lt;script&gt;</c> element on serve.</summary>
    public string Js { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the page is a single self-contained document.
    /// When <c>true</c>, <see cref="Document"/> is served as-is and <see cref="Html"/>/<see cref="Css"/>/<see cref="Js"/> are ignored.
    /// </summary>
    public bool SingleFile { get; set; }

    /// <summary>Gets or sets the complete HTML document served when <see cref="SingleFile"/> is <c>true</c>.</summary>
    public string Document { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the page is served. Disabled pages return 404.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the user IDs allowed to view the page. An empty list means every viewer the
    /// page's <see cref="Visibility"/> tier already admits. A non-empty list narrows the tier to
    /// those users, and is only meaningful on a tier that requires authentication.
    /// </summary>
    public List<string> AllowedUserIds { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets a value indicating whether the page runs without the isolating sandbox. When
    /// <c>true</c> the page runs on the Jellyfin origin with the same reach as the web client, so it
    /// can call the server API and read the viewer's session.
    /// </summary>
    public bool Unsandboxed { get; set; }

    /// <summary>
    /// Gets or sets the named server side routes this page may call through <c>serverFetch</c>. Each is
    /// forwarded from <c>/pages/{slug}/api/{name}</c> to its configured target. Requires the page to run
    /// with <see cref="Unsandboxed"/> access, since the helper calls the route on the Jellyfin origin.
    /// </summary>
    public List<PageApiRoute> ApiRoutes { get; set; } = new List<PageApiRoute>();
}
