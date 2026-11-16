using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;

namespace FontHarbor;

public sealed class FontService
{
    private const string SystemFontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
    private const string UserFontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string name, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    public List<FontItem> Scan(AppSettings settings)
    {
        var result = new List<FontItem>();
        ReadRegistryFonts(RegistryHive.LocalMachine, RegistryView.Registry64, SystemFontsKey, true, settings, result);
        ReadRegistryFonts(RegistryHive.CurrentUser, RegistryView.Default, UserFontsKey, false, settings, result);
        var unique = result.GroupBy(x => $"{x.RegistryHiveName}|{x.RegistryName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList();
        // TTC/OTC 集合文件通常由多个注册表项共享，自动锁定以免破坏整个集合。
        foreach (var group in unique.GroupBy(x => Path.GetFullPath(x.FilePath), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var item in group) { item.IsCollection = true; item.IsProtected = true; }
        foreach (var item in unique) SystemFontProtection.Apply(item);
        return unique.OrderBy(x => x.FamilyName).ThenBy(x => x.DisplayName).ToList();
    }

    private static void ReadRegistryFonts(RegistryHive hive, RegistryView view, string keyPath, bool system, AppSettings settings, List<FontItem> target)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(keyPath, false);
        if (key is null) return;
        foreach (var valueName in key.GetValueNames())
        {
            var raw = key.GetValue(valueName)?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var filePath = Path.IsPathRooted(raw) ? raw : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), raw);
            var family = CleanFamilyName(valueName);
            var style = GetStyle(valueName);
            var displayName = valueName;
            var collection = new[] { ".ttc", ".otc" }.Contains(Path.GetExtension(filePath).ToLowerInvariant());
            // A registry display label is not a reliable font family or style name.
            if (!collection && valueName.StartsWith("FontHarbor_", StringComparison.Ordinal) && File.Exists(filePath))
            {
                try
                {
                    var glyph = new GlyphTypeface(new Uri(filePath));
                    family = glyph.FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var english) ? english : glyph.FamilyNames.Values.First();
                    style = glyph.FaceNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var englishStyle) ? englishStyle : glyph.FaceNames.Values.First();
                    displayName = family + " · " + style;
                }
                catch { /* Keep the registry fallback for older/unsupported fonts. */ }
            }
            var canonical = Path.GetFullPath(filePath).ToLowerInvariant();
            settings.Tags.TryGetValue(canonical, out var tags);
            var categories = settings.Categories.Where(x => x.Value.Contains(canonical, StringComparer.OrdinalIgnoreCase)).Select(x => x.Key).OrderBy(x => x).ToList();
            target.Add(new FontItem
            {
                DisplayName = displayName, FamilyName = family, StyleName = style, FilePath = filePath,
                RegistryName = valueName, RegistryValue = raw, RegistryHive = hive, RegistryHiveName = hive.ToString(), RegistryKeyPath = keyPath,
                IsCollection = collection, IsSystem = system, IsProtected = collection,
                IsUserLocked = settings.LockedFonts.Contains(canonical, StringComparer.OrdinalIgnoreCase),
                TagsText = tags is null ? "" : string.Join(", ", tags), CategoriesText = string.Join(", ", categories),
                FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0
            });
        }
    }

    private static string CleanFamilyName(string valueName)
    {
        var name = valueName.Replace("(TrueType)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(OpenType)", "", StringComparison.OrdinalIgnoreCase).Trim();
        foreach (var suffix in new[] { " Bold Italic", " Bold", " Italic", " Light", " Medium", " Semibold", " Regular" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length].Trim();
        return name;
    }

    private static string GetStyle(string name)
    {
        var italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        var weight = name.Contains("ExtraLight", StringComparison.OrdinalIgnoreCase) ? "Extra Light" :
            name.Contains("Semibold", StringComparison.OrdinalIgnoreCase) ? "Semi Bold" :
            name.Contains("Bold", StringComparison.OrdinalIgnoreCase) ? "Bold" :
            name.Contains("Heavy", StringComparison.OrdinalIgnoreCase) || name.Contains("Black", StringComparison.OrdinalIgnoreCase) ? "Heavy" :
            name.Contains("Medium", StringComparison.OrdinalIgnoreCase) ? "Medium" :
            name.Contains("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Regular";
        return italic ? weight + " Italic" : weight;
    }

    public BackupEntry BackupAndRemove(FontItem font, string backupRoot, bool broadcastChange = true)
    {
        if (font.IsLocked) throw new InvalidOperationException($"“{font.FamilyName}”已锁定，禁止删除。");
        if (font.IsSystem) RequireAdministrator();
        if (!File.Exists(font.FilePath)) throw new FileNotFoundException("字体文件不存在。", font.FilePath);
        Directory.CreateDirectory(backupRoot);
        var folder = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(folder);
        var destination = UniquePath(folder, Path.GetFileName(font.FilePath));
        File.Copy(font.FilePath, destination, false);
        var entry = new BackupEntry
        {
            DisplayName = font.DisplayName, FamilyName = font.FamilyName, OriginalPath = font.FilePath,
            BackupPath = destination, RegistryName = font.RegistryName, RegistryValue = font.RegistryValue, RegistryHiveName = font.RegistryHiveName,
            RegistryKeyPath = font.RegistryKeyPath, Status = "已备份，正在删除"
        };
        SaveManifest(entry, folder);

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(font.RegistryHive, font.IsSystem ? RegistryView.Registry64 : RegistryView.Default);
            using var key = baseKey.OpenSubKey(font.RegistryKeyPath, true) ?? throw new UnauthorizedAccessException("无法写入字体注册表。请以管理员身份运行。");
            var originalValue = key.GetValue(font.RegistryName)?.ToString();
            key.DeleteValue(font.RegistryName, false);
            RemoveFontResourceEx(font.FilePath, 0, IntPtr.Zero);
            if (broadcastChange) BroadcastFontChange();
            try { File.Delete(font.FilePath); }
            catch
            {
                if (originalValue is not null) key.SetValue(font.RegistryName, originalValue, RegistryValueKind.String);
                AddFontResourceEx(font.FilePath, 0, IntPtr.Zero);
                if (broadcastChange) BroadcastFontChange();
                throw;
            }
            entry.Status = "已删除，可恢复";
            SaveManifest(entry, folder);
            return entry;
        }
        catch
        {
            entry.Status = "删除失败，备份已保留";
            SaveManifest(entry, folder);
            throw;
        }
    }

    public void Restore(BackupEntry entry)
    {
        if (entry.RegistryHiveName == RegistryHive.LocalMachine.ToString()) RequireAdministrator();
        if (!File.Exists(entry.BackupPath)) throw new FileNotFoundException("备份字体文件不存在。", entry.BackupPath);
        Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
        if (!File.Exists(entry.OriginalPath)) File.Copy(entry.BackupPath, entry.OriginalPath);
        var hive = Enum.Parse<RegistryHive>(entry.RegistryHiveName);
        using var baseKey = RegistryKey.OpenBaseKey(hive, hive == RegistryHive.LocalMachine ? RegistryView.Registry64 : RegistryView.Default);
        using var key = baseKey.OpenSubKey(entry.RegistryKeyPath, true) ?? throw new UnauthorizedAccessException("无法写入字体注册表。");
        var registryValue = string.IsNullOrWhiteSpace(entry.RegistryValue)
            ? (hive == RegistryHive.LocalMachine ? Path.GetFileName(entry.OriginalPath) : entry.OriginalPath)
            : entry.RegistryValue;
        key.SetValue(entry.RegistryName, registryValue, RegistryValueKind.String);
        if (AddFontResourceEx(entry.OriginalPath, 0, IntPtr.Zero) == 0) throw new InvalidOperationException("字体文件已恢复，但 Windows 未能立即加载。重启后通常会自动生效。");
        BroadcastFontChange();
        entry.Status = "已恢复";
        SaveManifest(entry, Path.GetDirectoryName(entry.BackupPath)!);
    }

    public void InstallForCurrentUser(string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("字体文件不存在。", source);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        Directory.CreateDirectory(folder);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
        var destination = Path.Combine(folder, "FontHarbor_" + hash + Path.GetExtension(source).ToLowerInvariant());
        var existed = File.Exists(destination);
        if (!existed) File.Copy(source, destination, false);
        // The hash-based key avoids replacing another font's registry value.
        using var key = Registry.CurrentUser.CreateSubKey(UserFontsKey, true);
        var registryName = "FontHarbor_" + hash + " (" + (Path.GetExtension(source).Equals(".otf", StringComparison.OrdinalIgnoreCase) ? "OpenType" : "TrueType") + ")";
        var oldValue = key.GetValue(registryName);
        try
        {
            if (oldValue is null && AddFontResourceEx(destination, 0, IntPtr.Zero) == 0) throw new InvalidOperationException("Windows 无法加载此字体。原始文件已保留。");
            key.SetValue(registryName, destination, RegistryValueKind.String);
        }
        catch
        {
            if (oldValue is null) key.DeleteValue(registryName, false);
            else key.SetValue(registryName, oldValue);
            if (oldValue is null) RemoveFontResourceEx(destination, 0, IntPtr.Zero);
            if (!existed) { try { File.Delete(destination); } catch { } }
            throw;
        }
        BroadcastFontChange();
    }

    public List<BackupEntry> LoadBackups(string root)
    {
        if (!Directory.Exists(root)) return [];
        var list = new List<BackupEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*.fontharbor.json", SearchOption.AllDirectories))
        {
            try { var item = JsonSerializer.Deserialize<BackupEntry>(File.ReadAllText(path)); if (item is not null) list.Add(item); } catch { }
        }
        return list.OrderByDescending(x => x.BackupTime).ToList();
    }

    private static void RequireAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("管理系统字体需要管理员权限。请关闭字港，右键程序选择“以管理员身份运行”，再执行系统字体删除或恢复。预览和当前用户字体安装无需管理员权限。");
    }

    private static void SaveManifest(BackupEntry entry, string folder) =>
        File.WriteAllText(Path.Combine(folder, entry.Id + ".fontharbor.json"), JsonSerializer.Serialize(entry, JsonOptions));
    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName); var i = 1;
        while (File.Exists(path)) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)}_{i++}{Path.GetExtension(fileName)}");
        return path;
    }
    private static void BroadcastFontChange() => SendMessageTimeout(new IntPtr(0xffff), 0x001D, IntPtr.Zero, IntPtr.Zero, 0x0002, 1500, out _);
    public static void NotifyFontCollectionChanged() => BroadcastFontChange();
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };
}

