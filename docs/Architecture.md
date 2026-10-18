# Poster Architecture

The Artwork Generator uses a four layer rendering pipeline to create consistent posters across all styles.

## Layer 1: Canvas (Base Layer)

The foundation layer that provides the visual background for the poster.

**Options:**

- **Video Frame Extraction**: Automatically extracts a frame from the video file using configurable extraction windows. A field of candidates is sampled at widely spaced points across the window and every one is scored before any is chosen, so the frame used is the best of the set rather than the first that was good enough. The sampling starts from a different point on each run, so refreshing offers a different frame rather than the same one.
- **Frame Scoring**: Each candidate is measured with its letterbox bars removed, since bars are not picture and scoring them drags every measurement toward black. Five things are weighed: tone, which favors a mid exposed frame and marks down the blown out as well as the crushed, fine detail, tonal range, colorfulness, which separates a striking frame from a flat gray one, and headroom, which prefers a frame with a calm band for the title to sit in.
- **Series Backdrop**: Uses the series' own backdrop image as the poster background, falling back to a transparent canvas when the series has none.
- **Transparent Background**: Creates a solid color or transparent canvas.

**Processing:**

- Optional brightening of the extracted frame, off by default
- Frames keep out of intros, credits, recaps, previews, and commercials where the server holds media segments for the item, with each kind optional
- Letterbox/pillarbox detection and cropping
- Aspect ratio adjustments and fill strategies

## Layer 2: Overlay (Color Tinting)

A translucent color layer applied over the canvas to enhance text readability and create visual cohesion.

**Features:**

- Configurable ARGB hex colors with alpha transparency
- Applied uniformly across the entire poster surface or use two colors blurred together
- Optional palette derived colors: the overlay color is sampled per episode from the dominant color of the frame, while the configured alpha is preserved

## Layer 3: Graphics (Static Images)

Optional static graphic overlays positioned above the canvas but below text elements.

**Capabilities:**

- User configurable file path for custom graphics
- Automatic sizing and positioning within safe area boundaries
- Supports PNG, JPG, and WEBP formats
- Maintains aspect ratio while fitting within poster constraints

## Layer 4: Typography (Text and Logos)

The top layer containing all text elements, the subtitle, and logos.

**Elements:**

- The subtitle: an episode's season and episode numbers, a season's label, or a film's year
- The title, with automatic wrapping and configurable handling for names too long to fit
- The logo selected for the item, its season, or its series, with configurable positioning
- Style specific typography (Roman numerals, cutout text, etc.)
- Drop shadows and contrasting outlines for enhanced readability
- A Text Position setting that moves the title and subtitle to the top, center, or bottom on every style whose text is a block laid over the image

## Rendering Pipeline

Each poster style follows this exact four layer sequence. The modular approach allows for easy customization and additional poster styles.

Elements that stack vertically, such as the logo, title, and subtitle, are measured before any of them are placed, so they keep a consistent gap and cannot overlap. That gap is the **Element Spacing** setting, applied the same way by every style.
