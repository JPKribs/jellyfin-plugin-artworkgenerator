// Screenshot server: serves the plugin pages, Base, jellyfin-web CSS and the harness, and answers the
// plugin's read-only routes with the real plugin code.
// Run from tools/screenshots: dotnet run --project server. It listens on 127.0.0.1:8849.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

var here = KitFolder();
var repo = Path.GetFullPath(Path.Combine(here, "..", ".."));
var plugin = Path.Combine(repo, "Jellyfin.Plugin.ArtworkGenerator");
var conf = Path.Combine(plugin, "Web");
var baseDir = BaseAssets(Path.Combine(plugin, "Jellyfin.Plugin.ArtworkGenerator.csproj"));
var web = Environment.GetEnvironmentVariable("JELLYFIN_WEB") is { Length: > 0 } w ? w : "/Applications/Jellyfin.app/Contents/Resources/jellyfin-web";
if (!File.Exists(Path.Combine(web, "index.html"))) Fail($"jellyfin-web not found at {web}. Set JELLYFIN_WEB to a jellyfin-web folder.");
var store = new UploadedFontStore(SampleFonts());
FontUtils.SetCatalog(new FontCatalog(store, () => BundledFonts.Faces, () => new[] { "DejaVu Sans", "DejaVu Serif", "Liberation Sans" }));
var preview = new PreviewService(NullLoggerFactory.Instance, Path.Combine(plugin, "Assets", "Demo"));
var page = File.ReadAllText(Path.Combine(here, "www", "index.html")).Replace("<!--STYLESHEETS-->", string.Join("\n", Stylesheets().Select(n => $"<link rel=\"stylesheet\" href=\"/jfweb/{n}\">")));
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
var listener = new HttpListener();
listener.Prefixes.Add("http://127.0.0.1:8849/");
listener.Start();
Console.WriteLine("listening");
while (true)
{
    var ctx = listener.GetContext();
    _ = Task.Run(() => Handle(ctx));
}

void Send(HttpListenerContext c, byte[] data, string type, int status = 200)
{
    c.Response.StatusCode = status; c.Response.ContentType = type; c.Response.Headers["Cache-Control"] = "no-store";
    c.Response.OutputStream.Write(data); c.Response.Close();
}
void SendJson(HttpListenerContext c, object o) => Send(c, JsonSerializer.SerializeToUtf8Bytes(o, json), "application/json");
string TypeOf(string p) => Path.GetExtension(p) switch { ".js" or ".mjs" => "text/javascript", ".css" => "text/css", ".html" => "text/html", ".png" => "image/png", ".jpg" => "image/jpeg", ".svg" => "image/svg+xml", ".woff2" => "font/woff2", ".woff" => "font/woff", _ => "application/octet-stream" };

