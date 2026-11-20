# Translations

How this plugin shows its text in the viewer's language. Base builds the strings in and checks them on every build. The full reference is Base's [Translations](https://jpkribs.github.io/jellyfin-plugin-base/Translations/) guide.

## Where the Strings Live

| Strings | Where |
| :--- | :--- |
| Jellyfin's own | `<JellyfinString Include="Key" />` items in `Jellyfin.Plugin.CustomPages/Jellyfin.Plugin.CustomPages.csproj` |
| This plugin's own | `Jellyfin.Plugin.CustomPages/Localization/en-us.json`, one file per language |
| The endpoint pages load them from | `GET /pages/plugin/translations`, which answers with `BaseTranslations.Serve` |

## Rules

* Every word a person reads comes from a key. No hard coded English in a page, a script, or a message the server shows.
* Use a Jellyfin string whenever Jellyfin has the words. Search the web client's `src/strings/en-us.json` and the server's `Localization/Core/en-US.json` before adding a key.
* Add a local key only when Jellyfin has no string for it. Put it in `en-us.json` with its US English.
* One string, one key. The build fails on a key said twice or English said twice.
* Keep placeholders as `{0}`, `{1}`. Never build a sentence from pieces, since word order differs by language.
* A key is PascalCase and says what the text is, such as `HeaderQueue`, `LabelApiKey`, `ButtonRetry`, `ToastSaved`, `ErrorServerUnreachable`.
* Log lines and exception text for admins in the log stay in English and need no key.

## Add a String

1. Search Jellyfin's strings for the words.
2. Found: add `<JellyfinString Include="Key" />` to the csproj.
3. Not found: add `"Key": "English"` to `Localization/en-us.json`.
4. Use it.
    * Markup: `data-str="Key"`, `data-str-title`, `data-str-label`, or `data-str-placeholder`, with the English as the element's own text.
    * Script: `t.tr('Key')` or `t.format('Key', value)` on the handle from `loadTranslations('pages/plugin/translations')`.
    * C#: `BaseTranslations.Get(TranslationKeys.Key, language)` or `BaseTranslations.Format(...)`. Text for everyone, such as an activity log entry, uses `BaseTranslations.ServerLanguage(config)`.
5. Build. Fix every `JPKT` error and warning.

## This Plugin

* The custom pages themselves are the author's content and are never translated. Only the plugin's tabs, the served page shell, and its server messages are.
* The served page shell has no Base script. `PageService` fills its text on the server, in the language `BaseTranslations.RequestLanguage` reads, which is the browser's `Accept-Language` for a visitor.
* The Pages tab calls `pages/config/check` through `Shared.apiRequest`, which sends the page's language in `X-Jellyfin-Plugin-Language`. The controller reads it with `BaseTranslations.RequestLanguage(Request)`, so a refused save's reason comes back in it.
* Each tab loads its strings in `bind` before it builds anything, and `load` waits for them. Script built text calls `t.tr('Key')` with no fallback.
* A sentence that holds code, such as `/pages/{slug}`, keeps its English in the markup and is filled by `fillWithCode`, with each `{0}` drawn as code.
* Base's components translate their own default text, such as the Save Bar's lines and a confirm's **Cancel**. Pass a text option only when it says something different from Base's default, such as **Add route** or **Delete**.
* Each `ActivityOutcome` in `StoreActivity` is built from keys, such as `ActivityAddedOne` and `ActivityAddedMany`, so the names, such as "4 records added", and the overviews are in the server's display language.
* The screenshot harness serves `pages/plugin/translations` in US English as `{ language, strings, casing }`, built from Base's strings, the csproj, `Localization/en-us.json`, and `Localization/casing.json`.
* `TranslationKeyTests` checks that every key a tab uses has a string.

## Casing

* Every local label, heading, button, tab, column header, option, title, and toast is in sentence case, mirroring Jellyfin, such as "Allow system access". Sentences are already sentence case.
* `Localization/casing.json` is a JSON array of the words and phrases written as listed, such as `"Custom Pages"` and `"HTML"`. Add a product name, format, or acronym the plugin uses there, unless it is one of Base's defaults, such as `Jellyfin`, `URL`, or `API`.
* Jellyfin's strings stay as Jellyfin writes them. Add `Case="sentence"` or `Case="title"` to a `JellyfinString` only when that one reads wrong next to its neighbors.
* Text built at runtime is converted with `ToSentenceCase` or `ToTitleCase` in C#, and `sentenceCase` or `titleCase`, or `t.sentence` and `t.title`, on a page. Each keeps the casing list.
* When two local strings become the same English after recasing, merge them into one key. When a local string becomes a Jellyfin string's English, declare the Jellyfin string and use it.

## Add a Language

Add `Localization/<code>.json`, such as `de.json` or `pt-br.json`, with only keys `en-us.json` has. A missing key falls back to the language's base and then to US English. Jellyfin's strings come in every language Jellyfin has, with no file.

## Checklist

* [ ] No English in a page, script, or server message outside `Localization/en-us.json`.
* [ ] Jellyfin's string used wherever it has the words.
* [ ] The build has no `JPKT` error or warning.
* [ ] The pages show the strings, checked once in another language such as `de`.
