# Artwork Generator Settings

The plugin has four tabs. **Designs** set how a poster looks, **Logos** set how a text logo looks, **Profiles** choose which images are made and which design draws each one, and **Settings** hold the plugin-wide options. Artwork is made for series, seasons, episodes, and films. Labels below match the configuration pages.

## Settings

* **Choices In Edit Images**: how many alternates to offer per image type when replacing an image from the Edit Images dialog, 1 to 10. Each one is rendered from a different frame and costs its own ffmpeg extraction, so higher values make the dialog slower to open. An item with no image of that type is only offered one, because that request comes from an automatic refresh that keeps a single image. Default 3.
* **Fixed Frame Seed**: leave empty to pick new frames on every refresh. Any whole number makes frame choice repeatable, so the same item always gets the same frames. Every image of one item, such as a season's poster, thumb, and backdrop, draws from the same shuffled set of frames, each starting at a different place in it. Default empty.

## Profiles

* **Profile**: the profile being viewed and edited. The default profile applies to everything not assigned to another one. It cannot be renamed or deleted.
* **Assigned Series** and **Assigned Movies**: the series and films that use the active profile. An item belongs to one profile at a time. A profile can carry both.
* **Images**: one row each for series, seasons, episodes, and movies. Tick an image to generate it whenever an item is missing one.
  * **Primary**: the main poster. Choose Portrait or Landscape, then a design. Series, seasons, and movies default to portrait and episodes to landscape. Any design can draw either shape.
  * **Thumb**: a landscape image, offered for every kind. An episode thumb is what many clients show in the next-up and resume rows.
  * **Logo**: a transparent text logo for a series or a film, drawn with a logo design.
  * **Backdrop**: a frame from the video with no design. For an episode it is also saved after its primary image is made, when the episode has no backdrop.
* **Backdrops**: aspect ratio, letterbox detection, HDR brightening, and extraction window for backdrop frames.

Jellyfin only asks the plugin for series and season images in libraries where **Artwork Generator** is ticked under Image Fetchers for those item types in the library's settings. Episodes work as before.

## Designs

Every design draws both shapes. The profile decides which one each image uses, so a single design covers a portrait series poster and a landscape thumb without being set up twice. The live preview shows both side by side.

Sizes are a percent of the poster's short side. A portrait image measures them against the average of its two sides instead, so text carries the same weight on a tall poster as it does on a wide one.

* **Active Design**: the design being viewed and edited. Profiles pick designs by name; deleting one sends its images to the default design. The default design cannot be renamed or deleted.
* **New, Rename, Delete**: manage named designs.
* **Export, Import**: save a design to JSON, or load one as a new design.
* **Preview As**: whether the previews show a series, a season, an episode, or a film.

What a design draws depends on the item. An episode shows its name as the title and a code such as S01E05. A season shows the series name as the title and its season, such as SEASON 2 or S02. A series shows only its name: no season count and no year, since the name is a series' whole identity. Styles built around a number adapt: Cutout punches the series name itself out of the overlay, Numeral draws the name where the numeral would go, and Timeline drops its progress bar.

Split lays out landscape only, so its portrait images are drawn with Standard instead. The Designs page says so under the previews.

## Canvas

* **Canvas Background**: the poster's base image. Extract Frame from Video, Use Series Backdrop, or No Background. Seasons and series extract from their own episodes. Default Extract Frame.
* **Extraction Start (%)**: earliest point to pull a frame from, as a percent of runtime. Default 20.
* **Extraction End (%)**: latest point to pull a frame from, as a percent of runtime. Default 80.
* **Brighten HDR (%)**: percent every extracted frame is brightened by. It is meant for HDR sources that come out dim after tone mapping, but nothing detects HDR, so it lifts all frames alike. Default 0, which leaves the frame as it was extracted.

## Letterbox

* **Enable Letterbox Detection**: crop black bars off an extracted frame. Default on.

## Poster

* **Style**: the layout. Standard, Bloom, Brush, Cutout, Fade, Frame, Frosted Glass, Logo, Numeral, Split, Striped, or Timeline. Default Standard.
  * Timeline draws a season progress bar. It needs the season's episode count, so the bar renders full when the count is unknown, and it does not refresh on its own as more episodes are added to an airing season.
  * Striped draws its sash from the overlay colors. The main band uses Overlay Color and the pinstripes use Secondary Overlay Color.
* **Fill Strategy**: how the canvas fits the poster. Original, Fill, or Fit. Portrait images always crop to fit, since a tall cut of a widescreen frame cannot keep its original shape. Default Original.
* **Landscape Aspect Ratio**: output aspect ratio for landscape images. Default 16:9.
* **Portrait Aspect Ratio**: output aspect ratio for portrait images. Default 2:3.
* **Safe Area**: margin kept clear around all edges. The percent applies to the poster's short side and the same pixel amount is used on all four sides. Default 5.
* **Element Spacing**: gap kept between stacked elements such as the logo, title, and subtitle, as a percent of the poster's short side. Every style resolves its spacing through this one value, so raising it pushes elements further apart everywhere. Default 2.
* **Text Position**: where the title and subtitle sit, Top, Center, or Bottom. Left on Design default each style keeps the placement it was built around, which is why turning this setting on moved nothing. Cutout, Fade, Striped, and Frame do not offer it: their text is part of the artwork rather than a block laid over it, and Frame has its own Text Edges instead. Default Design default.

## Outline (Style is Cutout or Brush)

* **Enable Outline**: draw a contrasting outline around the cut-out shape — the subtitle for Cutout, the brush stroke for Brush. Default on.