void Handle(HttpListenerContext c)
{
    try
    {
        var path = c.Request.Url!.AbsolutePath;
        var q = c.Request.QueryString;
        string body = "";
        if (c.Request.HasEntityBody) using (var r = new StreamReader(c.Request.InputStream)) body = r.ReadToEnd();
        if (path.EndsWith("/configurationpage"))
        {
            var n = q["name"]!;
            string f = n.Contains("jpkribs_shared") ? Path.Combine(baseDir, "jpkribs_shared" + Path.GetExtension(n)) : Path.Combine(conf, n.Contains('.') ? n : n + ".html");
            Send(c, File.ReadAllBytes(f), TypeOf(f)); return;
        }
        if (path.StartsWith("/jfweb/")) { var f = Path.Combine(web, Uri.UnescapeDataString(path[7..])); Send(c, File.ReadAllBytes(f), TypeOf(f)); return; }
        if (path == "/config.json") { SendJson(c, BuildConfig()); return; }
        if (path == "/logos.json") { SendJson(c, BuildLogos()); return; }
        if (path.StartsWith("/Plugins/ArtworkGenerator/"))
        {
            var route = path["/Plugins/ArtworkGenerator/".Length..];
            switch (route)
            {
                case "SettingOptions":
                    SendJson(c, new { options = SettingOptions.All(), defaults = SettingOptions.Defaults(), text = SettingOptions.Text(), backdropText = SettingOptions.BackdropText(), frameText = SettingOptions.FrameExtractionText(), frameOptions = SettingOptions.FrameExtractionOptions(), logoDefaults = SettingOptions.LogoDefaults() });
                    return;
                case "PosterStyles":
                    SendJson(c, PreviewService.GetStyleCatalog().Select(g => new { value = g.Style.ToString(), description = g.Description, primaryDescription = g.PrimaryDescription, secondaryDescription = g.SecondaryDescription, portrait = g.Supports(ArtworkShape.Portrait), landscape = g.Supports(ArtworkShape.Landscape), settings = g.SettingRules.ToDictionary(r => r.Key, r => r.Value.ToString()), text = g.SettingText }));
                    return;
                case "Fonts":
                    SendJson(c, FontUtils.GetFontFamilies().Select(e => new { family = e.Family, source = e.Source.ToString() }));
                    return;
                case "Fonts/Uploaded":
                    SendJson(c, store.List().Select(f => new { fileName = f.FileName, family = f.Family, style = f.Style }));
                    return;
                case "Preview":
                {
                    var s = JsonSerializer.Deserialize<PosterSettings>(body, json)!;
                    var kind = Enum.Parse<ArtworkItemKind>(q["kind"] ?? "Series");
                    var shape = Enum.Parse<ArtworkShape>(q["shape"] ?? "Landscape");
                    Send(c, preview.GeneratePreview(s, kind, shape)!, "image/jpeg"); return;
                }
                case "Preview/Logo":
                {
                    var s = JsonSerializer.Deserialize<LogoSettings>(body, json)!;
                    Send(c, preview.GenerateLogoPreview(s, q["name"])!, "image/png"); return;
                }
            }
            if (route.StartsWith("Preview/Component/"))
            {
                var r = preview.GetComponentImage(route["Preview/Component/".Length..]);
                if (r == null) { Send(c, Array.Empty<byte>(), "text/plain", 404); return; }
                Send(c, r.Value.Bytes, r.Value.ContentType); return;
            }
            Send(c, Encoding.UTF8.GetBytes("unknown " + route), "text/plain", 404); return;
        }
        if (path is "/" or "/index.html") { Send(c, Encoding.UTF8.GetBytes(page), "text/html"); return; }
        var file = Path.Combine(here, "www", path.TrimStart('/'));
        if (File.Exists(file)) { Send(c, File.ReadAllBytes(file), TypeOf(file)); return; }
        Send(c, Encoding.UTF8.GetBytes("not found " + path), "text/plain", 404);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        try { Send(c, Encoding.UTF8.GetBytes(ex.Message), "text/plain", 500); } catch { }
    }
}

static string KitFolder([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, ".."));

static void Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}

// Base's assets from the NuGet cache, at the version the plugin's csproj references.
static string BaseAssets(string csproj)
{
    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(csproj), "Include=\"JPKribs\\.Jellyfin\\.Base\"\\s+Version=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (!m.Success) Fail($"No JPKribs.Jellyfin.Base PackageReference in {csproj}.");
    var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } n ? n : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    var dir = Path.Combine(root, "jpkribs.jellyfin.base", m.Groups[1].Value, "assets");
    if (!Directory.Exists(dir)) Fail($"Base {m.Groups[1].Value} not found at {dir}. Run dotnet restore on the plugin first.");
    return dir;
}

// The uploaded fonts the Fonts shot lists: Georgia and Georgia Bold, copied from macOS's own fonts (or SAMPLE_FONTS)
// into a temporary folder, since their license keeps them out of the repo.
static string SampleFonts()
{
    var from = Environment.GetEnvironmentVariable("SAMPLE_FONTS") is { Length: > 0 } f ? f : "/System/Library/Fonts/Supplemental";
    var to = Path.Combine(Path.GetTempPath(), "ag-screenshot-fonts");
    Directory.CreateDirectory(to);
    foreach (var name in new[] { "Georgia.ttf", "Georgia Bold.ttf" })
    {
        var src = Path.Combine(from, name);
        if (!File.Exists(src)) Fail($"{name} not found in {from}. Set SAMPLE_FONTS to a folder that holds Georgia.ttf and Georgia Bold.ttf.");
        File.Copy(src, Path.Combine(to, name), true);
    }
    return to;
}

