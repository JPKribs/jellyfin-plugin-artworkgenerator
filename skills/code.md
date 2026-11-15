# Code

How to write, comment, and test code in this plugin. It covers C#, JavaScript, HTML, CSS, Python, shell, and YAML.

## Base First

[JPKribs.Jellyfin.Base](https://github.com/JPKribs/jellyfin-plugin-base) holds everything two plugins could share. Its README lists every component and helper. Read it before you write anything new.

### Use What Base Has

* Use the Base type or component when one fits. Never write a local version, a wrapper that renames it, or a CSS override of a `base-` class.
* When Base almost fits, change Base. Never patch around it locally.
* Delete a local copy as soon as Base offers the same thing. Do it in the commit that moves to that Base version.
* Never shadow a Base name. A local `FormatUtilities` beside Base's `FormatUtilities` is ambiguous.
* Read Base from the package this plugin builds against. The C# source is in `~/.nuget/packages/jpkribs.jellyfin.base/<version>/src/`, and the JS typings are in `assets/jpkribs_shared.d.ts` beside it.
* Treat the local Base checkout as read only. Base has its own owner and its own release, so report a gap and wait for the version that closes it.
* When Base differs from the local code it replaces, keep the local behavior in a small step around the Base call, and cover it with a test. `LogoDesignStore` reads its file itself, so a damaged file is told apart from a missing one, and writes it through `JsonFileStore`.

| Need | Use from Base |
| :--- | :--- |
| Plugin class and config lock | `PluginBase<TPlugin, TConfiguration>` and `MutateConfiguration` |
| Serving Base's bundle with the pages | `GetSharedPages("ag")` |
| JSON state file, such as `logos.json` | `JsonFileStore<T>` |
| Writing a file whole or not at all | `AtomicFile` |
| Paths inside a folder | `PathContainment` and `FileNameSanitizer` |
| Retry and backoff | `RetryPolicy`, `BackoffPolicy` |
| Parallel work | `ConcurrentTaskRunner` |
| Hashes and sizes | `HashUtilities`, `ByteSize` |
| The clock | `TimeProvider`, and `UtcTime` for stored times |
| Formatting and names | `FormatUtilities`, `ItemTitles`, `FileNameSanitizer` |
| Config page lifecycle | `createConfigPage`, through `createArtworkPage` in `ag_shared.js` |
| Page helpers | `createShared`, the `shared` bag on the page handle |
| Unsaved changes | `shared.createChangeTracker` and `page.guardUnsaved` |
| Saving and field checks | `createSaveBar` and `validateAll`, through `createArtworkSaveBar` in `ag_shared.js` |
| Showing and hiding fields | `shared.bindReveal` for a checkbox, `shared.setVisible` for any other rule, and `disableHiddenFields` in `ag_shared.js` so validation skips them |
| Names and other values from a dialog | `promptDialog`, through `promptName` in `ag_shared.js` |
| Generated images beside their fields | `createPreviewFrame` |
| Picking library items | `createMediaPicker` |
| Sample images and other media | `createMediaCard` in a `createList` |
| A grid of choices by kind of item | `createMatrixGrid`, one per `createSubTabs` tab |
| Dialogs | `openDialog`, `confirmDialog`, `promptDialog`, `openEditorDialog` |
| Lists and row editors | `createRepeater`, `createSortableCardList` |
| Tables | `createPaginatedTable`, `createMatrixGrid` |
| Color fields | `createColorPicker` |

### Move Code Into Base

Code belongs in Base when another plugin could use it unchanged. A helper with no Artwork Generator types in its signature is a candidate.

1. Add it to `../Shared/jellyfin-plugin-base/src/`, one file per component, following Base's [code guide](https://github.com/JPKribs/jellyfin-plugin-base/blob/main/skills/code.md).
2. Add tests in `JPKribs.Jellyfin.Base.Tests/`, and JS tests in `JPKribs.Jellyfin.Base.Tests/js/`.
3. Document it in Base's docs site inventory.
4. Pack it locally with `scripts/pack.sh` and build this plugin against it with `-p:RestoreAdditionalProjectSources=<base>/nupkg`.
5. Commit and push to Base, then run Base's **Release** workflow.
6. Raise the `JPKribs.Jellyfin.Base` version in this plugin's csproj, delete the local copy, and add a `developer` changelog line naming the Base version.

Never commit this plugin against an unreleased Base package.

## Comments

### The Rules

* Every class, method, function, and component gets a header comment.
* The header is the name, then a summary of one or two sentences, leaning to one.
* List inputs and outputs when there are any. One line each, as `name [type]: What or why`.
* Write no inline comments, with one exception. A branch whose reason is not obvious gets one short line above it.
* A branch comment says why, not what. Keep it under 15 words.
* Never comment what the code already says. `// Check if values differ` is noise.
* Never leave commented out code, TODO, FIXME, or HACK. Open an issue instead.
* Never describe history. "Used to", "now", and "was changed to" belong in commit messages.
* Use no em dashes or semicolons in comments.

```text
<Name>
<What it does, in one sentence.>

Inputs
name [type]: What it is or why it is needed.

Output
[type]: What comes back.
```

Leave out **Inputs** when there are none, and **Output** when nothing comes back.

### C#

C# uses XML docs so the compiler and IntelliSense read them. The member name is the title. The type comes from the signature, so a `param` gives only the what or why.

```csharp
/// <summary>
/// Reads the stored logo designs.
/// </summary>
/// <param name="logos">The stored designs, or empty when there are none or the file cannot be read.</param>
/// <returns><c>false</c> when the file exists but cannot be read, so a damaged file is left alone.</returns>
public bool TryLoad(out IReadOnlyList<LogoConfiguration> logos)
{
    logos = Array.Empty<LogoConfiguration>();
    if (_path == null)
    {
        logos = _memory.ToList();
        return true;
    }

    // A missing file is a first run, which may be seeded with a fresh design.
    if (!File.Exists(_path))
    {
        return true;
    }

    ...
}
```

* Every type and every member that is not private gets `<summary>`, every `<param>`, and `<returns>` when it returns a value.
* Private members get a `<summary>` when the name does not say enough.
* Use `<inheritdoc/>` on an interface or override member.
* Write `true` and `false` as `<c>true</c>` and `<c>false</c>`.
* A constructor summary is "Initializes a new instance of the <see cref="Name"/> class."
* Skip tautologies. `<param name="item">The item.</param>` says nothing, so write what the item is for.
* Put one type in each file, named after the type. A small enum used by one class may share its file.
* Match the namespace to the folder.

### JavaScript

```js
// createArtworkPage
// Wires a page through Base's createConfigPage with this plugin's id, route prefix, and tabs.
//
// Inputs
// view [HTMLElement]: The page element.
// tabIndex [number]: The page's place in the tab list.
// opts [object]: The bind, load, and onHide callbacks createConfigPage takes.
//
// Output
// [object]: The page handle from createConfigPage.
export function createArtworkPage(view, tabIndex, opts) {
```

* Comment every top level function, every function assigned to `shared`, and every exported function.
* Do not comment the methods inside a returned handle. List them in the Output line instead.
* Open each file with a header naming the file and saying what it holds in one sentence. Use no `// ====` banners.
* Only `ag_shared.js` imports Base, by the relative path `./configurationpage?name=ag_jpkribs_shared.js`. It exports each Base builder a page uses, and every page imports from `./configurationpage?name=ag_shared.js`.
* A new page script is an embedded resource in the csproj, a page in `Plugin.GetPages`, and a file `build.sh` checks for.

### HTML

```html
<!-- Frame Extraction -->
<!-- How frames are taken from a video and cleaned up. -->
<div class="verticalSection">
```

* One comment line naming each section or dialog where it starts, and an optional second line saying what it is for.
* Use no inline `style` attributes.

### CSS

```css
/* ag-preview-pair */
/* The landscape and portrait Preview Frames side by side, each sized to its shape. */
/* Example: <div class="ag-preview-pair"><div class="ag-preview-shape"></div></div> */
.ag-preview-pair { ... }
```

* Three lines above each component, the name, what it looks like, and one example.
* Prefix every class with `ag-` in kebab case. `base-` is reserved for Base.
* Use Base tokens such as `--base-border` and `--base-fg` for colors. Never write a hex value for something Base already colors.

### Python, Shell, and YAML

* A Python module opens with a docstring that names it and lists its commands. Each function gets a one line docstring.
* A shell script opens with a comment saying what it does and how to run it.
* A YAML file opens with a comment saying what it is for. Comment a step only when its reason is not obvious.

## C# Conventions

* Target `net10.0` with `Nullable` on, `TreatWarningsAsErrors` on, and `AnalysisMode` set to `AllEnabledByDefault`.
* Keep `jellyfin.ruleset` identical across plugins. Raise a rule change in every plugin at once.
* Suppress a rule only at the narrowest scope, with a reason, such as `[SuppressMessage("Usage", "CA2227", Justification = "Setter required for JSON deserialization")]`.
* Use file scoped namespaces in new files, `sealed` classes, and records for data.
* Name private fields `_camelCase`.
* End every async method name in `Async`, pass the `CancellationToken` through, and use `ConfigureAwait(false)` on every await.
* Guard public arguments with `ArgumentNullException.ThrowIfNull`.
* Log through `ILogger<T>` with static, named templates. Pass untrusted text through `FormatUtilities.SanitizeForLog`.
* Catch a specific exception, or filter with `when`. Rethrow `OperationCanceledException` when the token was cancelled.
* Read the clock once in a method and reuse the value. Two reads can straddle a second and disagree.
* Take a `TimeProvider` wherever time decides an outcome, so a test can move it.
* Build the plugin's services in the `Plugin` constructor, and reach them through `Plugin.Instance`.

## JavaScript Conventions

* Each config page is an ES module whose default export calls `createArtworkPage`, which wraps Base's `createConfigPage` and holds the tab list.
* Keep one handle for each Base component a page draws, such as a Preview Frame. Call `destroy` when the view is destroyed.
* Track unsaved changes with one `createChangeTracker` per page, passed to `page.guardUnsaved` and to `createArtworkSaveBar`.
* Read the server's choices, defaults, labels, and help text from `Plugins/ArtworkGenerator/SettingOptions`, so a setting's wording lives on its model in C#.
* Keep a stored value the page does not offer selectable, marked unavailable, rather than rewriting it.
* Save with a read, change, and write of the configuration, so a page never overwrites what another tab edits.
* Use `const` and `let` in new code, never `var` in a new file. Arrow functions are fine for callbacks.
* Escape every value placed in HTML with `escapeHtml`, or set it with `textContent`.
* Report results with `shared.setStatus`, and errors with `shared.errorText`.
* Register every poller and observer with the page handle so `viewhide` stops it.

## Security

* Every route on `ConfigurationController` needs `[Authorize(Policy = "RequiresElevation")]`, which the controller carries. `GET Generated/{token}` is the one anonymous route, since Jellyfin downloads a picked choice with no user. It only resolves an unguessable token that expires, and never renders.
* Never add an anonymous route, and never let the anonymous route render, read a path, or list anything.
* Load preview images through `shared.fetchObjectUrl` or `ApiClient`, so the admin's credentials go with them. A bare `img.src` to a plugin route sends none.
* Treat **Font Path** and **Graphic File Path** as paths an admin typed. Read them only to draw, and never return their contents or list their folder.
* Keep files the plugin writes inside its data folder, `artworkgenerator` under Jellyfin's data path, with `PathContainment` and `FileNameSanitizer`.
* Write `logos.json` through `JsonFileStore`, and copy a file that cannot be read to a `.bak` before anything replaces it.
* Pass every value from a request or an item through `FormatUtilities.SanitizeForLog` before logging it.
* Escape every value placed in HTML with `escapeHtml`.
* Never put a user's data in a URL, except an item id.

## Tests

Every source file has a test file. See [testing.md](testing.md) for each layer and how to write tests.

## Checklist

* [ ] Base's source and typings were searched, nothing here duplicates Base, and anything reusable went to Base first.
* [ ] Every class, method, function, and component has a header.
* [ ] Inline comments appear only above branches, and say why.
* [ ] Every changed source file has tests that pass, per [testing.md](testing.md).
* [ ] The [security](#security) rules hold.
* [ ] `dotnet build` shows no warnings.
