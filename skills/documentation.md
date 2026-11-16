# Documentation

How to write the README, the docs site, screenshots, and release notes for this plugin. It applies to every page under `docs/`, to `README.md`, and to the `changelog` in `build.yaml`.

## The One Rule

Documentation describes the release it ships with. It reads as if this version were the only one that ever existed.

* Write what the plugin does. Never write what it used to do, what it no longer does, or what it now does.
* Write how a feature works. Never write that a bug was fixed, a problem is gone, or a limit was lifted.
* History belongs in the release notes. See [Release Notes](#release-notes).

| Do not write | Write |
| :--- | :--- |
| Logos now take their color from the poster. | Logos can take their color from the poster. |
| The Light Background button was removed. | Leave it out. |
| Fixed an issue where long titles overflowed. | A title that does not fit follows **Long Titles**. |
| New in 12.4.4.0, Palette Colors take... | Palette Colors take... |

"Now" and "no longer" are fine when they describe state rather than history. "The design no longer exists" describes what a profile points at, so it stays.

A version number belongs in docs only when the reader needs it to act, such as the minimum Jellyfin version or the oldest release that can upgrade directly. Keep those facts in [Installation](../docs/Installation.md) and [Versioning](../docs/Versioning.md), and name the version outright instead of "this release" or "an older release".

Remove notes about Jellyfin versions the plugin no longer supports. The plugin requires Jellyfin 12.4.

## Writing Style

### Sentences

* Use present tense and plain declarative sentences.
* Describe the system in the third person. "Artwork Generator draws the poster."
* Address the reader in the second person only in steps. "Turn on **Artwork Generator** under **Image Fetchers** in the library's settings."
* State one fact per sentence. Aim for 20 words or fewer and split anything over 30.
* Use no contractions. Write "cannot", not "can't".
* Use no em dashes, en dashes as dashes, or semicolons. Split the sentence, or join it with a comma and "and".
* Use colons only to introduce a list or a code block.
* Use parentheses rarely. Move the aside into its own sentence.
* End every sentence with a period.
* Write compounds as separate words the way the UI does, such as "one time setup" and "sign in".
* Write one paragraph per line. Never hard wrap prose.
* Use no marketing words such as "powerful", "seamless", "easily", or "simply".
* Write examples with generic names, such as `Anime` for a profile and `Bold Titles` for a design. Never a real server, person, or address.

### Headings

* Use Title Case for every heading. "## Frame Extraction".
* Page titles are `#` and appear once per page.
* Name the thing, not the action, unless the section is a procedure. "## Profiles" and "## Add the Plugin Repository" are both correct.
* Never rename a heading without fixing its anchors. `mkdocs build --strict` fails on a broken anchor.

### UI Names

* Write a label exactly as the page shows it, in bold. **Edit Images Cache (minutes)**.
* Write a path through the UI with `>` between the steps, in bold. **Dashboard > Plugins > Artwork Generator > Settings**.
* Write a tab as its label, in bold. **Designs**, **Logos**, **Profiles**, **Settings**.
* Quote on-screen messages in double quotes. "Designs saved."
* Write routes, files, settings keys, and values in code. `POST /Plugins/ArtworkGenerator/Preview`.

A docs page and the UI must use the same word for the same thing. When a label changes, search `docs/` for the old one and change it everywhere in the same commit.

## Site Structure

The docs site is MkDocs with the Material theme, built from `docs/` by `mkdocs.yml` and published to `https://jpkribs.github.io/jellyfin-plugin-artworkgenerator/`. `docs/plans/` is left out of the build.

### Navigation

Keep the nav in this order.

1. **Overview** (`index.md`). What the plugin is, how it works, and how to get a poster.
2. **Getting Started**. Installation.
3. **Designs**. Poster Styles (`Styles.md`), Design Examples (`EXAMPLES.md`), and Live Preview (`Preview.md`).
4. **Reference**. Settings (`SETTINGS.md`), Poster Architecture (`Architecture.md`), and Versioning.

Name new page files in PascalCase with no spaces, such as `LivePreview.md`. `EXAMPLES.md` and `SETTINGS.md` keep their names, since links and tests point at them.

### Design Examples

Each folder under `docs/examples/` holds one `Template.json` and the images the Demo project renders from it. `EXAMPLES.md` shows them and links each template for download.

* Add a template by adding its folder and a section in `EXAMPLES.md`, then run the demo render per [testing.md](testing.md#demo-render).
* `TemplateCoverageTests` loads every template, so a template the plugin cannot read fails the tests.

## Architecture

[docs/Architecture.md](../docs/Architecture.md) describes how a poster is drawn, as four layers: the canvas, the overlay, the graphic, and the text and logos. It is the page a new contributor, person or AI, reads first.

Review it after every change, and update it in the same commit when the change affects any of these.

* A layer is added, removed, or drawn in a different order.
* A canvas source, a frame rule, or how frames are scored changes.
* How stacked elements are measured and spaced changes.

Name settings exactly as the page shows them. Never link to source files, since the docs build refuses links outside `docs/`.

### Page Templates

A reference page opens with one sentence on what it covers, then one section per tab or per group on a tab, in the order the page shows them.

```markdown
# Artwork Generator Settings

One sentence that says what the page covers.

## Settings

* **Setting Name**: what it does. Default value.
```

### Settings References

List settings as bullets in the order the page shows them. Start each with the label in bold, then a colon, what it does, and its default.

```markdown
* **Sampling Rate**: how many frames are scored for each one kept. Default Medium.
```

When a page has many settings with defaults, use a table instead.

| Setting | What it does | Default |
| :--- | :--- | :--- |
| **Sampling Rate** | Scores frames for each one kept | Medium |

Table cells have no closing period. Keep them to one sentence.

### Troubleshooting

Write trouble as a table of what the reader sees and what to do.

| Reason | What to do |
| :--- | :--- |
| Broken previews in **Edit Images** on another device | Fill in **Published Server URIs** on the server, per Installation |

Put the exact on-screen text in the first column. Use italics for a placeholder inside it, such as *server*.

### Admonitions

Give every admonition a short title.

```markdown
!!! warning "Use at your own risk"
    One or two sentences.
```

* `note` for a detail the reader needs.
* `tip` for a better way to do something.
* `warning` for anything that can lose data or break a server.
* `info` for a requirement, such as a published server address.

### Links

* Link pages relatively with their `.md` name, such as `[Installation](Installation.md#set-a-published-server-uri)`.
* End a Getting Started page with one "Next" line, such as `Next: [Poster Styles](Styles.md).`
* Link to Base's README for a shared component instead of describing it again.

## README

The README is a landing page. The docs site holds the detail.

1. The icon, `<img src="docs/assets/icon.png" alt="Artwork Generator" width="96" align="left">`, then a one paragraph pitch and `<br clear="left">`.
2. `## Documentation`. One sentence on what the docs cover, a bold link to the site, then the repository URL for Jellyfin in a code block. It closes with one sentence that credits the bundled fonts, naming each family and the SIL Open Font License 1.1, and says each family's license sits beside its files in `Jellyfin.Plugin.ArtworkGenerator/Fonts/`.
3. `## AI Disclaimer`. The owner's note on how the project is made. Leave its words as they are.

Use `##` for README headings. Keep the README under 40 lines.

## Screenshots

### What to Capture

* Capture every tab, section, and dialog a docs page describes, including **Fonts** on the Settings tab.
* Capture the dark theme only, which is Jellyfin's default.
* Capture with the [screenshot kit](#the-screenshot-kit), the page harness described in [testing.md](testing.md#harness), with headless Chromium at 2x. Never capture a live server's data.
* Use generic data. Series such as `Northern Lights` and `Harbor Town`, designs `Default` and `Bold Title`, profiles `Default` and `Anime`, and a logo sample name such as `Northern Lights`.
* Capture with designs and profiles filled in, so the previews, the **Assigned Items** list, and the **Images** grids show real choices.
* Draw every preview with the plugin's own renderer and a bundled font, so the screenshot shows real output.
* Hide nothing with editing. When a value is private, change the harness data instead.

### The Screenshot Kit

[`tools/screenshots/`](../tools/screenshots/README.md) holds everything the captures need. Its README lists each file, what the kit needs, and the commands to retake every shot or one.

| File | Does |
| :--- | :--- |
| `server/` | The screenshot server. It serves the plugin pages, Base from the NuGet cache, and jellyfin-web's stylesheets, and answers the plugin's routes with the plugin's own code |
| `www/index.html`, `www/upgrade.js`, `www/harness.js` | The page shell, the `emby-*` upgrades, and the stand-in `ApiClient` with the generic data |
| `shoot.mjs` | Opens each page, sets it up, and writes every PNG to `docs/screenshots/` |

```bash
cd tools/screenshots
dotnet run --project server &
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shoot.mjs
```

`node shoot.mjs Designs Fonts` retakes only the named files. Look at every PNG it writes before committing it. New sample designs and profiles go in `server/Program.cs`, and new sample items go in `www/harness.js`.

### Sizes

| Kind | Size | How |
| :--- | :--- | :--- |
| Full page | 1656 wide | The browser at 860 px and 2x, cropped to the form with 12 px on each side |
| Section | 1656 wide | The same, cropped from the section's header to its last element |
| Dialog | About 1380 wide | The browser at 860 px and 2x, cropped to the dialog |
| Phone | 750 wide | The browser's device mode at 375 px and 2x |

Save every capture as PNG. Crop out the browser frame and the Jellyfin header unless the header is the subject.

### Names and Placement

* Save to `docs/screenshots/`.
* Name the file after what it shows, in Title Case, with a dash between words. `Designs.png`, `Preview-Modal.png`.
* Link with alt text that names the subject. `![Designs](screenshots/Designs.png)`.
* Place two related shots side by side in a table whose header row holds the captions.

### Artwork Examples

The images under `docs/examples/` are the plugin's own output. The demo render draws them from each `Template.json`, per [testing.md](testing.md#demo-render). Every template names bundled fonts only, so the images match on any server. Never edit an example image by hand.

### When to Retake

Retake a screenshot in the same commit as any change that alters what it shows, including a label, a tab, a layout, or the Jellyfin version in the header. A new Base version changes how every component looks, so retake every screenshot with the kit when it is raised. A screenshot that shows a tab or label the plugin no longer has is a bug.

## Installing a Build by Hand

```bash
./build.sh
```

The zip and its `.md5` land in `dist/`. Unzip it into `<config>/plugins/Artwork Generator_<version>/` on a Jellyfin 12.4 test server and restart the server.

## Building the Docs

```bash
pip install -r .github/docs/requirements.txt
```

```bash
mkdocs serve -a 127.0.0.1:8765
```

```bash
mkdocs build --strict
```

Run the strict build before every docs commit. CI runs the same build and refuses broken links, anchors, and nav entries.

## Release Notes

Release notes live in the `changelog` of `build.yaml`, under four keys. The publish workflow copies them into `manifest.json`, so every line reaches users' plugin catalogs.

| Key | Holds | Voice |
| :--- | :--- | :--- |
| `added` | A feature that did not exist | The feature name, then what it does |
| `changed` | Behavior a user will notice | The new behavior, in present tense |
| `fixed` | A bug | What went wrong in past tense, then the fix in present tense |
| `developer` | Routes, schema, tests, and tooling | Plain statements for other developers |

```yaml
changelog:
  added:
    - Palette Colors, which take the overlay colors from each image
  fixed:
    - A long subtitle ran off a portrait poster. Long Subtitles decides how it fits
```

* This is the one place that may say "now", "no longer", and "was".
* List only this release. Drop a line that shipped in the last release, and a line a later change in this release replaced.
* Write full sentences with no closing period.
* Write one entry per change a user would notice. Fold internal steps into the change they serve.
* Name UI labels exactly as the page shows them, without bold or backticks.
* Name the Base version in `developer`, with what it replaced locally.
* Give a new or changed route or query parameter a `developer` line.
* Keep single and double quote characters out of every entry, and never put a colon followed by a space in one. The release workflows pass these values through the shell, and YAML reads such an entry as a key.

## Commit Messages

* Write an imperative sentence in sentence case with no closing period. "Fit a long subtitle on a portrait poster".
* Describe the behavior, not the files. Use no prefixes such as `fix:` or `feat:`.
* Add a body of plain paragraphs, wrapped at 72 characters, when the reason is not obvious.
* Commit docs and screenshots with the change they describe.

## Checklist

* [ ] Nothing describes an earlier version, a removed feature, or a fixed bug.
* [ ] Every UI label matches the page exactly and is bold.
* [ ] No page names a whole library as something the plugin draws. The targets are series, seasons, episodes, films, videos, collections, and folders.
* [ ] The bundled fonts table in Settings, the README credit, and the fonts the plugin ships name the same nine families.
* [ ] Every heading is Title Case.
* [ ] No em dashes, semicolons, or contractions.
* [ ] Every screenshot matches the current UI and uses generic data.
* [ ] [Architecture](../docs/Architecture.md) was reviewed and matches the code.
* [ ] Every term matches [glossary.md](glossary.md).
* [ ] `mkdocs build --strict` passes.
* [ ] `build.yaml` has a changelog entry for any change a user would notice.
