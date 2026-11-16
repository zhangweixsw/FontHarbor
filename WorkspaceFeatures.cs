using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FontHarbor;

public partial class MainWindow
{
    private CancellationTokenSource? _folderCancellation;
    private readonly List<ExternalFont> _externalFonts = [];
    private bool _updatingFolders;
    private bool _compactLayout;
    private bool _syncingCategory;
    public System.Collections.ObjectModel.ObservableCollection<CategoryChoice> CompactCategories { get; } = [];
    public Visibility SourceTabsVisibility => _compactLayout ? Visibility.Collapsed : Visibility.Visible;

    public sealed record CategoryChoice(string Name, int Count)
    {
        public System.Windows.Media.Geometry Icon => IconGeometryConverter.For(Name);
    }

    private bool _applyingFont;
    private double _fullWidth = 1480, _fullHeight = 860;
    public string ExternalPreviewText => ExternalPreviewInput?.Text ?? "";

    private void InitializeWorkspace()
    {
        _settings.FontFolders ??= [];
        Topmost = _settings.AlwaysOnTop;
        PinButton.Content = Topmost ? "取消置顶" : "窗口置顶";
        InitializeTagging();
        UpdateFolderChoices();
        SetCompact(_settings.CompactMode);
        Closed += (_, _) => { _folderCancellation?.Cancel(); _tagWindow?.RequestCancel(); };
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost; _settings.AlwaysOnTop = Topmost;
        PinButton.Content = Topmost ? "取消置顶" : "窗口置顶"; SaveSettings();
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        _settings.CompactMode = !_settings.CompactMode;
        SetCompact(_settings.CompactMode); SaveSettings();
    }

