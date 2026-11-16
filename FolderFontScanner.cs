using System.IO;
using System.Windows.Media;

namespace FontHarbor;

public sealed class ExternalFont
{
    public string TagsText { get; set; } = "未标注";
    public string Name { get; init; } = "";
    public string Family { get; init; } = "";
    public string Style { get; init; } = "";
    public System.Windows.FontWeight Weight { get; init; } = System.Windows.FontWeights.Normal;
    public System.Windows.FontStyle Slant { get; init; } = System.Windows.FontStyles.Normal;
    public string PostScriptName { get; init; } = "";
    public string Path { get; init; } = "";
    public FontFamily PreviewFamily { get; init; } = new("Segoe UI");
    public string Source { get; init; } = "";
    public string Format => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
}

public sealed class FolderScanResult
{
    public List<ExternalFont> Fonts { get; } = [];
    public int Failed { get; set; }
    public int SkippedDirectories { get; set; }
}

public static class FolderFontScanner
{
    public static FolderScanResult Scan(string root, CancellationToken token, IProgress<int>? progress = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("字体目录不存在。");
        var result = new FolderScanResult();
        var pending = new Stack<string>(); pending.Push(root);
        var count = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
                foreach (var dir in Directory.GetDirectories(folder))
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) pending.Push(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.SkippedDirectories++; continue; }
            foreach (var path in files.Where(p => new[] { ".ttf", ".otf", ".ttc", ".otc" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())))
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(++count);
                try
                {
                    var faces = ReadFile(path, root);
                    result.Fonts.AddRange(faces);
                    if (faces.Count == 0) result.Failed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { result.Failed++; }
            }
        }
        return result;
    }

    public static List<ExternalFont> ReadFile(string path, string root)
    {
        var list = new List<ExternalFont>();
                    var uri = new Uri(path);
                    var families = Fonts.GetFontFamilies(uri);

                    foreach (var family in families)
                    foreach (var face in family.GetTypefaces())
                    {
                        if (!face.TryGetGlyphTypeface(out var glyph) || face.IsBoldSimulated || face.IsObliqueSimulated) continue;
                        if (!string.Equals(System.IO.Path.GetFullPath(glyph.FontUri.LocalPath), System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) continue;
                        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                        var familyName = glyph.FamilyNames.TryGetValue(culture, out var english) ? english : glyph.FamilyNames.Values.FirstOrDefault() ?? family.Source;
                        var style = glyph.FaceNames.TryGetValue(culture, out var englishStyle) ? englishStyle : glyph.FaceNames.Values.FirstOrDefault() ?? "Regular";
                        list.Add(new ExternalFont
                        {
                            Name = familyName + " · " + style,
                            Family = familyName, Style = style, Weight = face.Weight, Slant = face.Style, Path = path, Source = root,
                            PostScriptName = "",
                            PreviewFamily = new FontFamily(new Uri(System.IO.Path.GetDirectoryName(path)! + System.IO.Path.DirectorySeparatorChar), "./" + Uri.EscapeDataString(System.IO.Path.GetFileName(path)) + "#" + familyName)
                        });

                    }
        return list;
    }
}
