using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FontHarbor;

public sealed partial class IllustratorScanner
{
    private const int ChunkSize = 1024 * 1024;
    private const int Overlap = 2048;

    [GeneratedRegex(@"/(?:BaseFont|FontName)\s*/(?<name>[A-Za-z0-9_.+#\-]+)", RegexOptions.Compiled)]
    private static partial Regex PdfFontRegex();
    [GeneratedRegex(@"/FontFamily\s*\((?<name>(?:\\.|[^)])+)\)", RegexOptions.Compiled)]
    private static partial Regex FontFamilyRegex();
    [GeneratedRegex(@"%%DocumentFonts:\s*(?<name>[^\r\n]*(?:\r?\n%%\+\s*[^\r\n]*)*)", RegexOptions.Compiled)]
    private static partial Regex DocumentFontsRegex();
    [GeneratedRegex(@"<stFnt:(?:fontName|fontFamily|postScriptName)>(?<name>[^<]+)</stFnt:", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex XmpFontRegex();
    [GeneratedRegex(@"^[A-Z]{6}\+")]
    private static partial Regex PdfSubsetPrefixRegex();
    [GeneratedRegex(@"#(?<hex>[0-9A-Fa-f]{2})")]
    private static partial Regex PdfHexRegex();

    public IllustratorScanResult Scan(string root, IReadOnlyList<FontItem> installedFonts, IProgress<(int Done, int Total, string File)>? progress = null, CancellationToken cancellationToken = default)
    {
        return Scan(new[] { root }, installedFonts, progress, cancellationToken);
    }

    public IllustratorScanResult Scan(IReadOnlyList<string> roots, IReadOnlyList<FontItem> installedFonts, IProgress<(int Done, int Total, string File)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (roots.Count == 0 || roots.Any(root => !Directory.Exists(root))) throw new DirectoryNotFoundException("扫描目录不存在。");
        var usage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        List<string> files;
        try { files = roots.SelectMany(root => EnumerateAiFiles(Path.GetFullPath(root), cancellationToken)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (OperationCanceledException) { return BuildResult(usage, installedFonts, 0, 0, true); }
        var scanned = 0;
        for (var i = 0; i < files.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) return BuildResult(usage, installedFonts, files.Count, scanned, true);
            var file = files[i];
            progress?.Report((i, files.Count, file));
            HashSet<string> names;
            try { names = ExtractFonts(file, cancellationToken); }
            catch (OperationCanceledException) { return BuildResult(usage, installedFonts, files.Count, scanned, true); }
            foreach (var name in names)
            {
                if (!usage.TryGetValue(name, out var references)) usage[name] = references = new(StringComparer.OrdinalIgnoreCase);
                references.Add(file);
            }
            scanned++;
        }
        progress?.Report((files.Count, files.Count, ""));
        return BuildResult(usage, installedFonts, files.Count, scanned, false);
    }

    private static IllustratorScanResult BuildResult(Dictionary<string, HashSet<string>> usage, IReadOnlyList<FontItem> installedFonts, int discovered, int scanned, bool cancelled)
    {
        var fonts = usage.Select(pair => new IllustratorFontUsage
        {
            FontName = pair.Key,
            Files = pair.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            MatchedFont = MatchInstalledFont(pair.Key, installedFonts)
        }).OrderByDescending(x => x.FileCount).ThenBy(x => x.FontName).ToList();
        return new IllustratorScanResult { Fonts = fonts, DiscoveredFileCount = discovered, ScannedFileCount = scanned, Cancelled = cancelled };
    }

    private static IEnumerable<string> EnumerateAiFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            string[] files = []; string[] folders = [];
            try { files = Directory.GetFiles(folder, "*.ai", SearchOption.TopDirectoryOnly); } catch (UnauthorizedAccessException) { } catch (IOException) { }
            foreach (var file in files) yield return file;
            try { folders = Directory.GetDirectories(folder); } catch (UnauthorizedAccessException) { } catch (IOException) { }
            foreach (var child in folders) { try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }

    private static HashSet<string> ExtractFonts(string path, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 严格只读：扫描器不包含任何写入、移动、重命名或删除扫描文件的代码路径。
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[ChunkSize];
        var tail = "";
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = tail + Encoding.Latin1.GetString(buffer, 0, count);
            CollectMatches(text, result);
            tail = text.Length > Overlap ? text[^Overlap..] : text;
        }
        return result;
    }

    private static void CollectMatches(string text, HashSet<string> result)
    {
        foreach (Match match in PdfFontRegex().Matches(text)) AddCleanName(match.Groups["name"].Value, result);
        foreach (Match match in FontFamilyRegex().Matches(text)) AddCleanName(match.Groups["name"].Value, result);
        foreach (Match match in DocumentFontsRegex().Matches(text))
            foreach (var name in match.Groups["name"].Value.Replace("%%+", " ").Split([' ', '\t', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) AddCleanName(name, result);
        foreach (Match match in XmpFontRegex().Matches(text)) AddCleanName(WebUtility.HtmlDecode(match.Groups["name"].Value), result);
    }

    private static void AddCleanName(string raw, HashSet<string> result)
    {
        var name = PdfHexRegex().Replace(raw.Trim().Trim('/', '(', ')'), m => ((char)Convert.ToByte(m.Groups["hex"].Value, 16)).ToString());
        name = PdfSubsetPrefixRegex().Replace(name, "").Replace("\\(", "(").Replace("\\)", ")");
        if (name.Length >= 2 && name.Length <= 160 && !name.Equals("None", StringComparison.OrdinalIgnoreCase)) result.Add(name);
    }

    private static FontItem? MatchInstalledFont(string illustratorName, IReadOnlyList<FontItem> fonts)
    {
        var full = Normalize(illustratorName, false);
        var family = Normalize(illustratorName, true);
        var exact = fonts.FirstOrDefault(f => Normalize(f.DisplayName, false) == full || Normalize(f.FamilyName, false) == full);
        if (exact is not null) return exact;
        exact = fonts.FirstOrDefault(f => Normalize(f.FamilyName, true) == family);
        if (exact is not null) return exact;
        if (family.Length < 4) return null;
        return fonts.FirstOrDefault(f =>
        {
            var candidate = Normalize(f.FamilyName, true);
            return candidate.Length >= 4 && (candidate.Contains(family, StringComparison.Ordinal) || family.Contains(candidate, StringComparison.Ordinal));
        });
    }

    public static FontItem? MatchInstalledFontForUsage(string illustratorName, IReadOnlyList<FontItem> fonts) =>
        MatchInstalledFont(illustratorName, fonts);

    public static BackupEntry? MatchBackupFont(string illustratorName, IReadOnlyList<BackupEntry> backups)
    {
        var available = backups.Where(x => File.Exists(x.BackupPath)).ToList();
        var full = Normalize(illustratorName, false);
        var exact = available
            .Where(x => CandidateFullNames(x).Any(name => Normalize(name, false) == full))
            .OrderByDescending(x => x.BackupTime)
            .FirstOrDefault();
        if (exact is not null) return exact;

        var family = Normalize(illustratorName, true);
        var familyMatches = available
            .Where(x => CandidateFullNames(x).Any(name => Normalize(name, true) == family))
            .GroupBy(x => Normalize(string.IsNullOrWhiteSpace(x.DisplayName) ? Path.GetFileNameWithoutExtension(x.BackupPath) : x.DisplayName, false), StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(entry => entry.BackupTime).First())
            .ToList();
        return familyMatches.Count == 1 ? familyMatches[0] : null;
    }

    private static IEnumerable<string> CandidateFullNames(BackupEntry entry)
    {
        yield return entry.DisplayName;
        yield return entry.FamilyName;
        yield return entry.RegistryName;
        yield return Path.GetFileNameWithoutExtension(entry.BackupPath);
    }

    private static string Normalize(string value, bool removeStyle)
    {
        var normalized = PdfSubsetPrefixRegex().Replace(value, "").ToLowerInvariant();
        normalized = new string(normalized.Where(char.IsLetterOrDigit).ToArray());
        foreach (var fontType in new[] { "truetype", "opentype" })
            if (normalized.EndsWith(fontType, StringComparison.Ordinal)) normalized = normalized[..^fontType.Length];
        if (!removeStyle) return normalized;
        foreach (var suffix in new[] { "bolditalic", "semibold", "demibold", "extralight", "ultralight", "extrabold", "black", "heavy", "medium", "regular", "italic", "oblique", "bold", "light", "roman", "mt" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal) && normalized.Length > suffix.Length + 2) { normalized = normalized[..^suffix.Length]; break; }
        return normalized;
    }
}
