using System.IO;
using System.Text.Json;

namespace FontHarbor;

public static class SystemFontProtection
{
    public const string SourceUrl = "https://learn.microsoft.com/en-us/typography/fonts/windows_11_font_list";
    private static readonly Dictionary<string,string> Catalog = LoadCatalog();
    private static Dictionary<string,string> LoadCatalog()
    {
        using var stream = typeof(SystemFontProtection).Assembly.GetManifestResourceStream("FontHarbor.Assets.windows-font-catalog.json")
            ?? throw new InvalidOperationException("Windows 字体保护清单缺失。");
        return new Dictionary<string,string>(JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!, StringComparer.OrdinalIgnoreCase);
    }
    public static void Apply(FontItem font)
    {
        var systemFolder = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)).TrimEnd(Path.DirectorySeparatorChar);
        var folder = Path.GetDirectoryName(Path.GetFullPath(font.FilePath));
        var name = Path.GetFileName(font.FilePath);
        // 系统级注册或位于 Fonts 目录，都不能单独证明是 Windows 预装字体。
        if (!font.IsSystem || !string.Equals(folder,systemFolder,StringComparison.OrdinalIgnoreCase) || !Catalog.TryGetValue(name,out var catalogName)) return;
        var key = name.ToLowerInvariant();
        var ui = key.StartsWith("segui") || key.StartsWith("segoeui") || key is "segoeicons.ttf" or "segmdl2.ttf" or "tahoma.ttf" or "tahomabd.ttf" or "micross.ttf";
        var language = key.StartsWith("msyh") || key.StartsWith("msjh") || key.StartsWith("simsun") || key.StartsWith("mingliu") || key.StartsWith("msgothic") || key.StartsWith("yugoth") || key.StartsWith("malgun") || catalogName.Contains(" UI",StringComparison.OrdinalIgnoreCase);
        font.SystemProtectionKind = ui ? "系统界面" : language ? "语言支持" : "Windows预装";
        font.SystemProtectionReason = ui ? "Windows 界面、符号或兼容界面文字使用，自动锁定以防误删。"
            : language ? "Windows 中文或其他语言的显示与字体回退支持，自动锁定以防缺字。"
            : "文件名命中微软 Windows 预装/可选字体清单。为保留应用和文档兼容性而保护，不代表每台电脑都必需。";
        font.SystemProtectionReason += " 匹配文件：" + name + "。";
        font.IsProtected = true;
    }
}
