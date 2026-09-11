# ![Episode Poster Generator](Jellyfin.Plugin.EpisodePosterGenerator/Assets/Logo.png)

A Jellyfin plugin that automatically generates custom episode posters using smart frame analysis, black frame detection, letterbox detection, and configurable styling. Perfect for filling in missing or generic episode artwork with clean, consistent visuals.

## How It Works

Episode Poster Generator scans each episode file, evaluates multiple frames, and selects a strong candidate while avoiding fades, black screens, and letterboxed shots. The selected frame is turned into a poster and optionally styled with configurable text such as episode title or numbering.

## Poster Styles

### Standard Style
Simple screenshot with optional season/episode information.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Standard Example 1](docs/examples/Standard/Example1.png) | ![Standard Example 2](docs/examples/Standard/Example2.png) | ![Standard Example 3](docs/examples/Standard/Example3.png) |

### Brush Style  
Flat color with a transparent brush cutout revealing the screenshot beneath, with optional season/episode information to the side.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Brush Example 1](docs/examples/StandardBrush/Example1.png) | ![Brush Example 2](docs/examples/StandardBrush/Example2.png) | ![Brush Example 3](docs/examples/StandardBrush/Example3.png) |

### Cutout Style  
Large episode numbers displayed as transparent cutouts revealing the screenshot beneath, with optional episode title.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Cutout Example 1](docs/examples/Cutout/Example1.png) | ![Cutout Example 2](docs/examples/Cutout/Example2.png) | ![Cutout Example 3](docs/examples/Cutout/Example3.png) |

**Cutout Types:**
- **Code**: Displays episode in format "S01E03" 
- **Text**: Displays episode number as words (e.g., "THREE")

### Frame Style
Decorative frame borders with episode title and optional season/episode information.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Frame Example 1](docs/examples/Frame/Example1.png) | ![Frame Example 2](docs/examples/Frame/Example2.png) | ![Frame Example 3](docs/examples/Frame/Example3.png) |

### Logo Style
Series logo focused posters with optional season/episode information.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Logo Example 1](docs/examples/Logo/Example1.png) | ![Logo Example 2](docs/examples/Logo/Example2.png) | ![Logo Example 3](docs/examples/Logo/Example3.png) |

### Numeral Style
Roman numeral episode numbers with optional overlapping title.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Numeral Example 1](docs/examples/NumeralFull/Example1.png) | ![Numeral Example 2](docs/examples/NumeralFull/Example2.png) | ![Numeral Example 3](docs/examples/NumeralFull/Example3.png) |

### Split Style
Episode screenshot with overlay text and episode information, split alongside the series poster.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Split Example 1](docs/examples/Split/Example1.png) | ![Split Example 2](docs/examples/Split/Example2.png) | ![Split Example 3](docs/examples/Split/Example3.png) |

### Frosted Glass Style
Episode information on a frosted glass panel that blurs the screenshot behind it.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Frosted Glass Example 1](docs/examples/FrostedGlass/Example1.png) | ![Frosted Glass Example 2](docs/examples/FrostedGlass/Example2.png) | ![Frosted Glass Example 3](docs/examples/FrostedGlass/Example3.png) |

### Fade Style
One sided color fade with a large episode number and a vertical episode title.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Fade Example 1](docs/examples/Fade/Example1.png) | ![Fade Example 2](docs/examples/Fade/Example2.png) | ![Fade Example 3](docs/examples/Fade/Example3.png) |

### Striped Style
Tilted color sash with pinstripes carrying the episode title, with season/episode information in the corner.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Striped Example 1](docs/examples/Striped/Example1.png) | ![Striped Example 2](docs/examples/Striped/Example2.png) | ![Striped Example 3](docs/examples/Striped/Example3.png) |

### Timeline Style
Season progress bar filled to the episode's position, with optional episode title and season/episode information.

| Example 1 | Example 2 | Example 3 |
|-----------|-----------|-----------|
| ![Timeline Example 1](docs/examples/Timeline/Example1.png) | ![Timeline Example 2](docs/examples/Timeline/Example2.png) | ![Timeline Example 3](docs/examples/Timeline/Example3.png) |

### Palette Derived Colors
Overlay colors sampled from the dominant color of each episode's frame instead of a fixed color. Works with every style. The configured alpha values are preserved.

