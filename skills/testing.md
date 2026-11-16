# Testing

How to test this plugin, from a single method to a page on a test server. A change is done when the tests for every layer it touches pass.

## The Rule

Every source file has tests, and every test proves that given inputs produce the expected output. A bug fix starts with a test that fails without the fix.

## Layers

Test each behavior at the lowest layer that can prove it. Go up a layer only for what the lower one cannot reach.

| Layer | Proves | Runs | Time |
| :--- | :--- | :--- | :--- |
| Unit | One class or function gives the right output for each input | `dotnet test` | Seconds |
| Render | A generator, composer, or frame scorer draws or ranks pixels as it should, on bitmaps built in the test | `dotnet test` | Seconds |
| Docs and templates | Every setting and style is in the docs, every example template loads, and nothing is spelled the British way | `dotnet test` | Seconds |
| Demo render | Every template in `docs/examples/` still renders end to end | `dotnet run` in the Demo project | Minutes |
| Page | A config page loads, saves, and shows its results | A test server | Minutes |

| A change to | Needs |
| :--- | :--- |
| A utility, model, rule, or setting | Unit |
| A poster style, the canvas, the overlay, text, or logo drawing | Render, then the demo render |
| A setting's label, a style, or an example template | Docs and templates |
| A config page script, its markup, or its CSS | Page |
| The Base version | Every layer |

## Layout

The tests sit in one flat folder, `Jellyfin.Plugin.ArtworkGenerator.Tests/`, one file per subject.

| Source | Test |
| :--- | :--- |
| `Jellyfin.Plugin.ArtworkGenerator/Utilities/GridComposer.cs` | `Jellyfin.Plugin.ArtworkGenerator.Tests/GridComposerTests.cs` |
| `Jellyfin.Plugin.ArtworkGenerator/Services/Artwork/LogoDesignStore.cs` | `Jellyfin.Plugin.ArtworkGenerator.Tests/LogoDesignStoreTests.cs` |
| `docs/SETTINGS.md` | `Jellyfin.Plugin.ArtworkGenerator.Tests/DocumentationTests.cs` |

## C# Tests

Tests use xUnit with plain `Assert`. The test project references NSubstitute, and no test uses it. Write a fake by hand when a test needs one.

```csharp
/// <summary>
/// The cells share the space evenly, so a grid reads as a grid rather than as one large frame
/// beside some small ones.
/// </summary>
[Fact]
public void TheCellsAreEvenlySized()
{
    var frames = Frames(4);

    ...
}
```

### Naming and Comments

* Name the class `<Subject>Tests` and each method as a sentence about the behavior, such as `EveryFrameIsDrawn`.
* Give the class a summary of what its subject must guarantee.
* Give each test a summary of what it proves and why it matters.

### Writing a Test

* Arrange, act, and assert, separated by blank lines.
* Assert one behavior per test. Several asserts are fine when they check one outcome.
* Use `[Theory]` with `[InlineData]` for one rule over several inputs.
* Test the edges. Null, empty, the largest value, a cancelled token, a missing file, and a damaged file.
* Build bitmaps in the test with SkiaSharp and dispose them. Never read a video or a frame from disk.
* Use `LogoDesignStore`'s in memory constructor, or a temp folder deleted in `Dispose`, for anything that stores designs.
* Never sleep. Pass a clock or a delay in, and control it from the test.
* A test that fails once in many runs has found a race. Fix the source or the test.
* A test never reaches the network or a Jellyfin server.

```bash
dotnet test
```

## Page Scripts

There is no JavaScript test suite. Check every changed page script parses, then load the page.

```bash
for f in Jellyfin.Plugin.ArtworkGenerator/Configuration/*.js; do node --check "$f"; done
```

* Confirm every name a page imports from `ag_shared.js` is exported there, and every name `ag_shared.js` imports is in Base's typings, `assets/jpkribs_shared.d.ts` in the package.
* Install the build on a test server, per [documentation.md](documentation.md#installing-a-build-by-hand), and open each changed tab. Reload the page after installing, since the dashboard caches scripts.
* On each tab, change a field, save, reload, and check the value stayed. Check the browser console shows no errors.

### Harness

A page can be checked in a harness before it goes to a test server, and the docs screenshots come from one, the kit in [`tools/screenshots/`](../tools/screenshots/README.md). A harness proves little unless it renders the way the dashboard does.

* Load Jellyfin's real stylesheets from its `jellyfin-web` folder. Load the two `index.html` links first, then the chunk stylesheets, then `themes/dark/theme.css`. Leave out the book player chunk, which sets the root font size to 82 percent, and the TV layout chunk, since the dashboard loads neither. Base's CSS alone hides real bugs.
* Mimic the upgrades Jellyfin's `emby-input`, `emby-select`, `emby-checkbox`, and `emby-button` elements make when they attach, including the label and arrow they insert.
* Answer the plugin's routes with the plugin's own code, such as `SettingOptions`, `PreviewService`, and `FontCatalog`, so labels and previews are real. Stub only Jellyfin's own calls, with generic data.
* Click through like a user. Type real keystrokes into every picker, open every tab, every collapsible, and every dialog, at 1280 and 375 px wide, and look at each screenshot.
* Type part of a real name into every lookup field, such as **Assigned Items**, and check the list finds it.
* Never call work done from a harness alone. Install the build on a test server and check every changed tab there after a hard reload.

## Demo Render

The Demo project renders every template in `docs/examples/` with the plugin's own generators and writes the images beside them. The **Publish Plugin** workflow runs it for every release.

```bash
cd Jellyfin.Plugin.ArtworkGenerator.Demo && dotnet run
```

* Run it after any change to how a style draws, and look at the images that changed.
* Every template names bundled fonts only, so the images match on any server and in the workflow. Never name a font a server may lack.
* Run it again whenever a template or the bundled fonts change, since the docs show these images as the plugin's output.
* Commit the images only when the change meant to alter them.

## Checklist

* [ ] Every changed source file has tests.
* [ ] A bug fix has a test that fails without it.
* [ ] Edges are covered, including null, empty, cancelled, missing, and damaged.
* [ ] `dotnet test` passes.
* [ ] Every changed page script passes `node --check`, and its page was opened on a test server.
* [ ] The demo render ran for any change to how a style draws.
