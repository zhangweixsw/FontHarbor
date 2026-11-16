using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace FontHarbor;

public sealed class FontItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isUserLocked;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string StyleName { get; set; } = "Regular";
    private System.Windows.Media.FontFamily? _previewFamily;
    [JsonIgnore] public System.Windows.Media.FontFamily PreviewFamily
    {
        get
        {
            if (_previewFamily is not null) return _previewFamily;
            try { _previewFamily = IsCollection ? System.Windows.Media.Fonts.GetFontFamilies(new Uri(FilePath)).FirstOrDefault() : new System.Windows.Media.FontFamily(FamilyName); }
            catch { }
            return _previewFamily ??= new System.Windows.Media.FontFamily("Segoe UI");
        }
    }

    public FontWeight RenderWeight => StyleName.Contains("Bold", StringComparison.OrdinalIgnoreCase) || StyleName.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ? FontWeights.Bold : FontWeights.Normal;
    public FontStyle RenderFontStyle => StyleName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ? FontStyles.Italic : FontStyles.Normal;
    public string FilePath { get; set; } = "";
    public string CanonicalKey => Path.GetFullPath(FilePath).ToLowerInvariant();
    public string RegistryName { get; set; } = "";
    public string RegistryValue { get; set; } = "";
    [JsonIgnore] public RegistryHive RegistryHive { get; set; }
    public string RegistryHiveName { get; set; } = "";
    public string RegistryKeyPath { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsProtected { get; set; }
    public bool IsCollection { get; set; }
    public string SystemProtectionKind { get; set; } = "";
    public string SystemProtectionReason { get; set; } = "";
    public bool IsWindowsFont => SystemProtectionKind.Length > 0;
    public string ProtectionReason => string.Join("\n", new[] {
        SystemProtectionReason,
        IsCollection ? "字体集合或共享字体文件：删除会同时影响其他样式，自动保护。" : "",
        IsUserLocked ? "已由用户或分类锁定，可通过解锁所选解除自定义锁定。" : ""
    }.Where(x => x.Length > 0));
    public string ProtectionBadge => IsLocked ? ProtectionText : "";
    public bool IsUserLocked
    {
        get => _isUserLocked;
        set
        {
            if (_isUserLocked == value) return;
            _isUserLocked = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(IsLocked)); OnPropertyChanged(nameof(CanSelect)); OnPropertyChanged(nameof(LockText)); OnPropertyChanged(nameof(ProtectionText)); OnPropertyChanged(nameof(ProtectionBadge)); OnPropertyChanged(nameof(ProtectionReason));
        }
    }
    public bool IsLocked => IsProtected || IsUserLocked;
    public bool CanSelect => true;
    public string LockText => IsProtected ? IsWindowsFont ? "Windows保护" : "集合保护" : IsUserLocked ? "已锁定" : "";
    public string ProtectionText => IsWindowsFont ? SystemProtectionKind + " · 已锁定" : IsCollection ? "字体集合 · 锁定" : IsProtected ? "自动保护 · 锁定" : IsUserLocked ? "已锁定" : IsSystem ? "系统" : "用户";
    public string TagsText { get; set; } = "";
    public string CategoriesText { get; set; } = "";
    public long FileSize { get; set; }
    public string SizeText => FileSize < 1024 * 1024 ? $"{FileSize / 1024d:0} KB" : $"{FileSize / 1024d / 1024d:0.0} MB";

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class IllustratorFontUsage
{
    public bool IsSelected { get; set; }
    public string FontName { get; set; } = "";
    public List<string> Files { get; set; } = [];
    [JsonIgnore] public FontItem? MatchedFont { get; set; }
    [JsonIgnore] public BackupEntry? MatchedBackup { get; set; }
    public int FileCount => Files.Count;
    public string InstalledName => MatchedFont?.DisplayName ?? MatchedBackup?.DisplayName ?? "未安装且备份中未找到";
    public string MatchStatus => MatchedFont is not null
        ? MatchedFont.IsLocked ? "已安装 · 已锁定" : "已安装"
        : MatchedBackup is not null ? "缺失 · 备份可安装" : "缺失";
    public bool CanSelect => MatchedFont is not null ? !MatchedFont.IsLocked : MatchedBackup is not null;
    public string FilesText => string.Join("\n", Files.Take(20));
}

public sealed class IllustratorScanResult
{
    public List<IllustratorFontUsage> Fonts { get; set; } = [];
    public int DiscoveredFileCount { get; set; }
    public int ScannedFileCount { get; set; }
    public bool Cancelled { get; set; }
}

public sealed class BackupEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime BackupTime { get; set; } = DateTime.Now;
    public string DisplayName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string RegistryName { get; set; } = "";
    public string RegistryValue { get; set; } = "";
    public string RegistryHiveName { get; set; } = "";
    public string RegistryKeyPath { get; set; } = "";
    public string Status { get; set; } = "已备份";
}

public sealed record RecentFont(string Family, string Style, string Name);

public sealed class AppSettings
{
    public List<RecentFont> RecentFonts { get; set; } = [];
    public ApiProfile AiProfile { get; set; } = new();
    public Dictionary<string, FontAnnotation> FontAnnotations { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> FontTagBindings { get; set; } = new(StringComparer.Ordinal);
    public string BackupRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FontHarbor备份");
    public bool AlwaysOnTop { get; set; }
    public bool CompactMode { get; set; }
    public double CategoryPanelWidth { get; set; } = 200;
    public double CompactCategoryPanelWidth { get; set; } = 164;
    public List<string> FontFolders { get; set; } = [];
    public List<string> IllustratorScanRoots { get; set; } = [];
    public string IllustratorScanRoot { get; set; } = "";
    public Dictionary<string, List<string>> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> LockedFonts { get; set; } = [];
}