## Cutout (Style is Cutout)

* **Type**: what the cutout shows. Code such as S01E05, or Text spelled out. Default Code.

## Frame (Style is Frame)

* **Text Edges**: how the border's two edges are filled.
  * **Top edge first** and **Bottom edge first** put whichever line the item has into that edge, the other line taking the opposite one. A series with only a title and a season with only a subtitle therefore look the same. Default is top edge first.
  * **Title always top** and **Title always bottom** pin the title to one edge and the subtitle to the other, so an item missing one of them leaves that edge empty.

## Logo (Style is Logo)

* **Logo Position**: vertical placement. Top, Center, or Bottom. Default Center.
* **Logo Alignment**: horizontal placement. Left, Center, or Right. Default Center.
* **Logo Height**: logo height as a percent of the poster's short side. Default 30.

## Subtitle

* **Show Subtitle**: draw the subtitle, the smaller line beside the title. An episode shows its season and episode, a season shows which season it is, and a series has none. Default on.
* **Font**: font family for the subtitle. Default Arial.
* **Use Custom Font**: use a font file instead of a family. Default off.
* **Font Path**: path to the custom font file.
* **Font Style**: weight or style such as Bold. Default Bold.
* **Font Size**: subtitle size as a percent of the poster's short side. Default 7.
* **Font Color**: text color as ARGB hex. Default #FFFFFFFF.

## Title Text

* **Show Title**: draw the title, which is the episode name for an episode and the series name otherwise. Default on.
* **Long Titles**: what to do when a title does not fit. Ellipsis trims it. Abbreviate shortens it in stages: first the text before a divider, then the first sentence, then the first letter of every word with periods such as L.O.T.R., keeping dividers and skipping middle initials if still too wide. Drop Name hides it. Default Ellipsis.
* **Font**: font family for the title. Default Arial.
* **Use Custom Font**: use a font file instead of a family. Default off.
* **Font Path**: path to the custom font file.
* **Font Style**: weight or style such as Bold. Default Bold.
* **Font Size**: text size as a percent of the poster's short side. Default 10.
* **Font Color**: text color as ARGB hex. Default #FFFFFFFF.

## Overlay

* **Palette-Derived Colors**: replace the overlay color channels with the dominant color sampled from each episode's image. The secondary color becomes a darker shade of it and the alpha values below still apply. Default off.
* **Overlay Color**: color drawn over the canvas as ARGB hex. Default #66000000.
* **Overlay Gradient**: gradient direction. None, Left To Right, Bottom To Top, or a diagonal corner. Default None.
* **Secondary Overlay Color**: the gradient's second color as ARGB hex. Default #66000000.

## Graphic

* **Graphic File Path**: path to an image drawn on the poster.
* **Graphic Size (%)**: size of the graphic as a percent of the poster's short side. The graphic keeps its own proportions inside that size, so it is never stretched. Default 25.
* **Graphic Position**: vertical placement. Top, Center, or Bottom. Default Center.
* **Graphic Alignment**: horizontal placement. Left, Center, or Right. Default Center.

## Logos

Logo designs are stored in their own file, `logos.json`, in the plugin's data directory, rather than inside the plugin configuration. Editing a logo therefore never rewrites the rest of your settings. Designs saved by an earlier version move into that file automatically the first time the plugin loads.

* **Logo Design**: the logo design being viewed and edited, with New, Rename, and Delete. At least one must exist.
* **Sample name**: the box under the preview, which starts out reading Demo Logo Text. What you type is previewed but never saved.
* **Name From**: Title, Original Title, Sort Title, or Folder Name. Falls back to the title when the chosen name is empty. Default Title.
* **Remove Year**: remove a year in brackets such as (2019). Folder names also lose a trailing year and tags like [tvdbid-12345]. Default on.
* **Names With a Subtitle**: how to treat a name split by a colon or spaced dash. Draw the whole name, keep only the title, keep only the subtitle, or draw both at two sizes with either part large. The two-size layouts read the way a spin-off's own logo usually looks. A name with no colon or dash is always drawn whole. Default draws the whole name.
* **Small Line Size (%)**: the small line's size as a percent of the large one, in the two-size layouts. Default 45.
* **Remove Pattern**: an optional regular expression whose matches are removed. An invalid pattern is ignored.
* **All Capitals**: draw the name in capitals. Default off.
* **Lines**: one line, or up to two. A name splits onto two lines only when that lets the text grow noticeably. Default up to two.
* **Font, Font Style, Use Custom Font, Font Path**: the typeface. Default Arial Bold.
* **Letters Filled With**: a color, or a frame from the show. A frame fill cuts the letters out of a picture, taken from the same shared set of frames the show's other images come from, and brightened so it still reads on a dark background. A heavy font shows more of the picture and an outline helps it stand out. With a frame fill, the Edit Images dialog offers several logos, each cut from a different frame. Default a color.
* **Color From**: Chosen Color, or the main color of the item's own Top Level Poster or Top Level Backdrop, lifted to stay legible. Default Chosen Color.
* **Text Color**: ARGB hex. When sampling, this is the fallback and its opacity still applies. Default #FFFFFFFF.
* **Outline, Outline Color, Outline Width (%)**: an optional stroke around the letters, its width a percent of the font size. Default off, black, 4.
* **Drop Shadow**: a soft shadow under the letters. Default off.
* **Width, Height**: the room the lettering is laid out in, not the size of the file. The finished logo is trimmed to its own artwork, the way a downloaded clear logo is, so the PNG is usually smaller than this. Default 800 by 310, the common HD clear logo proportions.
