# Screenshot Kit

Takes every screenshot in `docs/screenshots/` from a harness that runs the plugin's own pages and code, with generic data and Jellyfin's real stylesheets. Nothing here is part of the plugin build or its zip.

| File | Does |
| :--- | :--- |
| `server/Server.csproj`, `server/Program.cs` | The screenshot server on `127.0.0.1:8849`. It serves the plugin pages, Base from the NuGet cache, and jellyfin-web's stylesheets, and answers the plugin's read-only routes with the plugin's own `SettingOptions`, `PreviewService`, and `FontCatalog` |
| `www/index.html` | The page shell. The server fills in jellyfin-web's stylesheets in load order |
| `www/upgrade.js` | Adds the classes and markup Jellyfin's `emby-*` elements add on upgrade, so the real CSS applies |
| `www/harness.js` | A stand-in `ApiClient` with the generic series, movies, and collections, and loads one page |
| `shoot.mjs` | Opens each page, sets it up, and writes every PNG to `docs/screenshots/` |

The sample designs, logo designs, and profiles are in `BuildConfig` and `BuildLogos` in `server/Program.cs`. The design previews draw on the plugin's demo images in `Jellyfin.Plugin.ArtworkGenerator/Assets/Demo/`.

## Requirements

* The .NET 10 SDK.
* Base restored to the NuGet cache at the version the plugin's csproj references. Run `dotnet restore` on the plugin once.
* jellyfin-web. The default is the macOS app's, `/Applications/Jellyfin.app/Contents/Resources/jellyfin-web`. Set `JELLYFIN_WEB` for another.
* Georgia and Georgia Bold, which the **Fonts** shot lists as uploaded fonts. The default is macOS's `/System/Library/Fonts/Supplemental`. Set `SAMPLE_FONTS` to another folder that holds `Georgia.ttf` and `Georgia Bold.ttf`. The server copies them to a temporary folder.
* Node.js and playwright-core. Set `PLAYWRIGHT_CORE` to playwright-core's `index.mjs` when it is not installed where Node finds it.
* Chromium. The default is Playwright's Chromium 1148 in `~/Library/Caches/ms-playwright`. Set `CHROMIUM` to another Chromium binary.

## Run

From this folder, start the server and leave it running.

```bash
dotnet run --project server &
```

Take every shot.

```bash
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shoot.mjs
```

Take only the named ones, by file name without `.png`.

```bash
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shoot.mjs Designs Preview-Modal
```

The names are `Designs`, `Preview-Modal`, `Logos`, `Profiles`, `Settings`, and `Fonts`. Look at every PNG it writes before committing it. Stop the server when done.