| Brush | Cutout | Fade |
|-------|--------|------|
| ![Palette Brush](docs/examples/BrushPalette/Example7.png) | ![Palette Cutout](docs/examples/CutoutPalette/Example7.png) | ![Palette Fade](docs/examples/Fade/Example7.png) |

## Poster Architecture

The Episode Poster Generator uses a four layer rendering pipeline to create consistent posters across all styles:

### Layer 1: Canvas (Base Layer)
The foundation layer that provides the visual background for the poster.

**Options:**

- **Video Frame Extraction**: Automatically extracts a frame from the episode video file using configurable extraction windows. Candidates are sampled at widely spaced points across the window until one with adequate brightness and sharpness is found; the best scoring candidate is used as a fallback. The sampling starts from a different point on each run, so refreshing an episode offers a different frame rather than the same one.
- **Series Backdrop**: Uses the series' own backdrop image as the poster background, falling back to a transparent canvas when the series has none.
- **Transparent Background**: Creates a solid color or transparent canvas.

**Processing:**
- HDR brightening for HDR content
- Letterbox/pillarbox detection and cropping
- Aspect ratio adjustments and fill strategies

### Layer 2: Overlay (Color Tinting)
A translucent color layer applied over the canvas to enhance text readability and create visual cohesion.

**Features:**
- Configurable ARGB hex colors with alpha transparency
- Applied uniformly across the entire poster surface or use two colors blurred together
- Optional palette derived colors: the overlay color is sampled per episode from the dominant color of the frame, while the configured alpha is preserved

### Layer 3: Graphics (Static Images)
Optional static graphic overlays positioned above the canvas but below text elements.

**Capabilities:**
- User configurable file path for custom graphics
- Automatic sizing and positioning within safe area boundaries
- Supports PNG, JPG, and WEBP formats
- Maintains aspect ratio while fitting within poster constraints

### Layer 4: Typography (Text and Logos)
The top layer containing all text elements, episode information, and series logos.

**Elements:**
- Episode numbers and season information
- Episode titles with automatic text wrapping, and configurable handling for titles too long to fit
- Series logos with configurable positioning
- Style specific typography (Roman numerals, cutout text, etc.)
- Drop shadows and contrasting outlines for enhanced readability

### Rendering Pipeline
Each poster style follows this exact four layer sequence. The modular approach allows for easy customization and additional poster styles.

Elements that stack vertically — logo, episode code, title — are measured before any of them are placed, so they keep a consistent gap and cannot overlap. That gap is the **Element Spacing** setting, applied the same way by every style.

## Usage & Documentation

### Settings
For an explanation of the settings, visit [SETTINGS.md](docs/SETTINGS.md).

### Template examples & downloads
For additional template examples and downloadable configurations, visit [EXAMPLES.md](docs/EXAMPLES.md).

### Preview your poster
A live preview at the top of the Posters page renders your current settings against sample artwork and updates as you change them. Click the preview to enlarge it, or click a component thumbnail to see the artwork feeding it.
![Preview Modal](Jellyfin.Plugin.EpisodePosterGenerator/Assets/Preview-Modal.png)

---

## Versioning

Releases use a four part version, `JJ.JJ.F.B`, that matches the supported Jellyfin version with the plugin's own feature/bug count:

```
12.0.1.2
└──┘ └┬┘
 │    └── 1 = Plugin feature release
 │        2 = Plugin bug/patch release within that feature
 │
 └─── 12.0 = Jellyfin version this build was tested/released for
```

## Installation

### Step 1: Add Plugin Repository

* Open Jellyfin and navigate to Dashboard → Plugins → Repositories
* Click Add Repository
* Enter the following repository URL: `https://raw.githubusercontent.com/JPKribs/jellyfin-plugin-episodepostergenerator/master/manifest.json`
* Click Save

### Step 2: Install Plugin

* Go to the Catalog tab in the Plugins section
* Find Episode Poster Generator in the catalog
* Click Install
* Wait for installation to complete

### Step 3: Restart Jellyfin

* Restart your Jellyfin server completely
* Wait for Jellyfin to fully start up

### Verification Check

* After restart, navigate to Dashboard → Plugins → Episode Poster Generator to confirm the plugin configuration page loads properly.

---

## AI Disclaimer

Claude Code was utilized in the initial structure of this project and first drafts of documentation. All code has been manually reviewed, tested, and revised after its generation. This disclaimer exists in the interest of transparency.

**All code was written, or code reviewed and tested, by humans.**
