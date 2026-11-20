# Glossary

The agreed name for every concept in this plugin. The UI, the docs, messages, and code use these words and no others.

* Use the **Term** in the UI, the docs, and messages.
* The **In Code** column names the type or member that holds it, when the code name differs.
* Never use a word from the **Not** column for the concept.
* Add a term here before it appears anywhere else. Rename it here first when it changes, then everywhere in the same commit.

## Pages

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Page | An HTML page an administrator writes, served at `/pages/{slug}` | `CustomPage` | Site, view, document |
| Slug | The last part of a page's address, limited to `a-z`, `0-9`, `-`, and `_` | `Slug` | Path, route, ID |
| Title | The text in the browser tab for a page | `Title` | Name, heading |
| Published | A page that is served. An unpublished page answers `404` | `Enabled` | Live, active, enabled |
| Source | The HTML, CSS, or JavaScript of a page, picked in the **Source** select | `Html`, `Css`, `Js` | Code, content |
| Use a single webpage file | Serves one complete HTML document as written, in place of separate HTML, CSS, and JavaScript | `SingleFile`, `Document` | Raw mode, full page |
| Separate files | A page written as HTML, CSS, and JavaScript that are merged when it is served | `SingleFile` off | Split mode, parts |

## Visibility

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Visibility | Who may view a page or an asset | `PageVisibility` | Permission, access level, audience |
| Tier | One of the three visibility values | `PageVisibility` | Level, role |
| Anyone | Every visitor, signed in or not | `PageVisibility.Anonymous` | Public, Anonymous, Everyone |
| User | Every signed in account | `PageVisibility.User` | Users, Member, Authenticated |
| Admin | Administrators only | `PageVisibility.Admin` | Administrators, Owner |
| Access | Whether a User page is open to **All users** or to **Specific users** | `AllowedUserIds` empty or not | Restriction, sharing |
| Allow list | The users a page with **Specific users** admits. Administrators are always admitted | `AllowedUserIds` | Whitelist, ACL |
| Shell | The status card that signs a viewer in and fetches a User or Admin page | `custompages_shell`, `GetShellHtml` | Loader, splash |

## System Access

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Sandbox | The iframe sandbox a page runs in, with an opaque origin | `sandbox` attribute | Jail, container |
| Allow system access | Serves a page without the sandbox, on the Jellyfin origin | `Unsandboxed` | Unsandboxed, trusted mode |
| Route | A named target URL the server calls for a page, with an optional username and password | `PageApiRoute`, `ApiRoutes` | Endpoint, proxy, API |
| Route proxy | The code that forwards a route call and filters its headers | `RouteProxy` | Gateway, relay |
| `serverFetch` | The page helper that calls a route by name | `InjectServerFetch` | Proxy fetch |
| Clear password | The button beside a stored route password that removes it on the next save | A `secret` Repeater field with `clearable: true` | Reset password, Delete password |

## Assets

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Asset | An image stored in the plugin configuration as Base64 | `PageAsset` | File, upload, attachment |
| Asset path | The relative path a page uses for an asset, `asset/{name}` | `IsValidAssetName` for the name | Asset URL, link |
| Gated asset | An asset set to User or Admin. It is never served by URL and is embedded only into pages of the same or a higher tier | `InlineGatedAssets` | Private asset, hidden asset |

## Stores

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Store | A named set of records that pages read and write | `PageStore` | Database, table, collection |
| Record | One entry in a store, with its data and who created it | `StoreRecord` | Row, item, entry |
| Owner | The user who created a record | `StoreRecord.UserId` | Author, submitter |
| Label | Text a write passes to name a record in the activity log | `StoreRecord.Label` | Title, name |
| Enabled | A store that answers calls. A disabled store answers `404` | `PageStore.Enabled` | Active, published |
| Read | The lowest tier that may read a store | `ReadAccess` | Read access, viewer tier |
| Scope | How much of a store a reader sees, **All records** or **User records** | `ReadScope`, `StoreScope.All`, `StoreScope.Own` | Filter, visibility |
| Write | The lowest tier that may add a record | `WriteAccess` | Write access, submitter tier |
| Writers edit own records | Lets a writer change and delete records they created when Scope is User records | `AllowEditOwn` | Edit own, self edit |
| Record limit | The most records a store holds | `MaxRecords` | Max records, cap, quota |
| Retention | The age at which records are removed, counted from when each was created | `RetentionDays` | Expiry, TTL, lifetime |
| Notify administrators | Writes an activity log entry when records are added or removed | `NotifyOnWrite`, `StoreActivity` | Alerts, notifications |
| Clear | Deletes every record in a store and keeps the store | `Clear`, `POST /pages/store/{name}/clear` | Empty, reset, purge |
| `pageStore` | The page helper that reads and writes stores | `InjectPageStore` | Store client |
| Store file | The JSON file that holds one store's records, at `data/custompages/stores/{name}.json` | `StoreState.File` | Database, data file |
| Records bar | The bar on the **Stores** tab of how many records a saved store holds out of its Record limit | `usageMeter`, `createUsageMeter` | Usage bar, quota bar |
| Open to writes | The count card of enabled stores whose Write is Anyone | `renderCounts` in `custompages_stores.js` | Public stores, open stores |

## Dashboard

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Count cards | The row of counts at the top of each tab, one color per card | `renderCards` | Stats, tiles, badges |
| Save Bar | The full width **Save** button with its status line, the last element of the editor it saves | `createSaveBar` | Save button, footer |
| Copy field | A read only field with a Copy button, for a page's or a store's **URL** and an asset's path | `createCopyField` | Link box, copy box |

## Housekeeping

| Term | Meaning | Not |
| :--- | :--- | :--- |
| Activity log | Jellyfin's dashboard activity log | Activity feed, log page |
| Server log | Jellyfin's log files | Debug log |
| Configuration check | The `POST /pages/config/check` call the **Pages** tab makes before it saves, so a refusal reaches the page with its reason | Validation, preflight |
| Apply Custom Pages store retention | The scheduled task that applies Retention to stores nobody reads or writes | Cleanup task, purge job |
