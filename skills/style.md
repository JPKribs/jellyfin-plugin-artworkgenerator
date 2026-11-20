# Style

How every page, dialog, label, and message in this plugin looks and reads. It is drawn from Base and from all nine JPKribs plugins, so the same rules carry to each of them.

Base is [JPKribs.Jellyfin.Base](https://github.com/JPKribs/jellyfin-plugin-base). Its README documents each component's options. This guide says which component to use, when, and what to write in it.

## Principles

1. **One component per concept.** When Base has it, use it. Look before you build, per [Find the Component](#find-the-component). When Base lacks it and a second plugin needs it, add it to Base. Never build a second version.
2. **Short beats complete.** A label is a few words. A description is one sentence. A message is one sentence.
3. **The page explains itself.** A reader understands a setting from its label and its one line description, without the docs.
4. **The same thing has the same name everywhere.** The label, the docs, the message, and the server log use one word for one concept.
5. **Follow Jellyfin.** Use Jellyfin's own markup and theme, so the plugin looks like part of the dashboard.

## Writing

### The Rules

These rules apply to every string a user can see. That includes HTML, JS, C# messages the API returns, and activity log entries.

* **No em dashes or en dashes as punctuation.** Split the sentence, or join it with a comma and "and". An en dash in a numeric range, such as `22–6`, is fine.
* **No semicolons.** Split the sentence.
* **Avoid colons.** Never end a label with one. In a sentence, rewrite it. "How fast files move: downloads and uploads." becomes "Sets how fast files move in each direction."
* **One sentence.** Two only when the second gives the fix or the consequence.
* **End every sentence with a period.** Labels, buttons, titles, tabs, and table cells are not sentences and take no period.
* **No "please", "successfully", "simply", or "just".**
* **No exclamation marks.**
* **No ALL CAPS for emphasis.** Say why it matters instead.
* **No "(s)".** Write the right plural for the count.
* **No contractions.** Write "cannot", not "can't".
* **No "e.g." or "i.e.".** Write "such as".
* **American spelling.** Color, behavior, favorite, center.
* **Use the reader's words.** "Design" and "film", not "poster configuration" and "movie item", unless the text is for developers.

### Casing

Every string a person reads is in sentence case, mirroring Jellyfin, such as "Allow media playback". Only the first word, proper nouns, and the words in `Localization/casing.json` start with a capital.

| Text | Example |
| :--- | :--- |
| Tab | Designs |
| Section and collapsible title | Frame extraction |
| Field label | Samples per frame |
| Checkbox label | Detect letterboxing |
| Button, verb first | Edit series |
| Dialog title | Remove design |
| Column header | Primary |
| Description, message, placeholder sentence | Crops black bars off an extracted frame. |

* Write the words in `Localization/casing.json` exactly as listed, such as "Edit Images" and "ARGB". Add a product name, format, or acronym there when the plugin starts to use it. See [translations.md](translations.md#casing).
* A Jellyfin string keeps Jellyfin's wording.
* Docs headings are Title Case, per [documentation.md](documentation.md#headings). Labels quoted in the docs keep the page's case.

### Words to Use

Pick the word on the left every time.

| Use | Not | When |
| :--- | :--- | :--- |
| Remove | Delete | Takes an entry out of a list or the config, and nothing on disk is lost |
| Delete | Remove | Destroys data, files, or history that cannot come back |
| Clear | Empty, Reset | Removes every entry in a list or log |
| Reset | Restore | Puts settings back to their defaults |
| Save | Apply, Update | Writes changes to the config |
| Done | OK, Close | Closes an editor and keeps its changes |
| Cancel | Close, No | Closes without keeping changes |
| Add | New, Create | Puts a new entry into a list on the page |
| New | Add, Create | Starts a new record in a record picker |
| Import, Export | Load, Upload, Download | Reads a template from a file, or writes one |
| Select | Pick, Choose, Tick, Click | Any instruction to make a choice |
| Turn on, Turn off | Enable, Disable, Tick, Untick | An instruction about a checkbox or toggle |
| Leave blank | Leave empty | An empty field means the default |
| ARGB hex | Hex, color code | A color with its opacity first, such as #66000000 |
| Could not | Failed to, Unable to | An error message |

The control names its own result. "Remove" on a design takes it out of the config and leaves every image already made alone, so it is Remove. Nothing on these pages deletes a file.

### Labels

* Two to four words. Name the thing the field holds, not the action.
* Put a unit at the end in lowercase parentheses, such as **Edit Images cache (minutes)**. Use symbols for standard units, such as `%`, as in **Brighten frame (%)**.
* No colons, periods, or question marks.
* A checkbox label names the feature it turns on, never "Enable X". Write **Detect letterboxing**, not **Enable letterbox detection**. The one exception is the master switch at the top of a section, which reads **Enable** followed by the section name.

### Descriptions

A `fieldDescription` sits under every field that is not obvious from its label.

* One sentence that says what the setting does, starting with a verb. "Crops black bars off an extracted frame."
* Add a second sentence only for the default or a consequence. "0 uses every video."
* Describe the current behavior only. Never "now" or "no longer".
* Never repeat the label.
* Put units, ranges, and what blank means here, never in the label or the placeholder.

| Bad | Good |
| :--- | :--- |
| How frames are chosen: score several, keep the best. | Scores several frames for each one kept. |
| Empty picks new frames; a number repeats them. | Makes frame choice repeat. Leave blank for new frames each refresh. |
| This setting enables the detection of letterbox bars on frames. | Crops black bars off an extracted frame. |

A section intro is one `fieldDescription` right under the section title. It says what the section is for in one sentence.

### Placeholders

A placeholder shows an example of a valid value. It is never the only place an instruction lives, since it disappears once the reader types.

* Show a realistic example with no prefix. `#66000000`, not `e.g. #66000000`.
* Use generic names only, such as `Anime` for a profile or `Bold Titles` for a design.
* For an override that falls back to a default, show the value it falls back to. When that value is not known, write `Default`.
* For a search box, write "Search" and the noun, with no ellipsis. `Search items`.
* For a secret already stored, Base's `bindSecretField` writes "Saved, leave blank to keep". Leave it as is.
* Never put a sentence in a placeholder. Move it to the description.

### Buttons

* A verb, or a verb and a noun. **Save**, **New**, **Edit series**.
* Name the object when the page has more than one of that action. **Edit series** and **Edit movies** sit on the same page.
* Every Save reads **Save**, with no noun, even when a page has several saves.
* An icon only button carries a `title` of one to three words, such as `Remove` or `Expand`. Never a sentence.
* A button that runs for a while shows progress in place. See [Progress](#progress).

### Messages

A message reports what happened in one sentence that ends with a period.

| Kind | Pattern | Example |
| :--- | :--- | :--- |
| Saved | `<Thing> saved.` or `Saved.` | Designs saved. |
| Done | `<Thing> <past verb>.` | Design exported. |
| Working | `<Verb>ing…` | Rendering… |
| Could not | `Could not <verb> the <thing>.` | Could not save the designs. |
| Could not, with a fix | Add one sentence with the fix | Could not load the designs. Reload the page. |
| Missing input | `Enter <the thing>.` or `Select <the thing>.` | Enter a name. |
| Nothing found | `No <things> found.` | No movies found. |

* Use the ellipsis character `…`, never three dots, and only for work in progress.
* When the server gave a reason, show it with `shared.errorText(err, 'Could not save the designs.')`, which resolves to the text.
* Name the count with a correct plural, with `plural` from Base.
* Never say "error occurred". Say what could not happen.

### Confirm Dialogs

A confirm asks one question and states the consequence.

```js
confirmDialog('Remove Bold? Its images in Anime use the default design.', {
    title: 'Remove design',
    confirmText: 'Remove',
    destructive: true
});
```

* The message is a question, then one sentence on the consequence. "Remove Anime? Its assigned items use the default profile."
* The title is the action in sentence case, such as **Remove design**. Never leave the default **Confirm**.
* The confirm button repeats the action verb, such as **Remove** or **Delete**. Never **OK** or **Yes**.
* Set `destructive: true` for anything that loses data.
* Never "Are you sure you want to". Ask the question directly.
* Never use `window.confirm` or `Dashboard.confirm`.

### Empty States

| Where | Pattern | Example |
| :--- | :--- | :--- |
| List in a form | `No <things> assigned.` | No movies assigned. |
| Filtered list | `No <things> match.` | No series match. |
| Nothing to pick from | `No <things> found.` and the fix | No movies found. Add movies to a library first. |

Use `shared.emptySection(text)` for a region and `shared.emptySection(text, { inline: true })` for a list.

### Messages From C#

The API returns text that the page shows as is, so it follows the same rules.

* A full sentence that starts with a capital and ends with a period. "The preview could not be drawn."
* Say what is wrong and, when it helps, what to do. "At least one logo design is required."
* Never return an exception message, a stack trace, or a type name.

## Color

### Tokens

Use Base tokens. They follow the dashboard's theme, light themes included. Never write a hex value for something these cover.

| Token | Use |
| :--- | :--- |
| `--base-accent` | Links, focus rings, primary buttons, progress |
| `--base-accent-hover` | Hover on accent elements |
| `--base-fg` | Washes on elements that fade their own text |
| `--base-surface` | Panel and dialog backgrounds |
| `--base-border` | Every border and divider |
| `--base-gap-wide` | Space between components, such as one field row and the next |
| `--base-gap-narrow` | Space inside a component, such as between a label and its field |
| `--base-mono` | Code, ids, paths, and raw errors |
| `--base-success`, `--base-warning`, `--base-error` | Solid status colors |
| `--base-<color>-bg`, `--base-<color>-fg` | Tinted badge and card backgrounds and text, for green, red, orange, purple, blue, and gray |

### Meaning

A color means the same thing on every page and in every plugin.

| Color | Means | States |
| :--- | :--- | :--- |
| Green | Done and healthy | Active, Synced, Downloaded, Succeeded |
| Red | Failed, or about to lose data | Error, Errored, Failed, Deleting, Pending deletion |
| Orange | Waiting, or needs attention soon | Queued, Expired, Warning |
| Purple | Working right now | Pending, Downloading, Locked, Pending download |
| Blue | Informational | Admin, Imported, Pending replacement |
| Gray | Off or not applicable | Disabled, Ignored, Skipped, Group |

* Map a status to a color through `BADGE_STATES`. Never keep a second map in the plugin.
* Disabled is gray when it means off. Red is only for failure.
* Use "orange" for the warning color in class names, never "amber", "yellow", or "warning".
* Never use color alone. A badge always has a word, and a dot always has a `title`.

## Page Structure

### Shell

Every config page uses Jellyfin's page shell.

```html
<div data-role="page" class="page type-interior pluginConfigurationPage withTabs"
     data-require="emby-input,emby-button,emby-select,emby-checkbox">
    <div>
        <div class="content-primary">
            <div class="verticalSection">
                <div class="sectionTitleContainer flex align-items-center">
                    <h1 class="sectionTitle">Settings</h1>
                </div>
                <div class="fieldDescription ag-section-note">Holds the options every design and profile shares.</div>
            </div>
        </div>
    </div>
    <link rel="stylesheet" href="configurationpage?name=ag_jpkribs_shared.css">
</div>
```

* List `data-require` in the order `emby-input,emby-button,emby-select,emby-checkbox`, dropping any the page does not use.
* Use no inline `style` attributes. Add a class to the plugin CSS, or to Base when it is reusable.

### Tabs

* One registered page per tab, wired with `createConfigPage` and `setTabs`.
* Tab names are one word.
* Keep the tab list in one place, the plugin's shared module, never copied into each page.
* Order tabs from what is used most to what is set once. Artwork Generator is **Designs**, **Logos**, **Profiles**, **Settings**.

### Sections

| Need | Component |
| :--- | :--- |
| A page with several independent groups of settings | `base-collapsible-section`, one per group |
| A group that is always open | `base-collapsible-section fixed`, whose header is a `div` with no icon, so it matches the collapsible sections around it |
| A heading inside a section | `base-subsection-title` |
| An ordered setup, such as a server editor | `base-collapsible-section numbered`, which Base numbers in order |
| Settings that only matter when a checkbox is on | `base-indent` under the checkbox, shown with `shared.bindReveal` |

* A collapsible header is a `button.base-collapsible-header` holding `base-collapsible-title` and `base-collapsible-icon`. Base replaces the icon with `expand_more`.
* Start collapsibles closed, except the first one on a page someone opens to act on.
* Give every top level group on one page the same header. A chart or a summary takes the `fixed` section, never a bare `base-subsection-title`.
* Hide a whole section while its feature is off or it has nothing to show. Never show an empty section.
* Put anything that depends on a picker under that picker, and redraw it when the pick changes.

### Saving

Every save is Base's `createSaveBar` with `fill: true`, drawn through `createArtworkSaveBar` in `ag_shared.js`. Its button reads **Save**.

| Model | Use it when | Where the Save Bar goes |
| :--- | :--- | :--- |
| One Save per page | Settings depend on each other | One bar at the bottom of the page |
| One Save per section | Sections are independent and long | One bar at the bottom of each section's content |
| Editor dialog | Each record is edited on its own | **Done** in `openEditorDialog` |

* The Save Bar is the last element of what it saves. Nothing sits beside it or below it in that section, and no divider line sits above it.
* **Fonts** on the Settings tab is its own form, with a Save Bar reading **Upload** as its last element. It is the one bar that does not read **Save**, since it uploads files rather than saving settings.
* Other actions that belong with that save, such as Reset to Defaults, Clear, Test, Run Now, Refresh, or a select for Test, go in one `<div class="base-button-row">` directly above the Save Bar. Base spaces that row. Its buttons have no margin of their own, so their edges line up with the Save Bar's, the row sits one `--base-gap-narrow` above the Save Bar, and its buttons use the same font weight as Save. A plugin adds no margin, gap, or weight of its own. The Save Bar's element goes directly after the row, never inside a wrapper, since Base spaces them as siblings.
* Each button in that row is `<button is="emby-button" type="button" class="raised base-button secondary fill">`, or `destructive fill` for one that deletes or resets data. A select in that row stays a normal emby select. No `small` buttons in that row.
* Never hand place a Save button, use a `small` Save, a Save Bar without `fill`, a status line of its own beside the Save Bar, or plugin CSS that positions a save or its neighbours.
* `createArtworkSaveBar` checks every shown field through `validateAll` first, wires `setDirty` from the change tracker, and its `onSave` returns the save promise. Its line reports unsaved changes and the result.
* Track unsaved changes with `createChangeTracker`, and pass it to the Save Bar.
* Guard leaving the page with `page.guardUnsaved`.
* Use `shared.saveConfigWith` for get, change, save, and report.

## Components

Every component below is in Base. Use it as Base ships it. When it does not fit, change Base. A Base component used wrong is worse than none, so look at the result on a test server, not only in the code.

### Find the Component

Before writing markup for a list, a row editor, a chart, or a picker, look for it in Base.

```bash
grep -o "export function create[A-Za-z]*" ~/.nuget/packages/jpkribs.jellyfin.base/<version>/assets/jpkribs_shared.d.ts
```

* The typings in that file document every option of every component. Read the component's block before using it.
* Rows of fields with Add and Remove are a `createRepeater`. A list of checkboxes is a `createCheckboxList`. A used amount out of a limit is a `createUsageMeter`. Parts of a whole are a `createPieChart`.
* A hand built version of any of these is a defect, even when it looks the same.
* When a Base component lacks one thing the page needs, use the component and report the gap to Base. Never fall back to building it by hand.
* Never put a stand in where a Base option exists, and never keep a Base gap quietly. Name the missing option under [Hand Built Pieces](#hand-built-pieces) and ask Base's owner for it.

### Fields

| Need | Use |
| :--- | :--- |
| Text, number, or URL | `inputContainer` with `<label for>`, `<input is="emby-input">`, and a `fieldDescription` |
| Choice from a short list | `selectContainer` with `label.selectLabel` and `<select is="emby-select">` |
| On or off | `checkboxContainer checkboxContainer-withDescription` with `emby-checkbox` and `fieldDescription checkboxFieldDescription` |
| A mode that needs a sentence per option | `createChoiceGroup` with `base-choice` cards |
| Several on or off options in a row | `base-toggle-group` with `base-toggle` pills |
| Many checkboxes | `base-check-grid` |
| Library items, such as those a profile is assigned | `createMediaPicker` |
| Many items from a long or searchable list | `createCheckboxList`, with `renderRow` giving each row a `label`, a `sublabel`, and an `image` loader |
| Rows of several fields that are added and removed | `createRepeater` |
| Pairs of a name and a value | `createKeyValueEditor` |
| A list of plain text values | `createStringList` or `createTagInput` |
| Typeahead with removable chips | `createChipSelect`, with `single: true` for one choice |
| One user | `createUserSelector` |
| Several users | `createUserMultiSelector` |
| Several fields side by side | `base-field-row` with `base-field` |
| A secret | `bindSecretField`, `readSecretField`, and `clearSecretField` |
| A template with `{name}` tokens | `placeholderHint` and `bindPlaceholders` |
| Code or JSON, read only | `base-code-block` |
| A color setting | `createColorPicker` with `format: 'argb'`, through `bindColorField` in `ag_shared.js`, in an `ag-field` with its `<label for>` and `fieldDescription` |
| A number on a range, such as an opacity | `createSliderField` |
| A code, path, or link to copy | `createCopyField` |

* Use `emby-select`, never `base-selector-dropdown`, for a field. `base-selector-dropdown` is only for the record picker.
* Every input has a `<label for>`.
* A slider sets one number on a range, a color picker sets a color, and a copy field shows a code, path, or link. Never build any of them by hand.
* Never mount a Base component that holds inputs inside `inputContainer`. Base keeps checkboxes and radios there at their own size, but the server still stretches every other input to full width. Mount it in a plain wrapper, such as `ag-field`, with its label and `fieldDescription`, unless the component's typings say otherwise.
* A color setting whose color comes from the image, under **Palette colors** or **Color from**, shows only its opacity. `bindColorField` hides the Color Picker and shows a `createSliderField` in percent in its place, and the label reads **Overlay opacity**, **Secondary overlay opacity**, or **Text opacity**.

### Buttons

| Need | Class |
| :--- | :--- |
| Primary action | `raised base-button primary` |
| Destructive action | `raised base-button destructive` |
| Secondary action | `raised base-button secondary` |
| Compact action in a section | add `small`, never for a Save or the actions row above it |
| Save | Base's Save Bar through `createArtworkSaveBar`, full width, reading **Save** |
| Actions that belong with a Save | `raised base-button secondary fill`, or `destructive fill`, in one `base-button-row` directly above the Save Bar |
| Icon and label in a toolbar | `raised base-button` holding a `material-icons` span and a label |
| Icon only in a row | `base-row-btn`, with `.dirty` for unsaved and `.success` after saving |

* Every button is `<button is="emby-button" type="button">`.
* Icons are `<span class="material-icons" aria-hidden="true">name</span>`.
* Close a dialog with `base-dialog-close` and `&times;`. Never "✕" or another glyph.

| Action | Icon |
| :--- | :--- |
| Add | `add` |
| Edit | `edit` |
| Remove or delete | `delete` |
| Copy | `content_copy` |
| Move up and down | `keyboard_arrow_up`, `keyboard_arrow_down` |
| Drag | `drag_indicator` |
| Expand | `expand_more` |
| Retry | `refresh` |
| Open | `chevron_right` |
| Import and export | `upload`, `download` |
| Show | `visibility` |

### Records

| Need | Use |
| :--- | :--- |
| One record at a time from a list | `base-selector` with New, Rename, and Remove |
| Records in priority order | `createSortableCardList` |
| A plain list of records | `base-card-list` with `base-card-item-*` |
| Edit one record in a dialog | `openEditorDialog` |
| A large searchable list | `createPaginatedTable` |
| Edit table rows in place | `base-bulk-edit-bar` and `base-edit-row` |
| Uploaded files | `base-asset-list` |
| A thumbnail in a table row | A column `image(item)` loader with `imageShape`, one shape for each column |
| A mark on a table row, such as picked | The table's `rowClass(item, index)` |

* A record picker's actions read **New**, **Rename**, **Remove**, then **Import** and **Export**, in that order.
* Use `createPaginatedTable` for any list that can grow past one screen. Rows open with `actions.onRowClick`.
* Give any other list that can grow past one screen `height: 'fill'`, so it ends at the bottom of the window and scrolls inside.

### Status

| Need | Use |
| :--- | :--- |
| The state of one item | `shared.statusBadge(text, state)` |
| A label on an item | `shared.badge(color, label)` |
| Counts at the top of a page | `shared.renderCards` with `base-cards`, and `compact` for many counts on a phone |
| Label and value pairs | `base-record-card` |
| A notice in the page | `base-info`, `base-success`, `base-warn`, `base-error`, `base-neutral` |
| Raw failure text | `base-error-message` |
| The result of an action | `shared.setStatus` |
| A row of reset actions | `base-reset-group` with `base-reset-row` |

* Never use `Dashboard.alert` for a result. It blocks the page. Use `setStatus`.
* A notice holds one sentence. Use a callout for something the reader must know before acting, not for a result.

### Charts

| Need | Use |
| :--- | :--- |
| Parts of a whole, such as files by Storage | `createPieChart`, with `donut` and the total in `centerText` |
| How much of a limit is used, such as a cache | `createUsageMeter`, with `bytes: true` for sizes |
| Values compared across names | `createBarChart` |
| A value over time | `createLineChart` or `createSparkline` |
| One number with a label | `createStatCard` |

* Pick the chart by the question it answers. A single used of total bar is a Usage Meter, never a Bar Chart.
* Name each slice with a glossary term in sentence case, such as **Cached links**.
* Take colors from Base's series colors. Set a color only when it carries a [meaning](#meaning).
* Give every chart a `label`, and an `emptyText` sentence for when it has nothing to draw.
* Scope a chart to what the page shows. A chart beside a server picker counts that server only.

### Progress

* A task started from a button shows progress in that button with `shared.pollTaskProgress`.
* Progress inside a row uses `base-progress` and `base-progress-bar`.
* Never block the page with `Dashboard.showLoadingMsg`.

### Dialogs

| Need | Use |
| :--- | :--- |
| A yes or no question | `confirmDialog` |
| A few values | `promptDialog` |
| A record editor | `openEditorDialog`, with `wide: true` for a large one |
| A custom dialog | `openDialog` |
| A dialog already in the page markup | `bindDialog` |

* Build dialogs in JS with `openDialog` and its wrappers. Use `bindDialog` only when the markup is large and static.
* A dialog title is in sentence case and names what the dialog does.
* Buttons read, from left to right, the destructive action, then **Cancel**, then the primary action.

## Layout

* Use Base's breakpoints, 768 px for tablet and 600 px for phone. Use 700 px only where Base does, for dialogs.
* Test every page at 375 px wide. Nothing scrolls sideways except a table.
* Look at every changed page on a test server before calling it done. Reload the page after installing a build, since the dashboard caches scripts.
* Spacing comes from `--base-gap-wide` and `--base-gap-narrow`. Never add margins between fields.
* Never set a width in pixels on a field. Let it fill its container.
* Keep rows visibly separate, and put what each control does next to it. A lone toggle word at the far edge of a row is a defect.

## Accessibility

* Every input has a label, and every icon only button has a `title` and an `aria-label`.
* Every icon span has `aria-hidden="true"`.
* Rows that open on click also open with Enter and Space. Base's table and card list do this already.
* Never rely on color alone.

## CSS in This Plugin

* Plugin classes use the `ag-` prefix in kebab case. `base-` is reserved for Base.
* Keep plugin CSS for layout that only this plugin has. A component another plugin could use goes to Base.
* Never override a `base-` class. Change Base instead.
* When Base has a defect that breaks a page, report it and add one marked stopgap rule under a comment that names the defect. Delete the rule in the commit that moves to the Base version with the fix.
* Size a column in `rem` or `fr`. An `em` width changes with the font size of each row, so a smaller heading row falls out of line with its columns.
* Never define a color token. Use Base's.
* Watch for collisions with Jellyfin's own CSS. Jellyfin's global `.card` rule gives paint containment to any element with a bare `card` class. A Base component that draws a fixed popup, such as a select list or a color picker, must not sit inside an element with `contain`, `transform`, or `filter`, or its popup lands off screen.

## Hand Built Pieces

Nothing on the Designs, Logos, Profiles, or Settings tabs is built by hand. `ag_shared.css` holds only layout between Base components: the preview column, the two Preview Frames side by side, the space under a section note, and the `ag-field` wrapper around a color setting. Add nothing hand built. When Base lacks an option, report the gap and add a row here naming the Base option that is missing.

## Checklist

Run every new or changed string through this list.

* [ ] No em dashes, en dashes as punctuation, or semicolons.
* [ ] No colon at the end of a label, and none in a sentence unless it introduces a list.
* [ ] One sentence, or two when the second is the fix or the consequence.
* [ ] Sentences end with a period. Labels, buttons, and titles do not.
* [ ] Sentence case for every string, mirroring Jellyfin, with the words in `casing.json` as written.
* [ ] No "please", "successfully", exclamation marks, ALL CAPS, "(s)", or contractions.
* [ ] The word matches [Words to Use](#words-to-use).
* [ ] Base's typings were searched before any markup was built by hand.
* [ ] The component comes from Base, unchanged.
* [ ] Colors come from Base tokens and mean what [Meaning](#meaning) says.
* [ ] The docs use the same label.
