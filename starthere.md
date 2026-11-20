# Start Here

Read this file before you change anything in this repository. It applies to people and to AI assistants alike.

## The Guides

Each guide in [skills](skills/) covers one kind of work. Read the guide that matches the task before you start it.

| Guide | Read it before you |
| :--- | :--- |
| [skills/glossary.md](skills/glossary.md) | Name anything, in the UI, the docs, or code. |
| [skills/style.md](skills/style.md) | Build or change anything a user sees, including labels, messages, and layout. |
| [skills/code.md](skills/code.md) | Write or change code or comments in any language. |
| [skills/testing.md](skills/testing.md) | Write or run tests, check a page script, or run the demo render. |
| [skills/profiling.md](skills/profiling.md) | Find or fix slow C#, or change code on a hot path. Profile only for a reported symptom, a hot path change, or a number that grew. |
| [skills/documentation.md](skills/documentation.md) | Write or change the README, the docs site, screenshots, or release notes. |
| [skills/review.md](skills/review.md) | Call any change done. |
| [skills/release.md](skills/release.md) | Cut a release. |

A task that touches several areas needs every matching guide. A new setting, for example, needs the glossary, style, code, testing, documentation, and review guides.

Read [Poster Architecture](docs/Architecture.md) before changing how a poster is drawn.

## Ground Rules

* **Base comes first.** Anything reusable lives in [JPKribs.Jellyfin.Base](https://github.com/JPKribs/jellyfin-plugin-base). Search it before you build anything, use what it has, and never copy or override it locally.
* **Describe the current release only.** Docs, labels, and comments say what the plugin does today. Release notes are the only place for what changed.
* **Keep it short.** Every label, message, and comment is as brief as it can be while staying clear.
* **One word per concept.** Use the term in the glossary, everywhere.
* **Prove it with a test.** A change is not done until a test shows that its inputs give the right output.

## Verify Before Done

Work is done when it has been checked, not when it has been written.

* Run every check the change needs, and read its output. A check that did not run has not passed, and neither has one that ran before the last change.
* Look at every changed page on a test server.
* Read the whole diff before reporting, using [skills/review.md](skills/review.md).
* Check every new string against the style checklist and the glossary.
* Check every claim in a doc against the code or the running plugin.
* Report what was checked, what was not, and why. Never call work done while a check fails.

## Stop and Ask

Stop and ask before any of these, even when the task seems to call for it.

* **Stored data.** A change to the shape of the configuration, whose file name is pinned to `Jellyfin.Plugin.EpisodePosterGenerator.xml`, of `logos.json`, or of an exported template, or anything that resets, moves, or deletes them or the `artworkgenerator` data folder.
* **Media.** Anything that replaces or deletes an item's existing images, or changes when a refresh writes one.
* **Security.** A change to the admin policy on `ConfigurationController`, to the anonymous `Generated/{token}` route or its tokens, or to how **Font Path** and **Graphic File Path** are read.
* **Base.** Any change to Base, since every plugin takes it.
* **Settings.** Renaming, removing, or changing the default of a setting, a design field, a profile field, or an enum value users already have stored.
* **Releases.** Raising the version, publishing, or pushing to `master`.
* **The rules.** Any case where following a guide would make the result worse.

Explain what you would change, why, and what it risks. Wait for an answer before you act.

## The Repository

| Path | What it holds |
| :--- | :--- |
| `Jellyfin.Plugin.ArtworkGenerator/` | The plugin source, laid out as in [skills/code.md](skills/code.md#layout). |
| `Jellyfin.Plugin.ArtworkGenerator/Web/` | The dashboard pages, as HTML, JS, and CSS. |
| `Jellyfin.Plugin.ArtworkGenerator/Configuration/` | The plugin configuration model. |
| `Jellyfin.Plugin.ArtworkGenerator/Api/` | The plugin's routes, in `ConfigurationController`. |
| `Jellyfin.Plugin.ArtworkGenerator/Assets/` | The logo, the demo images, and the bundled fonts. |
| `Jellyfin.Plugin.ArtworkGenerator/Services/Posters/Types/` | One generator for each poster style. |
| `Jellyfin.Plugin.ArtworkGenerator.Tests/` | The xUnit tests, including the docs and template checks. |
| `Jellyfin.Plugin.ArtworkGenerator.Demo/` | The demo render, which draws every example template into `docs/examples/`. |
| `docs/` | The documentation site, built with MkDocs. |
| `docs/examples/` | One folder per example design, with its template and rendered images. |
| `tools/` | The screenshot kit and the frame benchmark. Neither is part of the plugin build or the solution. |
| `build.yaml` | The version, the artifacts, and the release notes. |
| `build.sh` | Builds the release zip into `dist/`. |
| `skills/` | The guides. |
| `../Shared/jellyfin-plugin-base/` | A local checkout of Base, when present. Read it, and never change it from work on this plugin. |
