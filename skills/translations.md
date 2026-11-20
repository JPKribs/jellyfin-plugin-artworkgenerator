# Translations

How this plugin shows its text in the viewer's language. Base builds the strings in and checks them on every build. The full reference is Base's [Translations](https://jpkribs.github.io/jellyfin-plugin-base/Translations/) guide.

## Where the Strings Live

| Strings | Where |
| :--- | :--- |
| Jellyfin's own | `<JellyfinString Include="Key" />` items in `Jellyfin.Plugin.ArtworkGenerator/Jellyfin.Plugin.ArtworkGenerator.csproj` |
| This plugin's own | `Jellyfin.Plugin.ArtworkGenerator/Localization/en-us.json`, one file per language |
| The endpoint pages load them from | `GET /Plugins/ArtworkGenerator/Translations`, which answers with `BaseTranslations.Serve` as `{ language, strings, casing }` |
| Words kept as written | `Jellyfin.Plugin.ArtworkGenerator/Localization/casing.json` |
| A setting's label, help text, and choices | Keys on the model, in `[Display(Name = TranslationKeys.X, Description = TranslationKeys.Y)]` and an enum member's `[Description(TranslationKeys.X)]` |
| A style's description and its own wording | Keys on the generator in `Services/Posters/Types/` |

## How the Pages Get Them

* `ag_shared.js` loads the strings once with `loadTranslations('Plugins/ArtworkGenerator/Translations')` and exports the handle as `t`. A page imports `t` from `ag_shared.js`.
* `startArtworkPage` builds a page only once the strings have loaded, so `t` is set wherever a page reads it. A show that came first is given to the page once it is built.
* `createArtworkPage` runs `t.apply(view)` before the page binds, so marked markup is translated first. Until then the markup's own English shows.
* `SettingOptions`, `PosterStyles`, and every route that returns a sentence answer in the language `BaseTranslations.RequestLanguage(Request)` reads, from `language` in the query, the `X-Jellyfin-Plugin-Language` header, or `Accept-Language`. A page builds those addresses with `withLanguage`, so the server answers in the viewer's language.
* A color field keeps its color wording in `data-str` and its opacity wording in `data-str-opacity`, and the page swaps them with `t.tr`.

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
    * Script: `t.tr('Key')` or `t.format('Key', value)`, with `t` from `ag_shared.js`. Write the key as a literal, so `TranslationsTests` can check it.
    * C#: `BaseTranslations.Get(TranslationKeys.Key, language)` or `BaseTranslations.Format(...)`. A model or generator holds the key, and `SettingOptions` translates it.
    * A list of names in a sentence: `Intl.ListFormat` with `t.language`. Never join words with "or" or "and".
5. Build. Fix every `JPKT` error and warning.

## Casing

Every string is in sentence case, mirroring Jellyfin, such as "Allow media playback". Sentences, labels, headings, buttons, tabs, and options alike start with one capital.

* `Localization/casing.json` is a JSON array of the words and phrases this plugin writes with special casing, such as `"Edit Images"` and `"ARGB"`. Add a product name, format, or acronym there when a string starts to use it. Base's own list, such as `Jellyfin`, `TV`, and `URL`, is always included.
* A Jellyfin string keeps Jellyfin's wording. Add `Case="sentence"` or `Case="title"` to its `JellyfinString` only when it reads wrong beside its neighbors.
* Text built at runtime is cased with `ToSentenceCase` and `ToTitleCase` in C#, and `sentenceCase` and `titleCase` on a page, or `t.sentence(key)` and `t.title(key)`. Each keeps the words in the casing list.

## Add a Language

Add `Localization/<code>.json`, such as `de.json` or `pt-br.json`, with only keys `en-us.json` has. A missing key falls back to the language's base and then to US English. Jellyfin's strings come in every language Jellyfin has, with no file.

## Checklist

* [ ] No English in a page, script, or server message outside `Localization/en-us.json`.
* [ ] Jellyfin's string used wherever it has the words.
* [ ] The build has no `JPKT` error or warning, and `TranslationsTests` passes.
* [ ] Every label, heading, button, and option is in sentence case, with `casing.json` words as written.
* [ ] The pages show the strings, checked once in another language such as `de`.
