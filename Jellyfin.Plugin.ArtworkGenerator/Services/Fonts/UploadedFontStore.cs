using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JPKribs.Jellyfin.Base;
using SkiaSharp;

[assembly: SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Scope = "member", Target = "~M:JPKribs.Jellyfin.Base.AtomicFile.WriteAllBytes(System.String,System.ReadOnlySpan{System.Byte},System.Boolean,System.Boolean,System.Action{System.String},Microsoft.Extensions.Logging.ILogger)", Justification = "UploadedFontStore.Upload passes a name made safe by FileNameSanitizer and checked inside the fonts folder by PathContainment.")]
[assembly: SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Scope = "member", Target = "~M:JPKribs.Jellyfin.Base.AtomicFile.TryDeleteTemp(System.String,Microsoft.Extensions.Logging.ILogger)", Justification = "The temp file sits beside the font UploadedFontStore.Upload checked inside the fonts folder.")]

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// The fonts folder in the plugin's data folder. It lists the .ttf and .otf files there, and uploads and deletes them.
/// </summary>
public sealed class UploadedFontStore
{
    /// <summary>
    /// The most bytes one uploaded font may have.
    /// </summary>
    public const int MaxFontBytes = 20 * 1024 * 1024;

    private static readonly string[] Extensions = { ".ttf", ".otf" };

    private readonly string _folder;
    private readonly object _lock = new();
    private readonly Dictionary<string, CachedFace> _cache = new(StringComparer.Ordinal);
    private IReadOnlyList<FontFace>? _faces;
    private DateTime _scannedFolderTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadedFontStore"/> class.
    /// </summary>
    /// <param name="folder">The fonts folder. It is created on the first upload when missing.</param>
    public UploadedFontStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = Path.GetFullPath(folder);
    }

    /// <summary>
    /// Gets the full path of the fonts folder.
    /// </summary>
    public string Folder => _folder;

    /// <summary>
    /// Lists the fonts in the folder, ordered by family and style. A file Skia cannot read is left out.
    /// </summary>
    /// <returns>The loaded faces.</returns>
    public IReadOnlyList<FontFace> List()
    {
        lock (_lock)
        {
            var folderTime = Directory.Exists(_folder) ? Directory.GetLastWriteTimeUtc(_folder) : DateTime.MinValue;

            // A file copied into the folder by hand changes its time, so the folder is scanned again.
            if (_faces == null || folderTime != _scannedFolderTime)
            {
                _faces = Scan();
                _scannedFolderTime = folderTime;
            }

            return _faces;
        }
    }

    /// <summary>
    /// Checks a font and writes it to the folder, replacing a file of the same name.
    /// </summary>
    /// <param name="fileName">The name the file was uploaded with.</param>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The stored face, or the translation key of a sentence saying why the font was refused.</returns>
    public FontUploadResult Upload(string? fileName, byte[]? data)
    {
        if (string.IsNullOrWhiteSpace(fileName) || data == null || data.Length == 0)
        {
            return FontUploadResult.Refused(TranslationKeys.ErrorSelectFontFile);
        }

        var safeName = FileNameSanitizer.Sanitize(Path.GetFileName(fileName.Replace('\\', '/')));
        if (!HasFontExtension(safeName))
        {
            return FontUploadResult.Refused(TranslationKeys.ErrorFontExtension);
        }

        if (data.Length > MaxFontBytes)
        {
            return FontUploadResult.Refused(TranslationKeys.ErrorFontTooLarge);
        }

        if (!HasFontHeader(data))
        {
            return FontUploadResult.Refused(TranslationKeys.ErrorNotAFont);
        }

        var path = Path.Combine(_folder, safeName);
        if (!PathContainment.IsStrictlyUnder(path, _folder))
        {
            return FontUploadResult.Refused(TranslationKeys.ErrorFontName);
        }

        using (var check = Load(data))
        {
            if (check == null)
            {
                return FontUploadResult.Refused(TranslationKeys.ErrorFontUnreadable);
            }
        }

        lock (_lock)
        {
            Directory.CreateDirectory(_folder);
            AtomicFile.WriteAllBytes(path, data);
            _cache.Remove(path);
            _faces = null;
        }

        var face = List().FirstOrDefault(f => string.Equals(f.FileName, safeName, StringComparison.Ordinal));
        return face == null
            ? FontUploadResult.Refused(TranslationKeys.ErrorFontUnreadable)
            : FontUploadResult.Stored(face);
    }

    /// <summary>
    /// Deletes one font from the folder.
    /// </summary>
    /// <param name="fileName">The file name, as the list gives it.</param>
    /// <returns><c>true</c> when the font was there and is deleted.</returns>
    public bool Delete(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (!HasFontExtension(name) || !Directory.Exists(_folder))
        {
            return false;
        }

        lock (_lock)
        {
            // The path comes from listing the folder, so a name can only pick a font already in it.
            var path = Directory.EnumerateFiles(_folder)
                .FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.Ordinal));
            if (path == null || !PathContainment.IsStrictlyUnder(path, _folder))
            {
                return false;
            }

            File.Delete(path);

            // The face is dropped but not disposed, since a render running now may still be drawing with it.
            _cache.Remove(path);
            _faces = null;
            return true;
        }
    }

    /// <summary>
    /// Checks the first four bytes for a TrueType or OpenType font, 00 01 00 00, true, or OTTO.
    /// </summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns><c>true</c> when the header is a font's.</returns>
    public static bool HasFontHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return false;
        }

        var head = data[..4];
        return head.SequenceEqual(new byte[] { 0x00, 0x01, 0x00, 0x00 })
            || head.SequenceEqual("true"u8)
            || head.SequenceEqual("OTTO"u8);
    }

    private static bool HasFontExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static SKTypeface? Load(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        return SKTypeface.FromData(skData);
    }

    // Scan
    // Loads every font file in the folder, reusing a face whose file has not changed since it was read.
    private List<FontFace> Scan()
    {
        var faces = new List<FontFace>();
        if (!Directory.Exists(_folder))
        {
            _cache.Clear();
            return faces;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(_folder).Where(p => HasFontExtension(p)))
        {
            seen.Add(path);
            var info = new FileInfo(path);
            if (!_cache.TryGetValue(path, out var cached) || cached.Length != info.Length || cached.WriteTime != info.LastWriteTimeUtc)
            {
                var typeface = TryLoadFile(path);
                cached = new CachedFace(info.Length, info.LastWriteTimeUtc, typeface == null ? null : FontFace.From(typeface, FontSource.Uploaded, info.Name));
                _cache[path] = cached;
            }

            if (cached.Face != null)
            {
                faces.Add(cached.Face);
            }
        }

        foreach (var gone in _cache.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _cache.Remove(gone);
        }

        return faces
            .OrderBy(face => face.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(face => face.Weight)
            .ThenBy(face => face.Italic)
            .ToList();
    }

    private static SKTypeface? TryLoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record CachedFace(long Length, DateTime WriteTime, FontFace? Face);
}
