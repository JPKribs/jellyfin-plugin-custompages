namespace Jellyfin.Plugin.CustomPages.Models;

/// <summary>
/// Small derived values for <see cref="PageVisibility"/>, kept on the model so callers read intent
/// instead of repeating the comparisons.
/// </summary>
public static class PageVisibilityExtensions
{
    /// <summary>Returns whether a page at this visibility requires a signed in viewer.</summary>
    /// <param name="visibility">The page visibility.</param>
    /// <returns>True for User and Admin, false for Anonymous.</returns>
    public static bool RequiresAuth(this PageVisibility visibility) => visibility != PageVisibility.Anonymous;

    /// <summary>Returns the content endpoint tier this visibility is served through.</summary>
    /// <param name="visibility">The page visibility.</param>
    /// <returns><c>admin</c> for Admin, otherwise <c>user</c>.</returns>
    public static string EndpointTier(this PageVisibility visibility) => visibility == PageVisibility.Admin ? "admin" : "user";
}
