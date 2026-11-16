using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FontHarbor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string AllCategories = "全部字体";
    private readonly FontService _fontService = new();
    private readonly IllustratorScanner _illustratorScanner = new();
    private readonly ObservableCollection<FontItem> _fonts = [];
    private readonly ObservableCollection<BackupEntry> _backups = [];
    private readonly ObservableCollection<IllustratorFontUsage> _illustratorFonts = [];
    private CancellationTokenSource? _aiScanCancellation;
    private bool _isBulkDeleting;
    private readonly string _dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FontHarbor");
    private AppSettings _settings = new();
    private ICollectionView? _fontView;
    private string _listPreviewText = "字体设计 Typography 012345";
    private double _listPreviewSize = 24;
    private string SettingsPath => Path.Combine(_dataRoot, "settings.json");

    public event PropertyChangedEventHandler? PropertyChanged;
    public string ListPreviewText { get => _listPreviewText; private set { _listPreviewText = value; OnPropertyChanged(); } }
    public double ListPreviewSize { get => _listPreviewSize; private set { _listPreviewSize = value; OnPropertyChanged(); } }

    public MainWindow() : this(null) { }

    public MainWindow(string? dataRoot)
    {
        if (dataRoot is not null) _dataRoot = Path.GetFullPath(dataRoot);
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) =>
        {
            LoadSettings();
            InitializeWorkspace();
            SetupGlyphs();
            _settings.IllustratorScanRoots ??= [];
            if (_settings.IllustratorScanRoots.Count == 0 && !string.IsNullOrWhiteSpace(_settings.IllustratorScanRoot)) _settings.IllustratorScanRoots.Add(_settings.IllustratorScanRoot);
            UpdateAiFolders();
            await RefreshAsync();
            _ = CheckForUpdateAsync();
        };
    }

    private void MooTop_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://mootop.top/") { UseShellExecute = true }); }
        catch { MessageBox.Show("请在浏览器中打开 https://mootop.top/", "MooTop"); }
        e.Handled = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void LoadSettings()
    {
        Directory.CreateDirectory(_dataRoot);
        try { if (File.Exists(SettingsPath)) _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch { _settings = new(); }
        _settings.AiProfile ??= new();
        _settings.FontAnnotations ??= new(StringComparer.Ordinal);
        _settings.FontTagBindings ??= new(StringComparer.Ordinal);
        foreach (var annotation in _settings.FontAnnotations.Values) FontTaxonomy.NormalizeLegacy(annotation);
        _settings.Tags ??= new(StringComparer.OrdinalIgnoreCase);
        _settings.Categories ??= new(StringComparer.OrdinalIgnoreCase);
        _settings.LockedFonts ??= [];
        BackupPathText.Text = "备份目录：" + _settings.BackupRoot;
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(_dataRoot);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_settings, FontService.JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "正在扫描 Windows 字体注册表…";
        var items = await Task.Run(() => _fontService.Scan(_settings));
        _fonts.Clear(); foreach (var item in items) _fonts.Add(item);
        _fontView = CollectionViewSource.GetDefaultView(_fonts);
        _fontView.Filter = FilterFont;
        FontsGrid.ItemsSource = _fontView;
        RefreshCategoryChoices();
        RefreshBackups();
        UpdateCount();
        StatusText.Text = $"已加载 {_fonts.Count} 个字体 · Windows 自动保护 {_fonts.Count(x => x.IsWindowsFont)} 个 · 集合保护 {_fonts.Count(x => x.IsCollection && !x.IsWindowsFont)} 个 · 用户锁定 {_fonts.Count(x => x.IsUserLocked && !x.IsProtected)} 个";
    }

    private bool FilterFont(object obj)
    {
        if (obj is not FontItem font) return false;
        var query = SearchBox.Text.Trim();
        var textMatch = query.Length == 0 || font.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            font.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) || font.TagsText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            font.CategoriesText.Contains(query, StringComparison.OrdinalIgnoreCase) || font.ProtectionText.Contains(query, StringComparison.OrdinalIgnoreCase);
        return textMatch && MatchesFacets(font.FilePath, font.FamilyName, font.StyleName);
    }

    private void RefreshCategoryChoices() => RefreshCategorySidebar();

    private void FilterChanged(object sender, TextChangedEventArgs e) { _fontView?.Refresh(); UpdateCount(); }

    private void UpdateCount()
    {
        CountText.Text = $"{_fontView?.Cast<object>().Count() ?? _fonts.Count} / {_fonts.Count} 个字体";
        SelectedCountText.Text = $"已选择 {_fonts.Count(x => x.IsSelected)} 个 · 锁定 {_fonts.Count(x => x.IsLocked)} 个";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void SetupGlyphs()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789一二三四五六七八九十天地玄黄宇宙洪荒，。！？@#$%&";
        GlyphGrid.ItemsSource = chars.Select(c => c.ToString()).ToArray();
    }

    private void FontsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontsGrid.SelectedItem is not FontItem font) return;
        PreviewTitle.Text = font.DisplayName;
        PreviewPath.Text = font.FilePath;
        try { PreviewText.FontFamily = font.PreviewFamily; } catch { PreviewText.FontFamily = new FontFamily("Segoe UI"); }
        GlyphGrid.FontFamily = PreviewText.FontFamily;
        PreviewOptionsChanged(sender, e);
        ShowTagEditor(InstalledTagEditor, font.FilePath, font.FamilyName, font.StyleName);
    }

    private void ListPreviewOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (ListPreviewInput is null || ListSizeSlider is null) return;
        ListPreviewText = string.IsNullOrEmpty(ListPreviewInput.Text) ? " " : ListPreviewInput.Text;
        ListPreviewSize = ListSizeSlider.Value;
        if (PreviewInput is not null) PreviewInput.Text = ListPreviewText;
    }

    private void PreviewOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (PreviewText is null || PreviewInput is null || SizeSlider is null || GlyphGrid is null) return;
        PreviewText.Text = PreviewInput.Text;
        if (CoverageText is not null) CoverageText.Text = FontsGrid.SelectedItem is FontItem selected ? FontCoverage.Describe(selected, PreviewInput.Text) : "";
        PreviewText.FontSize = SizeSlider.Value;
        if (PreviewStyleCombo?.SelectedItem is ComboBoxItem item)
        {
            var style = item.Tag?.ToString();
            PreviewText.FontWeight = style is "Bold" or "BoldItalic" ? FontWeights.Bold : FontWeights.Normal;
            PreviewText.FontStyle = style is "Italic" or "BoldItalic" ? FontStyles.Italic : FontStyles.Normal;
            GlyphGrid.FontWeight = PreviewText.FontWeight;
            GlyphGrid.FontStyle = PreviewText.FontStyle;
        }
    }

    private IEnumerable<FontItem> CheckedFonts() => _fonts.Where(x => x.IsSelected);

    private void SelectVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_fontView is null) return;
        foreach (FontItem font in _fontView) font.IsSelected = true;
        RefreshSelectionDisplay();
    }

    private void InvertVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_fontView is null) return;
        foreach (FontItem font in _fontView) font.IsSelected = !font.IsSelected;
        RefreshSelectionDisplay();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var font in _fonts) font.IsSelected = false;
        RefreshSelectionDisplay();
    }

    private void ItemCheck_Click(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(RefreshSelectionDisplay);

    private void FontsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || FontsGrid.SelectedItems.Count == 0) return;
        var rows = FontsGrid.SelectedItems.Cast<FontItem>().ToList();
        var newValue = rows.Any(x => !x.IsSelected);
        foreach (var font in rows) font.IsSelected = newValue;
        RefreshSelectionDisplay();
        e.Handled = true;
    }

    private void RefreshSelectionDisplay()
    {
        UpdateCount();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        var selected = CheckedFonts().ToList();
        if (selected.Count == 0) { MessageBox.Show("请先勾选字体。", "加入分类"); return; }
        var dialog = new InputDialog("加入分类", "输入分类名称：") { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        AddFontsToCategory(dialog.Value.Trim(), selected, false);
        StatusText.Text = $"已将 {selected.Count} 个字体加入“{dialog.Value.Trim()}”";
    }

    private void AddFontsToCategory(string requestedName, IEnumerable<FontItem> fonts, bool lockCategory)
    {
        var category = _settings.Categories.Keys.FirstOrDefault(x => x.Equals(requestedName, StringComparison.OrdinalIgnoreCase)) ?? requestedName;
        if (!_settings.Categories.TryGetValue(category, out var keys)) _settings.Categories[category] = keys = [];
        foreach (var font in fonts.DistinctBy(x => x.CanonicalKey, StringComparer.OrdinalIgnoreCase))
        {
            if (!keys.Contains(font.CanonicalKey, StringComparer.OrdinalIgnoreCase)) keys.Add(font.CanonicalKey);
            if (lockCategory) SetUserLock(font, true);
        }
        RefreshFontCategoryText();
        SaveSettings();
        RefreshCategoryChoices();
        FontsGrid.Items.Refresh();
        UpdateCount();
    }

    private void RefreshFontCategoryText()
    {
        foreach (var font in _fonts)
            font.CategoriesText = string.Join(", ", _settings.Categories.Where(x => x.Value.Contains(font.CanonicalKey, StringComparer.OrdinalIgnoreCase)).Select(x => x.Key).OrderBy(x => x));
    }

    private void ToggleCategoryLock_Click(object sender, RoutedEventArgs e)
    {
        var category = SelectedCategory;
        if (string.IsNullOrWhiteSpace(category) || category == AllCategories) { MessageBox.Show("请在左栏仅选择一个自定义分类。", "锁定分类"); return; }
        if (!_settings.Categories.TryGetValue(category, out var keys) || keys.Count == 0) return;
        var members = _fonts.Where(x => keys.Contains(x.CanonicalKey, StringComparer.OrdinalIgnoreCase)).ToList();
        var shouldLock = members.Any(x => !x.IsLocked);
        foreach (var font in members.Where(x => !x.IsProtected)) SetUserLock(font, shouldLock);
        SaveSettings(); RefreshSelectionDisplay(); AiResultsGrid.Items.Refresh();
        StatusText.Text = shouldLock ? $"已锁定分类“{category}”中的 {members.Count} 个字体" : $"已解除分类“{category}”的自定义锁定（系统锁定保持不变）";
    }

    private void UnlockSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = CheckedFonts().ToList();
        if (selected.Count == 0) { MessageBox.Show("请先勾选要解锁的字体。", "解锁所选"); return; }
        var unlocked = selected.Count(x => x.IsUserLocked);
        foreach (var font in selected) SetUserLock(font, false);
        SaveSettings();
        RefreshSelectionDisplay();
        AiResultsGrid.Items.Refresh();
        var protectedCount = selected.Count(x => x.IsProtected);
        StatusText.Text = $"已解除 {unlocked} 个字体的自定义锁定" +
            (protectedCount > 0 ? $"；{protectedCount} 个自动保护字体仍保留删除保护，可正常打标签" : "");
    }

    private void SetUserLock(FontItem font, bool locked)
    {
        font.IsUserLocked = locked;
        _settings.LockedFonts.RemoveAll(x => x.Equals(font.CanonicalKey, StringComparison.OrdinalIgnoreCase));
        if (locked) _settings.LockedFonts.Add(font.CanonicalKey);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_isBulkDeleting) return;
        var selected = CheckedFonts().ToList();
        if (selected.Count == 0) { MessageBox.Show("请先勾选字体。", "备份并删除"); return; }
        var deletable = selected.Where(x => !x.IsLocked).ToList();
        var blocked = selected.Count - deletable.Count;
        if (deletable.Count == 0) { MessageBox.Show("选中的字体均已锁定，禁止删除。", "安全保护", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var message = $"即将备份并删除 {deletable.Count} 个字体。\n备份目录：{_settings.BackupRoot}" + (blocked > 0 ? $"\n另有 {blocked} 个锁定字体会被跳过。" : "") + "\n\n继续吗？";
        if (MessageBox.Show(message, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var errors = new List<string>();
        var done = 0;
        _isBulkDeleting = true;
        DeleteFontsButton.IsEnabled = false;
        App.SuppressMissingFontErrors = true;
        PreviewText.FontFamily = new FontFamily("Segoe UI");
        GlyphGrid.FontFamily = new FontFamily("Segoe UI");
        PreviewTitle.Text = "正在备份并删除字体…";
        PreviewPath.Text = "操作完成后会自动刷新字体库";
        FontsGrid.ItemsSource = null;
        IProgress<(int Done, int Total, string Name)> progress = new Progress<(int Done, int Total, string Name)>(p => StatusText.Text = $"正在处理 {p.Done} / {p.Total}：{p.Name}");
        try
        {
            var result = await Task.Run(() =>
            {
                var localErrors = new List<string>();
                var localDone = 0;
                for (var i = 0; i < deletable.Count; i++)
                {
                    var font = deletable[i];
                    progress.Report((i + 1, deletable.Count, font.DisplayName));
                    try { _fontService.BackupAndRemove(font, _settings.BackupRoot, false); localDone++; }
                    catch (Exception ex) { localErrors.Add($"{font.DisplayName}: {ex.Message}"); }
                }
                FontService.NotifyFontCollectionChanged();
                return (Done: localDone, Errors: localErrors);
            });
            done = result.Done;
            errors.AddRange(result.Errors);
        }
        finally
        {
            try { await RefreshAsync(); }
            finally
            {
                App.SuppressMissingFontErrors = false;
                _isBulkDeleting = false;
                DeleteFontsButton.IsEnabled = true;
                if (FontsGrid.ItemsSource is null) FontsGrid.ItemsSource = _fontView;
            }
        }
        var summary = $"成功删除 {done} 个字体，失败 {errors.Count} 个。";
        if (errors.Count > 0) summary += "\n\n" + string.Join("\n", errors.Take(10));
        MessageBox.Show(summary, "操作完成", MessageBoxButton.OK, errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void BrowseAiFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_aiScanCancellation is not null) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "添加扫描目录（可多选）", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        _settings.IllustratorScanRoots = _settings.IllustratorScanRoots.Concat(dialog.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        UpdateAiFolders();
        SaveSettings();
    }

    private void UpdateAiFolders() => AiScanPathBox.Text = string.Join("\n", _settings.IllustratorScanRoots);

    private void RemoveAiFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_aiScanCancellation is not null) return;
        var line = AiScanPathBox.GetLineIndexFromCharacterIndex(AiScanPathBox.CaretIndex);
        if (line < 0 || line >= _settings.IllustratorScanRoots.Count) return;
        _settings.IllustratorScanRoots.RemoveAt(line);
        UpdateAiFolders(); SaveSettings();
    }

    private bool MatchesAiFilter(IllustratorFontUsage item)
    {
        var query = AiSearchBox.Text.Trim();
        if (query.Length > 0 && !item.FontName.Contains(query, StringComparison.OrdinalIgnoreCase) && !item.InstalledName.Contains(query, StringComparison.OrdinalIgnoreCase) && !item.Files.Any(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))) return false;
        return AiStatusFilter.SelectedIndex switch {
            1 => item.MatchedFont is not null,
            2 => item.MatchedFont is not null && !item.MatchedFont.IsLocked,
            3 => item.MatchedFont?.IsLocked == true,
            4 => item.MatchedFont is null,
            5 => item.MatchedFont is null && item.MatchedBackup is not null,
            6 => item.MatchedFont is null && item.MatchedBackup is null,
            _ => true };
    }

    private void AiFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (AiResultsGrid is null || AiSearchBox is null || AiStatusFilter is null || AiFilterCountText is null) return;
        var view = CollectionViewSource.GetDefaultView(_illustratorFonts);
        view.Filter = value => value is IllustratorFontUsage item && MatchesAiFilter(item);
        AiResultsGrid.ItemsSource = view;
        view.Refresh();
        AiFilterCountText.Text = $"显示 {view.Cast<object>().Count()} / {_illustratorFonts.Count} 种字体";
    }

    private async void ScanAi_Click(object sender, RoutedEventArgs e)
    {
        var roots = _settings.IllustratorScanRoots.ToArray();
        if (roots.Length == 0 || roots.Any(root => !Directory.Exists(root))) { MessageBox.Show("请先选择有效的扫描目录。", "Illustrator 字体检查"); return; }
        if (_aiScanCancellation is not null) return;
        _aiScanCancellation = new CancellationTokenSource();
        ScanAiButton.IsEnabled = false;
        CancelAiScanButton.IsEnabled = true;
        _illustratorFonts.Clear();
        AiResultsGrid.ItemsSource = _illustratorFonts;
        var progress = new Progress<(int Done, int Total, string File)>(p =>
        {
            AiScanStatusText.Text = p.Total == 0 ? "未找到 .ai 文件" : $"正在扫描 {p.Done} / {p.Total}：{Path.GetFileName(p.File)}";
        });
        try
        {
            var result = await Task.Run(() => _illustratorScanner.Scan(roots, _fonts.ToList(), progress, _aiScanCancellation.Token));
            var backups = await Task.Run(() => _fontService.LoadBackups(_settings.BackupRoot));
            foreach (var item in result.Fonts)
            {
                if (item.MatchedFont is null) item.MatchedBackup = IllustratorScanner.MatchBackupFont(item.FontName, backups);
                item.IsSelected = false;
                _illustratorFonts.Add(item);
            }
            AiFilter_Changed(this, new RoutedEventArgs());
            var fileCount = result.Fonts.SelectMany(x => x.Files).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var backupMatches = result.Fonts.Count(x => x.MatchedFont is null && x.MatchedBackup is not null);
            AiScanStatusText.Text = result.Cancelled
                ? $"已取消：完成 {result.ScannedFileCount} / {result.DiscoveredFileCount} 个 AI 文件，保留已发现的 {result.Fonts.Count} 种字体。"
                : $"完成：从 {fileCount} 个包含字体声明的 AI 文件中发现 {result.Fonts.Count} 种字体；匹配本机 {result.Fonts.Count(x => x.MatchedFont is not null)} 种，缺失 {result.Fonts.Count(x => x.MatchedFont is null)} 种，其中备份可安装 {backupMatches} 种。";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error); AiScanStatusText.Text = "扫描失败"; }
        finally
        {
            _aiScanCancellation?.Dispose();
            _aiScanCancellation = null;
            ScanAiButton.IsEnabled = true;
            CancelAiScanButton.IsEnabled = false;
        }
    }

    private void CancelAiScan_Click(object sender, RoutedEventArgs e)
    {
        if (_aiScanCancellation is null) return;
        AiScanStatusText.Text = "正在取消扫描…已完成的结果会保留。";
        CancelAiScanButton.IsEnabled = false;
        _aiScanCancellation.Cancel();
    }

    private void SelectAllAiMatches_Click(object sender, RoutedEventArgs e) { foreach (var item in _illustratorFonts) item.IsSelected = MatchesAiFilter(item) && item.MatchedFont is not null && !item.MatchedFont.IsLocked; AiResultsGrid.Items.Refresh(); }
    private void SelectAllAiBackups_Click(object sender, RoutedEventArgs e) { foreach (var item in _illustratorFonts) item.IsSelected = MatchesAiFilter(item) && item.MatchedFont is null && item.MatchedBackup is not null; AiResultsGrid.Items.Refresh(); }
    private void ClearAiSelection_Click(object sender, RoutedEventArgs e) { foreach (var item in _illustratorFonts) item.IsSelected = false; AiResultsGrid.Items.Refresh(); }

    private async void InstallAiFontsFromBackup_Click(object sender, RoutedEventArgs e)
    {
        var selected = _illustratorFonts.Where(x => x.IsSelected && x.MatchedFont is null && x.MatchedBackup is not null).ToList();
        if (selected.Count == 0) { MessageBox.Show("请先勾选状态为“缺失 · 备份可安装”的字体。", "从备份安装"); return; }
        var entries = selected.Select(x => x.MatchedBackup!).DistinctBy(x => x.BackupPath, StringComparer.OrdinalIgnoreCase).ToList();
        if (MessageBox.Show($"将从备份目录安装 {entries.Count} 个字体。\n\n继续吗？", "从备份安装", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        var errors = new List<string>();
        var done = 0;
        foreach (var entry in entries)
        {
            try { await Task.Run(() => _fontService.Restore(entry)); done++; }
            catch (Exception ex) { errors.Add($"{entry.DisplayName}: {ex.Message}"); }
        }
        await RefreshAsync();
        foreach (var item in _illustratorFonts)
        {
            item.MatchedFont = IllustratorScanner.MatchInstalledFontForUsage(item.FontName, _fonts.ToList());
            if (item.MatchedFont is not null) { item.MatchedBackup = null; item.IsSelected = false; }
        }
        AiResultsGrid.Items.Refresh();
        var missing = _illustratorFonts.Count(x => x.MatchedFont is null);
        AiScanStatusText.Text = $"已从备份安装 {done} 个字体；仍缺失 {missing} 种。";
        MessageBox.Show($"成功安装 {done} 个字体，失败 {errors.Count} 个。" + (errors.Count > 0 ? "\n\n" + string.Join("\n", errors.Take(10)) : ""), "安装完成", MessageBoxButton.OK, errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private List<FontItem> SelectedAiMatchedFonts() => _illustratorFonts.Where(x => x.IsSelected && x.MatchedFont is not null).Select(x => x.MatchedFont!).DistinctBy(x => x.CanonicalKey, StringComparer.OrdinalIgnoreCase).ToList();

    private void SelectAiFontsInLibrary_Click(object sender, RoutedEventArgs e)
    {
        var fonts = SelectedAiMatchedFonts();
        if (fonts.Count == 0) { MessageBox.Show("请先勾选已匹配且未锁定的字体。", "选择字体"); return; }
        foreach (var font in _fonts) font.IsSelected = false;
        foreach (var font in fonts) font.IsSelected = true;
        ClearFacetFilters_Click(this, new RoutedEventArgs());
        SearchBox.Clear();
        Tabs.SelectedIndex = 0;
        RefreshSelectionDisplay();
        StatusText.Text = $"已在字体库中选中 {fonts.Count} 个 Illustrator 项目字体";
    }

    private void AddAiFontsToLockedCategory_Click(object sender, RoutedEventArgs e)
    {
        var fonts = SelectedAiMatchedFonts();
        if (fonts.Count == 0) { MessageBox.Show("请先勾选已匹配且未锁定的字体。", "加入分类并锁定"); return; }
        var dialog = new InputDialog("项目字体分类", "输入分类名称（例如：客户A项目）：") { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        AddFontsToCategory(dialog.Value.Trim(), fonts, true);
        AiResultsGrid.Items.Refresh();
        StatusText.Text = $"已将 {fonts.Count} 个 Illustrator 项目字体加入“{dialog.Value.Trim()}”并锁定";
    }

    private void RefreshBackups()
    {
        _backups.Clear(); foreach (var item in _fontService.LoadBackups(_settings.BackupRoot)) _backups.Add(item);
        BackupsGrid.ItemsSource = _backups;
        BackupPathText.Text = "备份目录：" + _settings.BackupRoot;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var selected = BackupsGrid.SelectedItems.Cast<BackupEntry>().Where(x => x.Status != "已恢复").ToList();
        if (selected.Count == 0) { MessageBox.Show("请选择要恢复的备份记录。", "恢复字体"); return; }
        var errors = new List<string>(); var done = 0;
        foreach (var item in selected)
        {
            try { await Task.Run(() => _fontService.Restore(item)); done++; }
            catch (Exception ex) { errors.Add($"{item.DisplayName}: {ex.Message}"); }
        }
        RefreshBackups(); await RefreshAsync();
        MessageBox.Show($"成功恢复 {done} 个字体，失败 {errors.Count} 个。" + (errors.Count > 0 ? "\n" + string.Join("\n", errors) : ""), "恢复完成");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择字体备份目录", InitialDirectory = Directory.Exists(_settings.BackupRoot) ? _settings.BackupRoot : null };
        if (dialog.ShowDialog() != true) return;
        _settings.BackupRoot = dialog.FolderName;
        SaveSettings(); RefreshBackups();
    }
}

public sealed class InputDialog : Window
{
    private readonly TextBox _input = new();
    public string Value => _input.Text;

    public InputDialog(string title, string prompt)
    {
        Title = title; Width = 430; Height = 175; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 9) });
        Grid.SetRow(_input, 1); root.Children.Add(_input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "确定", IsDefault = true }; ok.Click += (_, _) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "取消", IsCancel = true };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _input.Focus();
    }
}
