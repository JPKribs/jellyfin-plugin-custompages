namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// A named server side route for one page. When the page calls <c>serverFetch('name', ...)</c> the
/// request reaches <c>/pages/{slug}/api/{name}</c>, and the server forwards it to <see cref="Url"/>,
/// adding the configured credentials. The target is fixed here in configuration and is never supplied
/// by the caller, so a page can only reach the resources an administrator wired up for it.
/// </summary>
public class PageApiRoute
{
    /// <summary>Gets or sets the route name, used as the last path segment and the <c>serverFetch</c> argument.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the absolute server side target URL the request is forwarded to.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional HTTP Basic auth username applied to the forwarded request.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional HTTP Basic auth password applied to the forwarded request.</summary>
    public string Password { get; set; } = string.Empty;
}