// jellyfin-web's stylesheets: index.html's own first, then every other chunk by name, then the dark theme. A chunk
// that sets the root font to anything but the dashboard's 93% belongs to another layout, such as TV, and is left out.
IEnumerable<string> Stylesheets()
{
    var first = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(web, "index.html")), "href=\"([^\"?]+\\.css)").Select(x => x.Groups[1].Value).ToList();
    var rest = Directory.GetFiles(web, "*.css").Select(Path.GetFileName).Select(x => x!).Where(x => !first.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).Where(x =>
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(web, x)), "html\\{[^}]*?font-size:([^;}!]+)").All(s => s.Groups[1].Value.Trim() == "93%"));
    return first.Concat(rest).Append("themes/dark/theme.css");
}

static object BuildConfig()
{
    var def = new Guid("11111111-1111-1111-1111-111111111111");
    var bold = new Guid("22222222-2222-2222-2222-222222222222");
    var logo = new Guid("33333333-3333-3333-3333-333333333333");
    var cfg = new Jellyfin.Plugin.ArtworkGenerator.Configuration.PluginConfiguration();
    cfg.PosterConfigurations.Add(new PosterConfiguration { Id = def, Name = "Default", IsDefault = true, Settings = new PosterSettings { PrimaryFontFamily = "Montserrat", PrimaryFontStyle = "Bold", SecondaryFontFamily = "Montserrat", SecondaryFontStyle = "Bold" } });
    cfg.PosterConfigurations.Add(new PosterConfiguration { Id = bold, Name = "Bold Title", Settings = new PosterSettings { PosterStyle = PosterStyle.Brush, PrimaryFontFamily = "Bebas Neue", PrimaryFontStyle = "Normal", PrimaryFontSize = 14, CutoutBorder = false, OverlayColor = "#F0141414", SecondaryFontFamily = "Oswald", SecondaryFontStyle = "Bold" } });
    Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkProfile Make(string id, string name, bool isDefault)
    {
        var p = new Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkProfile { Id = new Guid(id), Name = name, IsDefault = isDefault };
        p.Slots.Clear();
        foreach (var (kind, slot) in Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkProfile.SupportedSlots)
        {
            var on = kind is ArtworkItemKind.Series or ArtworkItemKind.Season or ArtworkItemKind.Episode or ArtworkItemKind.Movie;
            if (slot == Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkSlot.Backdrop) on = kind == ArtworkItemKind.Series;
            if (slot == Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkSlot.Thumb) on = kind is ArtworkItemKind.Series or ArtworkItemKind.Season;
            if (slot == Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkSlot.Logo) on = kind == ArtworkItemKind.Series;
            var design = slot == Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkSlot.Logo ? logo : (kind == ArtworkItemKind.Episode ? bold : def);
            var second = slot == Jellyfin.Plugin.ArtworkGenerator.Models.ArtworkSlot.Primary && kind == ArtworkItemKind.Series ? bold : Guid.Empty;
            p.Slots.Add(new Jellyfin.Plugin.ArtworkGenerator.Models.SlotAssignment { Kind = kind, Slot = slot, Enabled = on, DesignId = design, SecondaryDesignId = second });
        }
        return p;
    }
    cfg.Profiles.Add(Make("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Default", true));
    var anime = Make("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Anime", false);
    anime.SeriesIds.Add(new Guid("5e000000-0000-0000-0000-000000000001"));
    anime.SeriesIds.Add(new Guid("5e000000-0000-0000-0000-000000000002"));
    cfg.Profiles.Add(anime);
    return cfg;
}

static object BuildLogos()
{
    return new[]
    {
        new Jellyfin.Plugin.ArtworkGenerator.Models.LogoConfiguration { Id = new Guid("33333333-3333-3333-3333-333333333333"), Name = "Default", Settings = new LogoSettings { FontFamily = "Oswald", FontStyle = "Bold" } },
        new Jellyfin.Plugin.ArtworkGenerator.Models.LogoConfiguration { Id = new Guid("44444444-4444-4444-4444-444444444444"), Name = "Serif", Settings = new LogoSettings { FontFamily = "Playfair Display", FontStyle = "Bold Italic" } }
    };
}
