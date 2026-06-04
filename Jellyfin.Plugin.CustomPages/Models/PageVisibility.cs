namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// The authorization tier required to view a custom page.
/// </summary>
public enum PageVisibility
{
    /// <summary>Anyone can view the page, including signed-out visitors.</summary>
    Anonymous,

    /// <summary>Any signed-in Jellyfin user can view the page.</summary>
    User,

    /// <summary>Only administrators can view the page.</summary>
    Admin
}