    private static double SafeCategoryWidth(double value) => double.IsFinite(value) ? Math.Clamp(value,160,300) : 200;
    private void CategorySplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var width=SafeCategoryWidth(CompactCategoryColumn.ActualWidth);
        if(_compactLayout) _settings.CompactCategoryPanelWidth=width; else _settings.CategoryPanelWidth=width;
        SaveSettings();
    }

    private void SetCompact(bool compact)
    {
        if (compact) { _fullWidth = Width; _fullHeight = Height; WindowState = WindowState.Normal; }
        _compactLayout = compact;
        OnPropertyChanged(nameof(SourceTabsVisibility));
        CompactCategoryPanel.Visibility = Visibility.Visible;
        CompactCategoryColumn.Width = new GridLength(SafeCategoryWidth(compact ? _settings.CompactCategoryPanelWidth : _settings.CategoryPanelWidth));
        MinWidth = compact ? 800 : 1180; MinHeight = compact ? 440 : 700;
        Width = compact ? 920 : _fullWidth; Height = compact ? 660 : _fullHeight;
        LibraryDetails.Visibility = LibrarySplitter.Visibility = LibraryManagement.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DetailColumn.MinWidth = compact ? 0 : 280;
        DetailColumn.Width = compact ? new GridLength(0) : new GridLength(300);
        LibraryLayout.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 10);
        LibraryColumn.MinWidth = compact ? 0 : 400;
        LibraryColumn.Width = new GridLength(1, GridUnitType.Star);
        FontsGrid.Columns[5].Width = new DataGridLength(96);
        if (!compact) FontsGrid.Columns[1].Width = new DataGridLength(1,DataGridLengthUnitType.Star);
        foreach (FrameworkElement child in LibraryTools.Children)
            if (Grid.GetColumn(child) is 0 or 1 or 2 or 4 or 5 or 6) child.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RefreshFontsButton.Visibility = BackupSettingsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Width = compact ? 150 : 150;
        ApiSettingsButton.Visibility = TagScanButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        // Compact mode moves category filtering to the side and hides source tabs.
        for (int i = 0; i < FontsGrid.Columns.Count; i++)
            if (i is 0) FontsGrid.Columns[i].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (TabItem tab in Tabs.Items) if (tab != Tabs.Items[0]) tab.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Tabs.SelectedIndex = 0;
        CompactButton.Content = compact ? "完整模式" : "精简模式";
        StyleSectionHeading.Visibility = AuxiliarySectionHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StyleGroupsHost.Margin = new Thickness(compact ? 0 : 8,0,0,0);
        UpdateFontColumnWidths();
        SyncCategorySidebarSelection();
    }

    private void FontsGrid_SizeChanged(object sender,SizeChangedEventArgs e) => UpdateFontColumnWidths();

    private void UpdateFontColumnWidths()
    {
        if (!_compactLayout || FontsGrid is null || FontsGrid.ActualWidth<=0) return;
        // Compact mode reserves the action column before assigning the preview width.
        // Pixel widths avoid stale DataGrid star allocations after hiding columns or changing DPI.
        var previewWidth=Math.Max(120,FontsGrid.ActualWidth-SystemParameters.VerticalScrollBarWidth-96-4);
        if(Math.Abs(FontsGrid.Columns[1].Width.Value-previewWidth)>0.5 || FontsGrid.Columns[1].Width.UnitType!=DataGridLengthUnitType.Pixel)
            FontsGrid.Columns[1].Width=new DataGridLength(previewWidth);
    }

    private async void ApplyInstalled_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FontItem font) return;
        var family = font.FamilyName; var style = font.StyleName;
        if (font.IsCollection)
        {
            try
            {
                var faces = await Task.Run(() => FolderFontScanner.ReadFile(font.FilePath, Path.GetDirectoryName(font.FilePath)!));
                if (faces.Count == 0) throw new InvalidOperationException("无法读取集合中的字体样式。");
                var picker = new Window {Title="选择集合中的字体样式",Owner=this,Width=520,Height=180,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
                var panel = new StackPanel {Margin=new Thickness(20)};
                panel.Children.Add(new TextBlock {Text="此集合包含多个字体，请选择要应用的字体与样式：",Margin=new Thickness(0,0,0,12)});
                var combo = new ComboBox {ItemsSource=faces,DisplayMemberPath="Name",SelectedIndex=0}; panel.Children.Add(combo);
                var apply = new Button {Content="应用到 Illustrator",Margin=new Thickness(0,12,0,0),IsDefault=true};
                apply.Click += (_,_) => picker.DialogResult=true;panel.Children.Add(apply);picker.Content=panel;
                if (picker.ShowDialog()!=true || combo.SelectedItem is not ExternalFont selectedFace) return;
                await ApplyToIllustratorAsync(selectedFace.Family, selectedFace.Style, selectedFace.PostScriptName); return;
            }
            catch (Exception ex) {MessageBox.Show(this, ex.Message, "字体集合");return;}
        }
        if (!font.IsCollection)
        {
            try
            {
                var metadata = await Task.Run(() => {
                    var glyph = new System.Windows.Media.GlyphTypeface(new Uri(font.FilePath));
                    var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                    return (Family: glyph.FamilyNames.TryGetValue(culture, out var en) ? en : glyph.FamilyNames.Values.First(),
                        Style: glyph.FaceNames.TryGetValue(culture, out var enStyle) ? enStyle : glyph.FaceNames.Values.First());
                });
                family = metadata.Family; style = metadata.Style;
            }
            catch (Exception ex) { MessageBox.Show(this, "无法读取字体文件：" + ex.Message, "字体应用"); return; }
        }
        await ApplyToIllustratorAsync(family, style, font.DisplayName.Replace(" (TrueType)", "").Replace(" (OpenType)", ""));
    }

    private async Task ApplyToIllustratorAsync(string family, string style, string name)
    {
        if (_applyingFont) return;
        _applyingFont = true; StatusText.Text = "正在应用到 Illustrator 选中文字…";
        try {
            StatusText.Text = await IllustratorBridge.ApplyAsync(family, style, name);
            _settings.RecentFonts ??= [];
            _settings.RecentFonts.RemoveAll(x => x.Family == family && x.Style == style);
            _settings.RecentFonts.Insert(0, new RecentFont(family, style, name));
            _settings.RecentFonts = _settings.RecentFonts.Take(20).ToList();
            try { SaveSettings(); } catch { StatusText.Text += " · 最近应用记录未能保存"; }
        }
        catch (Exception ex) { StatusText.Text = "未应用字体"; MessageBox.Show(this, ex.Message, "Illustrator 字体应用", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { _applyingFont = false; }
    }

    private void UpdateFolderChoices(string? selected = null)
    {
        _updatingFolders = true;
        FontFolderCombo.ItemsSource = _settings.FontFolders.ToList();
        FontFolderCombo.SelectedItem = selected ?? _settings.FontFolders.FirstOrDefault();
        _updatingFolders = false;
    }

    private async void AddFontFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "添加字体文件夹（含子文件夹）" };
        if (dialog.ShowDialog(this) != true) return;
        await AddAndScanFolderAsync(dialog.FolderName);
    }
    private async void ScanBackupFolder_Click(object sender, RoutedEventArgs e) => await AddAndScanFolderAsync(_settings.BackupRoot);
    private async Task AddAndScanFolderAsync(string path)
    {
        if (!_settings.FontFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) _settings.FontFolders.Add(path);
        SaveSettings(); UpdateFolderChoices(path); await ScanFolderAsync(path);
    }
    private async void RescanFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FontFolderCombo.SelectedItem is string path) await ScanFolderAsync(path);
    }
    private async void FontFolder_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFolders && FontFolderCombo.SelectedItem is string path) await ScanFolderAsync(path);
    }
    private void CancelFolder_Click(object sender, RoutedEventArgs e) => _folderCancellation?.Cancel();
    private async Task ScanFolderAsync(string path)
    {
        _folderCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); _folderCancellation = cancellation;
        CancelFolderButton.IsEnabled = true; ExternalStatus.Text = "正在扫描 " + path;
        var progress = new Progress<int>(n => { if (_folderCancellation == cancellation) ExternalStatus.Text = $"已发现 {n} 个字体文件，正在解析样张…"; });
        try
        {
            var result = await Task.Run(() => FolderFontScanner.Scan(path, cancellation.Token, progress));
            if (_folderCancellation != cancellation) return;
            _externalFonts.Clear(); _externalFonts.AddRange(result.Fonts);
            ExternalGrid.ItemsSource = CollectionViewSource.GetDefaultView(_externalFonts);
            ExternalFilter_Changed(this, null!);
            RefreshCategorySidebar();
            ExternalStatus.Text = $"扫描完成 · {result.Fonts.Count} 个字体样式 · {result.Failed} 个文件无法解析 · {result.SkippedDirectories} 个目录无法访问。预览不会安装字体。";
        }
        catch (OperationCanceledException) { if (_folderCancellation == cancellation) ExternalStatus.Text = "已取消扫描，保留上次结果。"; }
        catch (Exception ex) { if (_folderCancellation == cancellation) ExternalStatus.Text = "扫描失败：" + ex.Message; }
        finally
        {
            if (_folderCancellation == cancellation) { _folderCancellation = null; CancelFolderButton.IsEnabled = false; }
            cancellation.Dispose();
        }
    }
    private void ExternalFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (ExternalGrid?.ItemsSource is not System.ComponentModel.ICollectionView view) return;
        var query = ExternalSearch.Text.Trim();
        view.Filter = o => o is ExternalFont f && (f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || f.Path.Contains(query, StringComparison.OrdinalIgnoreCase)) && MatchesFacets(f.Path, f.Family, f.Style);
    }
    private void ExternalPreview_Changed(object sender, TextChangedEventArgs e) => OnPropertyChanged(nameof(ExternalPreviewText));
    private async void InstallExternal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ExternalFont font || sender is not Button button) return;
        button.IsEnabled = false;
        try
        {
            await Task.Run(() => _fontService.InstallForCurrentUser(font.Path));
            await RefreshAsync();
            await ApplyToIllustratorAsync(font.Family, font.Style, font.PostScriptName);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "字体安装失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { button.IsEnabled = true; }
    }
}
