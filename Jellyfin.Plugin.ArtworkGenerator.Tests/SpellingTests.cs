using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The project is written in US English, labels and comments included. British spellings have crept
/// back in twice by hand, so this holds the line rather than leaving it to whoever is typing.
/// </summary>
public class SpellingTests
{
    private static readonly string[] Forms =
    {
        "colour", "centre", "grey", "behaviour", "favour", "penalis",
        "normalis", "organis", "recognis", "initialis", "analyse", "marshalled"
    };

    private static readonly string[] Extensions = { ".cs", ".js", ".html", ".css", ".md", ".json" };

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "README.md")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate the repository root above the test assembly.");
    }

    [Fact]
    public void NothingIsSpelledTheBritishWay()
    {
        var root = RepositoryRoot();
        var pattern = new Regex(string.Join("|", Forms), RegexOptions.IgnoreCase);
        var offences = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file);
            if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.StartsWith(".git", StringComparison.Ordinal)
                || relative.Contains("SpellingTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = pattern.Match(lines[i]);
                if (match.Success)
                {
                    offences.Add($"{relative}:{i + 1} \"{match.Value}\"");
                }
            }
        }

        Assert.True(offences.Count == 0, "British spellings found:\n  " + string.Join("\n  ", offences.Take(20)));
    }
}
