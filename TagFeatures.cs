using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FontHarbor;
public sealed record TagTarget(string Path,string Family,string Style,FontFamily Preview,FontWeight Weight,FontStyle Slant)
{
    public string Name=>Family+" · "+Style;
}
public partial class MainWindow
{
    private readonly HashSet<string> _selectedCategories=new(StringComparer.OrdinalIgnoreCase),_selectedBasic=[],_selectedStyles=[],_selectedAttributes=[],_selectedStatus=[];
    private readonly List<ListBox> _styleLists=[];
    private readonly Dictionary<string,FontAnnotation?> _annotationCache=new(StringComparer.Ordinal);
    private string _apiSessionKey="",_visionSignature="";
    private FontTaggingWindow? _tagWindow;
    private string? SelectedCategory=>_selectedCategories.Count==1?_selectedCategories.First():null;
    private void InitializeTagging()
    {
        try{_apiSessionKey=UserSecret.Unprotect(_settings.AiProfile.ProtectedKey);}catch{_apiSessionKey="";StatusText.Text="已保存的 API Key 无法读取，请重新填写。";}
        RefreshCategorySidebar();
    }
    private static string BindingKey(string path,string family,string style)
    {
        var f=new FileInfo(path);
        return FontTaxonomy.PathKey(path,family,style)+"|"+(f.Exists?$"{f.Length}:{f.LastWriteTimeUtc.Ticks}":"missing");
    }
    private FontAnnotation? Annotation(string path,string family,string style)
    {
        try
        {
            var identity=FontTaxonomy.PathKey(path,family,style);
            if(_annotationCache.TryGetValue(identity,out var cached))return cached;
            var found=_settings.FontTagBindings.TryGetValue(BindingKey(path,family,style),out var key)&&_settings.FontAnnotations.TryGetValue(key,out var annotation)?annotation:null;
            if(found is not null)FontTaxonomy.NormalizeLegacy(found);
            _annotationCache[identity]=found;return found;
        }catch{return null;}
    }
    private bool MatchesFacets(string path,string family,string style)
    {
        var canonical=System.IO.Path.GetFullPath(path).ToLowerInvariant();
        if(_selectedCategories.Count>0&&!_selectedCategories.Any(c=>_settings.Categories.TryGetValue(c,out var keys)&&keys.Contains(canonical,StringComparer.OrdinalIgnoreCase)))return false;
        var a=Annotation(path,family,style);
        return (_selectedBasic.Count==0||_selectedBasic.Contains(a?.Basic??"其他/待确认"))&&(_selectedStyles.Count==0||(a is not null&&a.Styles.Any(_selectedStyles.Contains)))&&(_selectedAttributes.Count==0||(a is not null&&a.Attributes.Any(_selectedAttributes.Contains)))&&(_selectedStatus.Count==0||_selectedStatus.Contains(FontTaxonomy.IsComplete(a)?"已完整":"待确认/未标注"));
    }
    private void RefreshCategorySidebar()
    {
        if(CompactCategoryList is null)return;
        _annotationCache.Clear();
        _syncingCategory=true;
        try
        {
            var items=Tabs.SelectedIndex==1?_externalFonts.Select(f=>(f.Path,f.Family,f.Style)).ToList():_fonts.Select(f=>(f.FilePath,f.FamilyName,f.StyleName)).ToList();
            foreach(var f in _fonts){var a=Annotation(f.FilePath,f.FamilyName,f.StyleName);f.TagsText=(a is null?"类型：未标注 · 风格：未标注":"类型："+a.Basic+" · 风格："+(a.Styles.Count>0?string.Join("、",a.Styles):"待确认"))+(_settings.Tags.TryGetValue(f.CanonicalKey,out var legacy)?" · "+string.Join(" · ",legacy):"");}
            foreach(var f in _externalFonts){var a=Annotation(f.Path,f.Family,f.Style);f.TagsText=a is null?"类型：未标注 · 风格：未标注":"类型："+a.Basic+" · 风格："+(a.Styles.Count>0?string.Join("、",a.Styles):"待确认");}
            FontsGrid.Items.Refresh();ExternalGrid.Items.Refresh();
            CompactCategories.Clear();CompactCategories.Add(new CategoryChoice(AllCategories,items.Count));
            foreach(var c in _settings.Categories.OrderBy(c=>c.Key))CompactCategories.Add(new CategoryChoice(c.Key,items.Count(f=>c.Value.Contains(System.IO.Path.GetFullPath(f.Item1).ToLowerInvariant(),StringComparer.OrdinalIgnoreCase))));
            foreach(var item in CompactCategories.Where(c=>_selectedCategories.Contains(c.Name)))CompactCategoryList.SelectedItems.Add(item);
            BasicTagList.SelectedItems.Clear();AttributeTagList.SelectedItems.Clear();TagStatusList.SelectedItems.Clear();
            BasicTagList.ItemsSource=FontTaxonomy.Basic.Select(t=>new CategoryChoice(t,items.Count(f=>(Annotation(f.Item1,f.Item2,f.Item3)?.Basic??"其他/待确认")==t))).ToList();
            foreach(CategoryChoice item in BasicTagList.Items)if(_selectedBasic.Contains(item.Name))BasicTagList.SelectedItems.Add(item);
            StyleGroupsHost.Children.Clear();_styleLists.Clear();RefreshStyleFilterSummary();
            foreach(var group in FontTaxonomy.Groups)
            {
                var count=group.Tags.Count(_selectedStyles.Contains);
                var expander=new Expander{Style=(Style)FindResource("StyleAccordion"),Header=group.Name+(count>0?$" · 已选 {count}":""),IsExpanded=true,Margin=new Thickness(0,0,0,6),FontSize=12,FontWeight=FontWeights.Normal};
                var list=new ListBox{SelectionMode=SelectionMode.Multiple,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,ItemContainerStyle=(Style)FindResource("FacetChipItem"),Tag=group.Name};
                ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
                ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
                var factory=new FrameworkElementFactory(typeof(WrapPanel));list.ItemsPanel=new ItemsPanelTemplate(factory);
                var label=new FrameworkElementFactory(typeof(ContentControl));label.SetBinding(ContentControl.ContentProperty,new System.Windows.Data.Binding("Name"));label.SetValue(ContentControl.ContentTemplateProperty,FindResource("CategoryTagLabel"));list.ItemTemplate=new DataTemplate{VisualTree=label};
                list.ItemsSource=group.Tags.Select(t=>new CategoryChoice(t,items.Count(f=>Annotation(f.Item1,f.Item2,f.Item3)?.Styles.Contains(t)==true))).ToList();
                foreach(CategoryChoice item in list.Items)if(_selectedStyles.Contains(item.Name))list.SelectedItems.Add(item);
                list.SelectionChanged+=TagFacet_Changed;
                list.PreviewMouseLeftButtonDown+=Facet_PreviewMouseLeftButtonDown;
                ApplyCategoryPalette(expander,group.Name);
                expander.Content=list;_styleLists.Add(list);StyleGroupsHost.Children.Add(expander);
            }
            AttributeTagList.ItemsSource=new[]{new CategoryChoice("等宽",items.Count(f=>Annotation(f.Item1,f.Item2,f.Item3)?.Attributes.Contains("等宽")==true))};
            TagStatusList.ItemsSource=new[]{new CategoryChoice("已完整",items.Count(f=>FontTaxonomy.IsComplete(Annotation(f.Item1,f.Item2,f.Item3)))),new CategoryChoice("待确认/未标注",items.Count(f=>!FontTaxonomy.IsComplete(Annotation(f.Item1,f.Item2,f.Item3))))};
            foreach(CategoryChoice item in AttributeTagList.Items)if(_selectedAttributes.Contains(item.Name))AttributeTagList.SelectedItems.Add(item);
            foreach(CategoryChoice item in TagStatusList.Items)if(_selectedStatus.Contains(item.Name))TagStatusList.SelectedItems.Add(item);
            LockCategoryButton.IsEnabled=SelectedCategory is not null;
        }
        finally{_syncingCategory=false;}
    }
    private void RefreshStyleFilterSummary()
    {
        StyleFilterSummary.Children.Clear();
        StyleFilterSummary.Visibility=_selectedStyles.Count>0?Visibility.Visible:Visibility.Collapsed;
        if(_selectedStyles.Count==0)return;
        StyleFilterSummary.Children.Add(new TextBlock{Text=$"已选 {_selectedStyles.Count}",FontSize=10,Foreground=(Brush)FindResource("Accent"),Margin=new Thickness(0,5,6,5)});
        foreach(var tag in _selectedStyles.OrderBy(t=>t).ToList())
        {
            var remove=new Button{Content=tag+"  ×",ContentTemplate=null,FontSize=10,Padding=new Thickness(6,3,6,3),Margin=new Thickness(0,0,4,5),Background=(Brush)FindResource("AccentSoft")};
            remove.Click+=(_,_)=>{_selectedStyles.Remove(tag);RefreshCategorySidebar();ApplyFacetFilters();};StyleFilterSummary.Children.Add(remove);
        }
        var clear=new Button{Content="清空",ContentTemplate=null,Style=(Style)FindResource("QuietButton"),FontSize=10,Padding=new Thickness(5,3,5,3)};
        clear.Click+=(_,_)=>{_selectedStyles.Clear();RefreshCategorySidebar();ApplyFacetFilters();};StyleFilterSummary.Children.Add(clear);
    }
    private void SyncCategorySidebarSelection()=>RefreshCategorySidebar();
    private void CompactCategory_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_syncingCategory)return;
        _selectedCategories.Clear();
        if(e.AddedItems.OfType<CategoryChoice>().Any(c=>c.Name==AllCategories))
        {
            _syncingCategory=true;CompactCategoryList.SelectedItems.Clear();_syncingCategory=false;
        }
        else foreach(CategoryChoice c in CompactCategoryList.SelectedItems)if(c.Name!=AllCategories)_selectedCategories.Add(c.Name);
        ApplyFacetFilters();
    }
    private void TagFacet_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_syncingCategory)return;
        var list=(ListBox)sender;
        if(_styleLists.Contains(list))
        {
            var group=FontTaxonomy.Groups.Single(g=>g.Name==(string)list.Tag);
            foreach(var tag in group.Tags)_selectedStyles.Remove(tag);
            foreach(CategoryChoice c in list.SelectedItems)_selectedStyles.Add(c.Name);
            var expander=StyleGroupsHost.Children.OfType<Expander>().FirstOrDefault(e=>ReferenceEquals(e.Content,list));
            if(expander is not null){var count=group.Tags.Count(_selectedStyles.Contains);expander.Header=group.Name+(count>0?$" · 已选 {count}":"");}
        }
        else
        {
            var set=sender==BasicTagList?_selectedBasic:sender==AttributeTagList?_selectedAttributes:_selectedStatus;set.Clear();
            foreach(CategoryChoice c in list.SelectedItems)set.Add(c.Name);
        }
        RefreshStyleFilterSummary();ApplyFacetFilters();
    }
    private static void ApplyCategoryPalette(FrameworkElement card,string group)
    {
        var palette=group switch {
            "情绪感受" => ("#C18E96","#FCF6F7","#F8E9EC","#EFE2E5"),
            "年代与文化" => ("#B79B78","#FAF8F4","#F3ECE0","#EAE5DC"),
            "书写风格" => ("#A591BB","#F8F5FC","#EEE8F6","#E8E2EF"),
            "趣味装饰" => ("#C69B88","#FCF7F4","#F7ECE6","#EFE5DF"),
            "科技与工业" => ("#7FAAB2","#F3F9FA","#E5F1F3","#DFEAEC"),
            "形态与力度" => ("#8CA98F","#F5F9F5","#E9F1E9","#E1EAE1"),
            _ => ("#809AB9","#F4F7FB","#EAF0F8","#E4E9EF") };
        card.Resources["CategoryInk"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Item1));
        card.Resources["CategoryBackground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Item2));
        card.Resources["CategorySelected"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Item3));
        card.Resources["CategoryBorder"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Item4));
    }
    private void ApplyFacetFilters(){_fontView?.Refresh();ExternalFilter_Changed(this,null!);UpdateCount();LockCategoryButton.IsEnabled=SelectedCategory is not null;}
    private void ClearFacetFilters_Click(object sender,RoutedEventArgs e)
    {
        _selectedCategories.Clear();_selectedBasic.Clear();_selectedStyles.Clear();_selectedAttributes.Clear();_selectedStatus.Clear();RefreshCategorySidebar();ApplyFacetFilters();
    }
    private void SourceTab_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(e.Source!=Tabs||CompactCategoryList is null)return;RefreshCategorySidebar();
    }
    private void ExternalTagSelection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(ExternalGrid.SelectedItem is not ExternalFont f)return;
        ShowTagEditor(ExternalTagEditor,f.Path,f.Family,f.Style);
    }
    private void ShowTagEditor(StackPanel panel,string path,string family,string style)
    {
        panel.Children.Clear();System.Windows.Documents.TextElement.SetForeground(panel,(Brush)FindResource("TextPrimary"));var a=Annotation(path,family,style);
        panel.Children.Add(DialogUi.Text("分类与风格标签",16));
        panel.Children.Add(DialogUi.Text(a is null?"尚未标注":FontTaxonomy.IsComplete(a)?"状态：两个维度均已填写":"状态：待确认，可补扫或手动修正",10));
        var basic=new ComboBox{ItemsSource=FontTaxonomy.Basic,SelectedItem=a?.Basic??"其他/待确认"};
        DialogUi.Label(panel,"01 / 基础类型 · 单选");panel.Children.Add(basic);DialogUi.Label(panel,"02 / 进阶风格 · 合计选 1–3 个");
        panel.Children.Add(DialogUi.Text("分组仅便于查找，不必每组填写。",10));
        var countText=DialogUi.Text("",11);countText.Foreground=(Brush)FindResource("Accent");
        var selectedTags=new WrapPanel {Margin=new Thickness(0,8,0,4)};
        var summary=new StackPanel();summary.Children.Add(countText);summary.Children.Add(selectedTags);
        panel.Children.Add(new Border{Background=(Brush)FindResource("SurfaceAlt"),CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Margin=new Thickness(0,8,0,14),Child=summary});
        var options=new List<CheckBox>();
        var groupAccordions=new List<(Expander Expander,FontTaxonomy.StyleGroup Group)>();
        var chipStyle=(Style)FindResource("TagChip");
        foreach(var group in FontTaxonomy.Groups)
        {
            var wrap=new WrapPanel{Margin=new Thickness(0,2,0,4)};
            var accordion=new Expander{Header=group.Name,Style=(Style)FindResource("StyleAccordion"),Content=wrap,IsExpanded=true};
            ApplyCategoryPalette(accordion,group.Name);panel.Children.Add(accordion);groupAccordions.Add((accordion,group));
            foreach(var t in group.Tags){var c=new CheckBox{Content=t,IsChecked=a?.Styles.Contains(t)==true,Style=chipStyle};wrap.Children.Add(c);options.Add(c);}
        }
        var clear=new Button{Content="清空风格",Style=(Style)FindResource("QuietButton"),HorizontalAlignment=HorizontalAlignment.Right,FontSize=11};
        clear.Click+=(_,_)=>{foreach(var c in options)c.IsChecked=false;};panel.Children.Add(clear);
        var review=new CheckBox{Content="暂无法确定，标为待确认",IsChecked=a?.NeedsReview==true&&a.Styles.Count==0,Margin=new Thickness(0,6,0,14)};panel.Children.Add(review);
        void UpdateCount()
        {
            var chosen=options.Where(c=>c.IsChecked==true).ToList();
            countText.Text=$"已选风格  {chosen.Count} / 3"+(review.IsChecked==true?" · 待确认":"");selectedTags.Children.Clear();
            if(chosen.Count==0)selectedTags.Children.Add(DialogUi.Text("点击下方标签添加风格",11));
            foreach(var c in chosen){var remove=new Button{Content=c.Content+"  ×",Padding=new Thickness(8,4,8,4),FontSize=11,Margin=new Thickness(0,0,6,6),Background=(Brush)FindResource("AccentSoft"),ToolTip="移除此风格"};remove.Click+=(_,_)=>c.IsChecked=false;selectedTags.Children.Add(remove);}
            foreach(var (accordion,group) in groupAccordions){var n=chosen.Count(c=>group.Tags.Contains(c.Content.ToString()!));accordion.Header=group.Name+(n>0?$" · 已选 {n}":"");}
        }
        foreach(var c in options)
        {
            c.Checked+=(_,_)=>{if(options.Count(o=>o.IsChecked==true)>3){c.IsChecked=false;countText.Text="最多选择 3 项，请先移除一个已选风格。";}else{review.IsChecked=false;UpdateCount();}};
            c.Unchecked+=(_,_)=>UpdateCount();
        }
        review.Checked+=(_,_)=>{foreach(var c in options)c.IsChecked=false;UpdateCount();};review.Unchecked+=(_,_)=>UpdateCount();UpdateCount();
        var mono=new CheckBox{Content="等宽",IsChecked=a?.Attributes.Contains("等宽")==true,Style=chipStyle};
        var attributeRow=new WrapPanel();attributeRow.Children.Add(mono);attributeRow.Children.Add(new TextBlock{Text="字体属性 · 不占风格名额",FontSize=11,Foreground=(Brush)FindResource("TextSecondary"),Margin=new Thickness(4,7,0,0)});panel.Children.Add(attributeRow);
        var info=DialogUi.Text(a is null?"尚未标注。":$"状态：{(FontTaxonomy.IsComplete(a)?"已完整":"待确认")}\n来源：{a.Origin} · {a.Model}\n置信度：{(a.Confidence is null?"未提供":a.Confidence.Value.ToString("P0"))}",10);panel.Children.Add(info);
        if(a?.LegacyStyles.Count>0)panel.Children.Add(DialogUi.Text("保留的旧标签："+string.Join("、",a.LegacyStyles),10));
        Button? save=null;save=DialogUi.Button("保存标签",async(_,_)=>
        {
            save!.IsEnabled=false;
            try
            {
                var styles=options.Where(c=>c.IsChecked==true).Select(c=>c.Content.ToString()!).ToList();
                if(styles.Count>3||(styles.Count==0&&review.IsChecked!=true))throw new InvalidDataException("请选择 1 至 3 个风格，无法判断时明确勾选待确认状态。");
                var binding=BindingKey(path,family,style);var fingerprint=await Task.Run(()=>FontTaxonomy.Fingerprint(path,family,style));
                if(binding!=BindingKey(path,family,style))throw new IOException("字体文件已变化，请刷新后重试。");
                var annotation=new FontAnnotation{Basic=basic.SelectedItem?.ToString()??"其他/待确认",Styles=styles,Attributes=mono.IsChecked==true?["等宽"]:[],TaxonomyVersion=FontTaxonomy.Version,NeedsReview=review.IsChecked==true||basic.SelectedItem?.ToString()=="其他/待确认",Origin="手动修改",Model="人工编辑",LegacyStyles=a?.LegacyStyles.ToList()??[]};
                PersistAnnotation(binding,fingerprint,annotation);info.Text="标签已保存到本机。";RefreshCategorySidebar();ApplyFacetFilters();
            }
            catch(Exception ex){info.Text=DialogUi.Error(ex);}finally{save.IsEnabled=true;}
        });save.Style=(Style)FindResource("PrimaryButton");save.Margin=new Thickness(0,12,0,0);panel.Children.Add(save);
    }
    private void PersistAnnotation(string binding,string fingerprint,FontAnnotation annotation)
    {
        _settings.FontAnnotations.TryGetValue(fingerprint,out var old);_settings.FontTagBindings.TryGetValue(binding,out var oldBinding);
        _settings.FontAnnotations[fingerprint]=annotation;_settings.FontTagBindings[binding]=fingerprint;
        try{SaveSettings();_annotationCache.Clear();}catch{if(old is null)_settings.FontAnnotations.Remove(fingerprint);else _settings.FontAnnotations[fingerprint]=old;if(oldBinding is null)_settings.FontTagBindings.Remove(binding);else _settings.FontTagBindings[binding]=oldBinding;throw;}
    }
    private void ApiSettings_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new ApiSettingsWindow(_settings.AiProfile,_apiSessionKey,_visionSignature){Owner=(Window?)_tagWindow??this};
        if(dialog.ShowDialog()!=true)return;
        _settings.AiProfile=dialog.Profile;_apiSessionKey=dialog.SessionKey;_visionSignature=dialog.VisionSignature;SaveSettings();
        StatusText.Text=string.IsNullOrEmpty(_visionSignature)?"API 配置已保存。开始自动标注时将检查视觉能力。":"API 配置已保存，视觉能力验证通过。";
    }
    private void TagScan_Click(object sender,RoutedEventArgs e)
    {
        if(_tagWindow is not null){_tagWindow.Activate();return;}
        _tagWindow=new FontTaggingWindow(this,_settings.AiProfile,_apiSessionKey,_visionSignature,GetTagTargetsAsync,Annotation,PersistAnnotation,BindingKey){Owner=this};
        _tagWindow.Closed+=(_,_)=>{_tagWindow=null;RefreshCategorySidebar();ApplyFacetFilters();};_tagWindow.Show();
    }
    private async Task<List<TagTarget>> GetTagTargetsAsync(int source,string path,bool selected,CancellationToken token)
    {
        _annotationCache.Clear();
        var targets=new List<TagTarget>();
        var collections=new Dictionary<string,List<ExternalFont>>(StringComparer.OrdinalIgnoreCase);
        if(source==0)
        {
            foreach(var f in _fonts.Where(f=>!selected||f.IsSelected).ToList())
            {
                token.ThrowIfCancellationRequested();
                if(f.IsCollection)
                {
                    if(!collections.TryGetValue(f.FilePath,out var faces)){faces=await Task.Run(()=>FolderFontScanner.ReadFile(f.FilePath,System.IO.Path.GetDirectoryName(f.FilePath)!),token);collections[f.FilePath]=faces;}
                    var face=faces.FirstOrDefault(g=>g.Family.Equals(f.FamilyName,StringComparison.OrdinalIgnoreCase)&&g.Style.Equals(f.StyleName,StringComparison.OrdinalIgnoreCase))
                        ?? faces.FirstOrDefault(g=>g.Style.Equals(f.StyleName,StringComparison.OrdinalIgnoreCase)) ?? faces.FirstOrDefault();
                    if(face is not null)targets.Add(new TagTarget(f.FilePath,f.FamilyName,f.StyleName,face.PreviewFamily,face.Weight,face.Slant));
                }
                else targets.Add(new TagTarget(f.FilePath,f.FamilyName,f.StyleName,f.PreviewFamily,f.RenderWeight,f.RenderFontStyle));
            }
        }
        else
        {
            var root=source==1?_settings.BackupRoot:path;
            if(selected)
            {
                targets.AddRange(ExternalGrid.SelectedItems.Cast<ExternalFont>().Where(f=>System.IO.Path.GetFullPath(f.Path).StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)).Select(f=>new TagTarget(f.Path,f.Family,f.Style,f.PreviewFamily,f.Weight,f.Slant)));
            }
            else
            {
                var result=await Task.Run(()=>FolderFontScanner.Scan(root,token),token);
                targets.AddRange(result.Fonts.Select(f=>new TagTarget(f.Path,f.Family,f.Style,f.PreviewFamily,f.Weight,f.Slant)));
            }
        }
        return targets.DistinctBy(t=>FontTaxonomy.PathKey(t.Path,t.Family,t.Style)).ToList();
    }
    public (ApiProfile Profile,string Key,string Vision) ConfigureTaggingApi()
    {
        ApiSettings_Click(this,new RoutedEventArgs());
        return (_settings.AiProfile,_apiSessionKey,_visionSignature);
    }
    public void RememberTaggingVision(ApiProfile profile,string key,string signature)
    {
        if(ApiSettingsWindow.Signature(_settings.AiProfile,_apiSessionKey)==ApiSettingsWindow.Signature(profile,key))_visionSignature=signature;
    }
    public bool TryGetCachedAnnotation(string key,out FontAnnotation annotation)=>_settings.FontAnnotations.TryGetValue(key,out annotation!);
    public string CurrentFontFolder=>FontFolderCombo.SelectedItem?.ToString()??"";
    public string CurrentBackupFolder=>_settings.BackupRoot;
}
