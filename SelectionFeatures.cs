using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FontHarbor;

public static class FontCoverage
{
    public static string Describe(FontItem font, string text)
    {
        if (font.IsCollection) return "字体集合：请确认具体样式，当前不判定字符覆盖。";
        try
        {
            var glyph = new GlyphTypeface(new Uri(Path.GetFullPath(font.FilePath)));
            return Describe(glyph, text);
        }
        catch { return "无法读取字符覆盖，预览可能使用系统替代字形。"; }
    }

    public static string Describe(GlyphTypeface glyph, string text)
    {
        var characters = text.EnumerateRunes().Where(r => !Rune.IsWhiteSpace(r) && !Rune.IsControl(r)).Distinct().ToArray();
        if (characters.Length == 0) return "输入预览文字后检查字符支持。";
        var missing = characters.Where(r => !glyph.CharacterToGlyphMap.TryGetValue(r.Value, out var index) || index == 0).ToArray();
        return missing.Length == 0 ? "当前文字的字符均受支持（不代表所有语言均受支持）。"
            : $"当前文字缺少 {missing.Length} 种字符：{string.Join(" ", missing.Take(12).Select(r => r.ToString()))}{(missing.Length > 12 ? " …" : "")}。缺字可能显示为系统替代字形。";
    }
}

public partial class MainWindow
{
    private void CompareFonts_Click(object sender, RoutedEventArgs e)
    {
        var selected = CheckedFonts().ToList();
        if (selected.Count == 0) selected = FontsGrid.SelectedItems.Cast<FontItem>().ToList();
        if (selected.Count < 2 || selected.Count > 6)
        {
            MessageBox.Show(this, "请勾选 2–6 款字体，再点击对比所选。", "字体对比"); return;
        }
        CreateComparison(selected).Show();
    }

    internal Window CreateComparison(IReadOnlyList<FontItem> fonts)
    {
        var window = new Window { Title = "字体对比", Owner = IsLoaded ? this : null, Width = 1000, Height = 720, MinWidth = 640, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("SurfaceAlt") };
        var root = new DockPanel { Margin = new Thickness(20) };
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls);
        var input = new TextBox { Text = ListPreviewText, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
        controls.Children.Add(input);
        var tools = new WrapPanel { Margin = new Thickness(0,8,0,12) }; controls.Children.Add(tools);
        var size = new Slider { Minimum = 16, Maximum = 72, Value = 32, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(new TextBlock { Text = "统一字号", VerticalAlignment = VerticalAlignment.Center }); tools.Children.Add(size);
        var cards = new WrapPanel();
        var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll);
        var previews = new List<(FontItem Font, TextBlock Sample, TextBlock Coverage)>();
        foreach (var font in fonts)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = font.DisplayName, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
            var sample = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = font.PreviewFamily, FontWeight = font.RenderWeight, FontStyle = font.RenderFontStyle, MinHeight = 110 };
            content.Children.Add(sample);
            var coverage = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("TextSecondary"), Margin = new Thickness(0,12,0,0) }; content.Children.Add(coverage);
            var apply = new Button { Content = "应用到 Illustrator", DataContext = font, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,12,0,0) }; apply.Click += ApplyInstalled_Click; content.Children.Add(apply);
            var card = new Border { Child = content, Width = 430, Padding = new Thickness(16), Margin = new Thickness(0,0,12,12), CornerRadius = new CornerRadius(10), Background = (Brush)FindResource("Surface"), BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1) }; cards.Children.Add(card);
            previews.Add((font, sample, coverage));
        }
        void Update()
        {
            foreach (var entry in previews) { entry.Sample.Text = input.Text; entry.Sample.FontSize = size.Value; entry.Coverage.Text = FontCoverage.Describe(entry.Font, input.Text); }
        }
        foreach (var preset in new[] { ("中文", "字体之美，让设计更有表达。"), ("西文", "The quick brown fox jumps over the lazy dog."), ("数字", "0123456789  ¥123.45  10:30"), ("段落", "字体承载信息，也塑造阅读的节奏。\nTypography gives language a visible form.\n在同样的字号下，比较字面、间距与阅读感受。") })
        {
            var button = new Button { Content = preset.Item1, ContentTemplate = null }; var text = preset.Item2; button.Click += (_,_) => input.Text = text; tools.Children.Add(button);
        }
        input.TextChanged += (_,_) => Update(); size.ValueChanged += (_,_) => Update();
        scroll.SizeChanged += (_,_) => { var width = Math.Max(240, scroll.ViewportWidth - 18); foreach (Border card in cards.Children) card.Width = width >= 780 ? (width - 24) / 2 : width - 12; };
        Update(); window.Content = root; return window;
    }

    private void RecentFonts_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window { Title = "最近应用 · 最多 20 款", Owner = this, Width = 560, Height = 520, MinWidth = 400, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(18) };
        var history = (_settings.RecentFonts ?? []).ToArray();
        panel.Children.Add(new TextBlock { Text = history.Length == 0 ? "还没有成功应用的记录。" : "点击字体，再次应用到 Illustrator 当前选中的文字。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        foreach (var recent in history)
        {
            var button = new Button { Content = recent.Family + " · " + recent.Style, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += async (_,_) => { button.IsEnabled = false; try { await ApplyToIllustratorAsync(recent.Family, recent.Style, recent.Name); } finally { button.IsEnabled = true; } };
            panel.Children.Add(button);
        }
        var clear = new Button { Content = "清空最近应用", IsEnabled = history.Length > 0, Margin = new Thickness(0,14,0,0) };
        clear.Click += (_,_) => { var previous = _settings.RecentFonts ?? []; _settings.RecentFonts = []; try { SaveSettings(); window.Close(); } catch { _settings.RecentFonts = previous; MessageBox.Show(window, "保存失败，最近应用记录已保留。", "最近应用"); } };
        panel.Children.Add(clear); window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Show();
    }
}
