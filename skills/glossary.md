# Glossary

The agreed name for every concept in this plugin. The UI, the docs, messages, and code use these words and no others.

* Use the **Term** in the UI, the docs, and messages.
* The **In Code** column names the type or member that holds it, when the code name differs.
* Never use a word from the **Not** column for the concept.
* Add a term here before it appears anywhere else. Rename it here first when it changes, then everywhere in the same commit.

## Items and Images

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Item | A series, season, episode, film, video, collection, folder, or library the plugin draws for | `ArtworkSubject` | Media, entry |
| Kind | Which of those an item is, one row in the **Images** table | `ArtworkItemKind` | Type, category |
| Film | A movie. The **Images** row and the assignment list read **Movies** | `ArtworkItemKind.Movie` | |
| Video | A music video or home video that is neither an episode nor a film | `ArtworkItemKind.Video` | Clip, standalone file |
| Image | One picture the plugin makes for an item, as **Primary**, **Thumb**, **Logo**, or **Backdrop** | `ArtworkSlot` | Slot, artwork type |
| Primary | The main poster of an item | `ArtworkSlot.Primary` | Cover, main image |
| Thumb | A landscape image clients show in rows such as next up | `ArtworkSlot.Thumb` | Thumbnail |
| Logo | A transparent text logo drawn with a logo design | `ArtworkSlot.Logo` | Clear logo, wordmark |
| Backdrop | A frame from the video with no design | `ArtworkSlot.Backdrop` | Fanart, background |
| Shape | Whether an image is **Portrait** or **Landscape** | `ArtworkShape` | Orientation, format |

## Designs

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Design | A named set of poster settings, edited on the **Designs** tab. Each draws both shapes | `PosterConfiguration` | Configuration, config, template |
| Default design | The design an image falls back to when its design is gone. It cannot be renamed or removed | `IsDefault` | Base design |
| Style | The layout a design draws, such as Standard or Cutout | `PosterStyle` | Type, theme, mode |
| Template | A design exported to JSON, to share or import | | Preset, export file |
| Canvas | The picture under everything else, as set by **Canvas Background** | `CanvasSource` | Background layer |
| Frame | One still taken from a video | | Screenshot, snapshot |
| Grid | A canvas tiled from several frames | `CanvasSource.Grid` | Collage, mosaic |
| Overlay | The color washed over the canvas | `OverlayColor` | Tint, filter |
| Palette Colors | Takes the overlay colors from each image instead of the chosen ones | `PaletteDerivedColors` | Palette-derived, auto color |
| Static Graphic | An image file drawn on every poster of a design | `GraphicPath` | Watermark, overlay image |
| Title | The item's own name, the large line | `ShowPrimary`, `Primary*` | Primary text, heading |
| Subtitle | The smaller line, such as S01E05 or SEASON 2 | `ShowSecondary`, `Secondary*` | Secondary text, episode info |
| Safe Area | The margin kept clear on every edge | `PosterSafeArea` | Padding, bleed |
| Element Spacing | The gap kept between the logo, title, and subtitle | `ElementSpacing` | Margin |
| Live Preview | The images at the top of the **Designs** and **Logos** tabs, drawn from the settings on screen | `PreviewService` | Sample, demo |
| Preview As | The kind of item the design previews are drawn for | `PreviewKind` | Preview type |
| Component | A sample image a preview is drawn from, such as **Canvas Image** or **Series Poster** | | Asset, input image |

## Logos

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Logo design | A named set of logo settings, edited on the **Logos** tab | `LogoConfiguration` | Logo config, logo template |
| Logo Source | Which name of the item a logo shows | `TitleSource` | Name source |
| Sample name | The text the logo preview draws. It is never saved | | Demo text, test name |
| Letters Filled With | Whether the letters are a color or a frame from the show | `LogoFill` | Fill mode, texture |
| Color From | Where a color fill takes its color, the chosen color or the item's own art | `LogoColorSource` | Color mode, sampling |

## Profiles

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Profile | Which images each kind of item gets, which design draws each, and the items it applies to | `ArtworkProfile` | Rule, preset |
| Default profile | The profile every item not assigned elsewhere uses. It cannot be renamed or removed | `IsDefault` | Global profile |
| Assigned | An item listed under a profile. An item belongs to one profile at a time | `SeriesIds`, `MovieIds`, and the other id lists | Linked, mapped |
| Images | The **Images** tabs on the Profiles tab, one grid per image with a row per kind of item, each row turned on with **Generate** | `Slots` of `SlotAssignment` | Slot matrix, Images table |
| Generate | Makes an image whenever an item is missing it | `SlotAssignment.Enabled` | Enable, active |
| Second design, third design | Extra designs that only add choices in **Edit Images** | `SecondaryDesignId`, `TertiaryDesignId` | Alternate design, fallback |

## Frames

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Frame Extraction | How frames are taken from a video and cleaned up, on the **Settings** tab | `FrameExtractionSettings` | Capture settings |
| Extraction window | The stretch of a video frames come from, set by **Extraction Start (%)** and **Extraction End (%)** | `ExtractWindowStart`, `ExtractWindowEnd` | Range, span |
| Sampling Rate | How many frames are scored for each one kept, **Low**, **Medium**, or **High** | `SamplingRate` | Quality, sample size |
| Frame pool | The shared set of frames every image of one item draws from | `FramePoolService` | Cache, frame cache |
| Reuse Samples | Lets an item's images draw from overlapping frames | `ReuseSamples` | Share frames |
| Videos Per Parent | The most videos a series, season, collection, folder, or library takes frames from | `VideosPerParent` | Video limit |
| Media segments | Intros, outros, recaps, previews, and commercials a segment provider marks | `MediaSegmentType` | Chapters, markers |
| Letterboxing | Black bars on a frame, cropped off by **Detect Letterboxing** | `EnableLetterboxDetection` | Black bars, borders |
| Fixed Frame Seed | A number that makes frame choice repeat | `FixedExtractionSeed` | Random seed |

## Fonts

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Bundled font | A font family that ships inside the plugin, always available | `BundledFonts` | Built-in font, included font |
| Uploaded font | A .ttf or .otf file in the fonts folder, added with **Upload** on the **Settings** tab | `UploadedFontStore` | Custom font, user font |
| Server font | A font family installed on the server, the **Server** group in a font list | `FontSource.System` | System font, local font |
| Fonts folder | `fonts` in the plugin's data folder, which holds the uploaded fonts | `UploadedFontStore.Folder` | Font directory |

## Jellyfin

| Term | Meaning | Not |
| :--- | :--- | :--- |
| Edit Images | Jellyfin's dialog for picking an item's images, where the plugin offers choices | Image editor, image picker |
| Choices | The images the plugin offers in **Edit Images** for one image | Candidates, alternates |
| Image Fetchers | The library setting where **Artwork Generator** is turned on | Image providers, metadata providers |
| Refresh | Jellyfin's metadata refresh, which makes any missing image a profile turns on | Scan, rebuild |
