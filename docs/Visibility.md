# Visibility

Each page declares who may view it, enforced by Jellyfin's authorization policies:

* **Admin**: Administrators only.
* **User**: Any authenticated Jellyfin account.
* **Anyone**: Public. Reachable by typing `/pages/{slug}`, even while signed out.

Because Jellyfin authenticates with a token rather than a browser session, protected pages are delivered through a small authentication shell. Visiting `/pages/{slug}` creates a loader that re-fetches the content using your signed in token, then renders it. Anonymous pages are served directly. If you open a protected page while signed out, you will be prompted to sign in. Opening a page with an underprivileged user will inform the user they are not authorized to view this page.

A **User** page can be narrowed further to specific accounts. See [Restricting Access](RestrictingAccess.md).
