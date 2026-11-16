using FontHarbor;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

class Checks
{
    [STAThread]
    static int Main()
    {
        try
        {
            if(Environment.GetEnvironmentVariable("FH_OLLAMA_SPEED_CHECK")=="1"){OllamaSpeedChecks.Run();return 0;}
            if(Environment.GetEnvironmentVariable("FH_API_CHECK_ONLY")=="1"){ApiChecks.Run();return 0;}
            UpdateChecks.Run();
            var root = Directory.GetCurrentDirectory();
            if(Environment.GetEnvironmentVariable("FH_LIVE_CHECK")=="1"){
                using var client=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=65536};client.DefaultRequestHeaders.UserAgent.ParseAdd("FontHarbor/1.3.16");
                var check=ReleaseUpdate.FetchAsync(client).GetAwaiter().GetResult();
                Console.WriteLine("LIVE source="+(check.Fallback?"GitHub":"website")+" newer="+ReleaseUpdate.NewerVersion(check.Json,new Version(1,3,15,0)));return 0;
            }
            var windowsFonts=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var systemUi=new FontItem{IsSystem=true,FilePath=Path.Combine(windowsFonts,"segoeui.ttf"),FamilyName="Localized UI name"};
            SystemFontProtection.Apply(systemUi);
            if(!systemUi.IsProtected || systemUi.SystemProtectionKind!="系统界面")throw new Exception("UI protection missing");
            var chinese=new FontItem{IsSystem=true,FilePath=Path.Combine(windowsFonts,"msyh.ttc")};SystemFontProtection.Apply(chinese);
            if(chinese.SystemProtectionKind!="语言支持")throw new Exception("Chinese protection missing");
            var compatibility=new FontItem{IsSystem=true,FilePath=Path.Combine(windowsFonts,"arial.ttf")};SystemFontProtection.Apply(compatibility);
            if(compatibility.SystemProtectionKind!="Windows预装")throw new Exception("Preinstalled compatibility must be distinct from UI essentials");
            var thirdParty=new FontItem{IsSystem=true,FilePath=Path.Combine(windowsFonts,"ArialCustom.ttf"),FamilyName="Arial Custom"};SystemFontProtection.Apply(thirdParty);
            var userCopy=new FontItem{IsSystem=false,FilePath=Path.Combine(windowsFonts,"arial.ttf")};SystemFontProtection.Apply(userCopy);
            var outside=new FontItem{IsSystem=true,FilePath=Path.Combine(Path.GetTempPath(),"segoeui.ttf")};SystemFontProtection.Apply(outside);
            if(thirdParty.IsProtected || userCopy.IsProtected || outside.IsProtected)throw new Exception("Third-party or user font misidentified as Windows preinstall");
            try{new FontService().BackupAndRemove(systemUi,Path.GetTempPath());throw new Exception("Protected deletion allowed");}catch(InvalidOperationException){}
            var lockedFont = new FontItem { IsUserLocked = true };
            lockedFont.IsSelected = true;
            if (!lockedFont.IsSelected || !lockedFont.CanSelect) throw new Exception("Locked font selection failed");
            lockedFont.IsUserLocked = false;
            if (lockedFont.IsLocked || !lockedFont.IsSelected) throw new Exception("Unlock must preserve selection");
            lockedFont.IsUserLocked = true;
            if (!lockedFont.IsSelected) throw new Exception("Lock must preserve selection");
            var protectedFont = new FontItem { IsProtected = true, IsSelected = true };
            if (!protectedFont.IsSelected || !protectedFont.CanSelect || !protectedFont.IsLocked) throw new Exception("Protected font selection/deletion protection failed");
            ApiChecks.Run();
            var result = FolderFontScanner.Scan(Path.Combine(root,"validation","fixtures"),CancellationToken.None);
            if(result.Fonts.Count == 0 || result.Failed != 1) throw new Exception("Recursive scan/corrupt-font check failed");
            if(!result.Fonts[0].PreviewFamily.GetTypefaces().Any(t=>t.TryGetGlyphTypeface(out _))) throw new Exception("File preview failed");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { FolderFontScanner.Scan(root,cancelled.Token); throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) { }
            var scan=System.Threading.Tasks.Task.Run(()=>new FontService().Scan(new AppSettings()));
            if(!scan.Wait(TimeSpan.FromSeconds(20))) throw new Exception("Installed registry scan exceeded 20 seconds");
            if(scan.Result.Count==0) throw new Exception("Installed scan empty");
            Console.WriteLine($"PASS: installed registry scan returned {scan.Result.Count} entries.");
            var app = new App(); app.InitializeComponent(); app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var testData=Path.Combine(root,"validation","settings-test");
            var window = new MainWindow(testData);
            TagWorkflowChecks.Run(window,Path.Combine(root,"validation","fixtures","nested","arial.ttf"));
            var fonts = (DataGrid)window.FindName("FontsGrid");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var collection=(System.Collections.ObjectModel.ObservableCollection<FontItem>)typeof(MainWindow).GetField("_fonts",flags)!.GetValue(window)!;
            var a=new FontItem {FamilyName="Arial",DisplayName="Arial Regular",StyleName="Regular",FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"arial.ttf")};
            var b=new FontItem {FamilyName="Microsoft YaHei",DisplayName="微软雅黑",StyleName="Regular",FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc")};
            var arialGlyph = new GlyphTypeface(new Uri(a.FilePath));
            if (!FontCoverage.Describe(arialGlyph, "ABC123").Contains("均受支持")) throw new Exception("Latin coverage failed");
            if (!FontCoverage.Describe(arialGlyph, "中中").Contains("缺少 1 种")) throw new Exception("Missing character deduplication failed");
            if (!FontCoverage.Describe(arialGlyph, "\U00020000").Contains("缺少 1 种")) throw new Exception("Supplementary Unicode coverage failed");
            if (!FontCoverage.Describe(new FontItem { IsCollection = true }, "中文").Contains("不判定")) throw new Exception("Collection must not claim coverage");
            var comparison = (Window)typeof(MainWindow).GetMethod("CreateComparison", flags | BindingFlags.Public)!.Invoke(window, new object[]{new List<FontItem>{a,b}})!;
            RenderOther(comparison,1000,720,"font-comparison.png");
            RenderOther(comparison,640,480,"font-comparison-small.png");
            comparison.Close();
            var recentSettings = new AppSettings { RecentFonts = [new RecentFont("Arial", "Regular", "ArialMT")] };
            var roundtrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(recentSettings))!;
            if (roundtrip.RecentFonts.Single().Name != "ArialMT") throw new Exception("Recent font persistence failed");
            Console.WriteLine("PASS: character coverage, supplementary Unicode, comparison layouts and recent font persistence.");
            collection.Add(a);collection.Add(b);
            var settings=(AppSettings)typeof(MainWindow).GetField("_settings",flags)!.GetValue(window)!;
            settings.Categories["常用字体"]=[a.CanonicalKey];settings.Categories["中文字体"]=[b.CanonicalKey];
            var view=System.Windows.Data.CollectionViewSource.GetDefaultView(collection);
            var filter=typeof(MainWindow).GetMethod("FilterFont",flags)!;
            view.Filter=o=>(bool)filter.Invoke(window,new[]{o})!;
            typeof(MainWindow).GetField("_fontView",flags)!.SetValue(window,view);fonts.ItemsSource=view;
            typeof(MainWindow).GetMethod("RefreshCategoryChoices",flags)!.Invoke(window,null);
            if(window.SourceTabsVisibility!=Visibility.Visible)throw new Exception("Full-mode navigation missing");
            Render(window,1380,820,"desktop.png");
            var setCompact=typeof(MainWindow).GetMethod("SetCompact",flags)!;
            setCompact.Invoke(window,new object[]{true});
            var categoryList=(ListBox)window.FindName("CompactCategoryList");
            if(((Border)window.FindName("CompactCategoryPanel")).Visibility!=Visibility.Visible || window.SourceTabsVisibility!=Visibility.Collapsed)throw new Exception("Compact category panel/navigation failed");
            if(window.CompactCategories.Count!=3 || window.CompactCategories[0].Count!=2)throw new Exception("Category counts failed");
            categoryList.SelectedItems.Add(window.CompactCategories.Single(c=>c.Name=="常用字体"));
            if(view.Cast<FontItem>().Single()!=a)throw new Exception("Category sidebar did not filter library");
            categoryList.SelectedItems.Add(window.CompactCategories.Single(c=>c.Name=="中文字体"));
            if(view.Cast<FontItem>().Count()!=2)throw new Exception("Within-group union failed");
            ((TextBox)window.FindName("SearchBox")).Text="Arial";
            if(view.Cast<FontItem>().Single()!=a)throw new Exception("Search intersection failed");
            ((TextBox)window.FindName("SearchBox")).Clear();
            var binding=typeof(MainWindow).GetMethod("BindingKey",BindingFlags.Static|BindingFlags.NonPublic)!;
            var aBinding=(string)binding.Invoke(null,new object[]{a.FilePath,a.FamilyName,a.StyleName})!;
            var bBinding=(string)binding.Invoke(null,new object[]{b.FilePath,b.FamilyName,b.StyleName})!;
            settings.FontTagBindings[aBinding]="test-a";settings.FontAnnotations["test-a"]=new FontAnnotation{Basic="无衬线",Styles=["科技"]};
            settings.FontTagBindings[bBinding]="test-b";settings.FontAnnotations["test-b"]=new FontAnnotation{Basic="衬线",Styles=["复古"]};
            typeof(MainWindow).GetMethod("RefreshCategorySidebar",flags)!.Invoke(window,null);
            var basic=(ListBox)window.FindName("BasicTagList");
            basic.SelectedItems.Add(basic.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="无衬线"));
            if(view.Cast<FontItem>().Single()!=a)throw new Exception("Cross-group intersection failed");
            var styleLists=(List<ListBox>)typeof(MainWindow).GetField("_styleLists",flags)!.GetValue(window)!;
            if(styleLists.Count!=7)throw new Exception("Grouped sidebar missing");
            var styles=styleLists.Single(l=>(string)l.Tag=="年代与文化");styles.SelectedItems.Add(styles.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="复古"));
            if(view.Cast<FontItem>().Any())throw new Exception("Style intersection failed");
            var science=styleLists.Single(l=>(string)l.Tag=="科技与工业");
            science.SelectedItems.Add(science.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="科技"));
            if(view.Cast<FontItem>().Single()!=a)throw new Exception("Styles across visual groups must use union");
            var groupedHost=(StackPanel)window.FindName("StyleGroupsHost");
            if(!groupedHost.Children.OfType<Expander>().Any(e=>e.Header.ToString()!.Contains("科技与工业 · 已选 1")))throw new Exception("Group selection count missing");
            typeof(MainWindow).GetMethod("ClearFacetFilters_Click",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
            var attributes=(ListBox)window.FindName("AttributeTagList");
            settings.FontAnnotations["test-a"].Attributes=["等宽"];
            typeof(MainWindow).GetMethod("RefreshCategorySidebar",flags)!.Invoke(window,null);
            attributes.SelectedItems.Add(attributes.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="等宽"));
            if(view.Cast<FontItem>().Single()!=a)throw new Exception("Independent attribute filtering failed");
            typeof(MainWindow).GetMethod("ClearFacetFilters_Click",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
            var status=(ListBox)window.FindName("TagStatusList");
            status.SelectedItems.Add(status.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="已完整"));
            if(view.Cast<FontItem>().Count()!=2)throw new Exception("Complete-state filtering failed");
            typeof(MainWindow).GetMethod("ClearFacetFilters_Click",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
            if(((StackPanel)window.FindName("StyleGroupsHost")).Children.OfType<Expander>().Any(e=>!e.IsExpanded))throw new Exception("Style groups must default to expanded");

            var categoryCards=((StackPanel)window.FindName("StyleGroupsHost")).Children.OfType<Expander>().ToList();
            if(categoryCards.Count!=7 || categoryCards.Select(c=>c.Resources["CategoryInk"].ToString()).Distinct().Count()!=7)throw new Exception("Seven distinct pastel groups required");
            foreach(var card in categoryCards)if(card.Style!=(Style)app.FindResource("StyleAccordion"))throw new Exception("Category card style missing");
            if(view.Cast<FontItem>().Count()!=2 || basic.SelectedItems.Count!=0 || styleLists.Any(l=>l.SelectedItems.Count!=0))throw new Exception("Clear filters failed");
            Render(window,920,620,"compact.png");Render(window,800,500,"compact-small.png");
            var originalSidebarWidth=((ColumnDefinition)window.FindName("CompactCategoryColumn")).Width;
            foreach(var sidebarWidth in new[]{160.0,300.0})
            {
                ((ColumnDefinition)window.FindName("CompactCategoryColumn")).Width=new GridLength(sidebarWidth);
                Render(window,800,500,$"compact-action-{sidebarWidth}.png");
                var actions=Desc<Button>(fonts).Where(b=>b.Content?.ToString()=="应用 ↗").ToList();
                if(actions.Count==0)throw new Exception("Compact apply buttons missing");
                foreach(var action in actions)
                {
                    var point=action.TranslatePoint(new Point(),fonts);
                    if(point.X<0 || point.X+action.ActualWidth>fonts.ActualWidth-2 || action.ActualWidth<68)throw new Exception("Compact apply button clipped");
                    var label=Desc<ContentPresenter>(action).First();
                    if(label.DesiredSize.Width>label.ActualWidth+0.5)throw new Exception("Compact apply label clipped");
                }
            }
            ((ColumnDefinition)window.FindName("CompactCategoryColumn")).Width=originalSidebarWidth;
            Render(window,920,620,"compact.png");
            Console.WriteLine("PASS: apply buttons and labels fit at minimum window width with narrow and wide sidebars.");
            if(fonts.Columns[1].ActualWidth<250 || fonts.Columns[5].ActualWidth<75)throw new Exception("Compact sample/apply columns collapsed");
            var selectFacet=typeof(MainWindow).GetMethod("SelectFacet",flags)!;
            var first=categoryList.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="常用字体");
            var second=categoryList.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="中文字体");
            selectFacet.Invoke(window,new object[]{categoryList,first,false});
            selectFacet.Invoke(window,new object[]{categoryList,second,false});
            if(categoryList.SelectedItems.Count!=1||view.Cast<FontItem>().Single()!=b)throw new Exception("Plain click must replace selection");
            selectFacet.Invoke(window,new object[]{categoryList,first,true});
            if(view.Cast<FontItem>().Count()!=2)throw new Exception("Shift click must add selection");
            selectFacet.Invoke(window,new object[]{categoryList,first,true});
            if(view.Cast<FontItem>().Single()!=b)throw new Exception("Shift click must remove selection");
            var sans=basic.Items.Cast<MainWindow.CategoryChoice>().Single(c=>c.Name=="无衬线");
            selectFacet.Invoke(window,new object[]{basic,sans,false});
            if(categoryList.SelectedItems.Count!=0||view.Cast<FontItem>().Single()!=a)throw new Exception("Plain click must clear other sidebar groups");
            var allStyles=FontTaxonomy.Styles.Select(IconGeometryConverter.For).ToList();
            if(allStyles.Count!=34||allStyles.Select(g=>g.ToString()).Distinct().Count()!=34)throw new Exception("Every style needs a unique icon");
            typeof(MainWindow).GetMethod("ClearFacetFilters_Click",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
            Console.WriteLine("PASS: compact column widths, plain/Shift selection across groups, 34 unique vector icons.");
            setCompact.Invoke(window,new object[]{false});
            if(window.SourceTabsVisibility!=Visibility.Visible||((Border)window.FindName("CompactCategoryPanel")).Visibility!=Visibility.Visible)throw new Exception("Full mode must preserve left facets");
            fonts.SelectedItem=a;
            var editor=(StackPanel)window.FindName("InstalledTagEditor");
            typeof(MainWindow).GetMethod("ShowTagEditor",flags)!.Invoke(window,new object[]{editor,a.FilePath,a.FamilyName,a.StyleName});
            IEnumerable<T> Desc<T>(DependencyObject parent) where T:DependencyObject
            {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Desc<T>(child))yield return nested;}
            }
            Render(window,1480,860,"tag-editor.png");
            var accordions=Desc<Expander>(editor).ToList();
            if(accordions.Count!=7)throw new Exception("Missing style accordions");
            if(accordions.Any(e=>!e.IsExpanded))throw new Exception("Editor groups must default to expanded");
            var groupIcons=FontTaxonomy.Groups.Select(g=>IconGeometryConverter.For(g.Name).ToString()).ToList();
            if(groupIcons.Distinct().Count()!=7||groupIcons.Intersect(FontTaxonomy.Styles.Select(t=>IconGeometryConverter.For(t).ToString())).Any())throw new Exception("Group icons must be distinct from one another and tag icons");
            foreach(var accordion in accordions)accordion.IsExpanded=true;
            window.UpdateLayout();
            var chips=accordions.SelectMany(e=>((WrapPanel)e.Content).Children.OfType<CheckBox>()).ToList();
            if(chips.Count!=34)throw new Exception("Missing style chips");
            Render(window,1480,1000,"style-wrapping.png");
            foreach(var accordion in accordions)
            {
                var wrap=(WrapPanel)accordion.Content;
                if(wrap.Children.Count>3 && wrap.Children.Cast<FrameworkElement>().Select(c=>c.TranslatePoint(new Point(0,0),wrap).Y).Distinct().Count()<2)
                    throw new Exception("Style tags must wrap within the detail panel: "+accordion.Header);
                if(!Desc<System.Windows.Shapes.Path>(accordion).Any())throw new Exception("Missing group icon: "+accordion.Header);
            }
            foreach(var width in new[]{180.0,260.0})
            {
                var measured=new WrapPanel{Width=width};
                foreach(var tag in FontTaxonomy.Groups[1].Tags)measured.Children.Add(new CheckBox{Content=tag,Style=(Style)app.FindResource("TagChip")});
                measured.Measure(new Size(width,double.PositiveInfinity));measured.Arrange(new Rect(0,0,width,measured.DesiredSize.Height));
                if(measured.Children.Cast<FrameworkElement>().Select(c=>c.TranslatePoint(new Point(),measured).Y).Distinct().Count()<2)throw new Exception("Narrow panel tags must wrap");
            }
            var editorParent=(StackPanel)editor.Parent;var editorIndex=editorParent.Children.IndexOf(editor);editorParent.Children.Remove(editor);
            var editorView=new ScrollViewer{Content=editor,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(20),Background=Brushes.White};
            var editorHost=new Window{Content=editorView,Width=360,Height=1350,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false,Opacity=0};editorHost.Show();editorHost.UpdateLayout();
            var editorBitmap=new RenderTargetBitmap(360,1350,96,96,PixelFormats.Pbgra32);editorBitmap.Render(editorView);var editorPng=new PngBitmapEncoder();editorPng.Frames.Add(BitmapFrame.Create(editorBitmap));using(var stream=File.Create(Path.Combine("validation","style-panel-wrapped.png")))editorPng.Save(stream);
            editorView.Content=null;editorHost.Close();editorParent.Children.Insert(editorIndex,editor);
            Console.WriteLine("PASS: group icons and real detail-panel multi-row wrapping, including narrow widths.");
            accordions[4].IsExpanded=false;accordions[4].IsExpanded=true;
            if(!accordions[4].IsExpanded)throw new Exception("Style accordion cannot reopen");
            foreach(var c in chips)c.IsChecked=false;
            foreach(var c in chips.Take(4))c.IsChecked=true;
            if(chips.Count(c=>c.IsChecked==true)!=3)throw new Exception("Three-style limit broken");
            var remove=Desc<Button>(editor).First(b=>b.Content.ToString()!.EndsWith("  ×"));remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(chips.Count(c=>c.IsChecked==true)!=2)throw new Exception("Selected chip removal failed");
            var splitter=(GridSplitter)window.FindName("CategorySplitter");
            Render(window,1480,860,"tag-editor.png");
            var shell=(FrameworkElement)window.Content;window.Content=null;var resizeHost=new Window{Content=shell,Width=1480,Height=860,Opacity=0,ShowInTaskbar=false};resizeHost.Show();resizeHost.UpdateLayout();
            var column=(ColumnDefinition)window.FindName("CompactCategoryColumn");var previous=column.ActualWidth;
            var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(resizeHost)!,0,System.Windows.Input.Key.Right){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent};
            typeof(GridSplitter).GetMethod("OnKeyDown",flags)!.Invoke(splitter,new object[]{key});
            resizeHost.UpdateLayout();
            if(column.Width.Value<=previous)throw new Exception("Splitter keyboard resize failed");
            typeof(MainWindow).GetMethod("CategorySplitter_DragCompleted",flags)!.Invoke(window,new object[]{splitter,new System.Windows.Controls.Primitives.DragCompletedEventArgs(10,0,false)});
            var savedWidth=settings.CategoryPanelWidth;resizeHost.Content=null;resizeHost.Close();window.Content=shell;
            setCompact.Invoke(window,new object[]{true});setCompact.Invoke(window,new object[]{false});
            if(column.Width.Value!=savedWidth)throw new Exception("Sidebar width lost after mode switch");
            Console.WriteLine("PASS: sidebar splitter resize/width memory, seven-group chip navigation, three-style limit and selected-chip removal.");
            Render(window,1480,860,"desktop.png");
            Render(window,1180,740,"desktop-small.png");
            var persist=typeof(MainWindow).GetMethod("PersistAnnotation",flags)!;
            persist.Invoke(window,new object[]{aBinding,"persistent-test",new FontAnnotation{Basic="无衬线",Styles=["科技"],Origin="手动修改",Model="人工编辑"}});
            var reloaded=new MainWindow(testData);typeof(MainWindow).GetMethod("LoadSettings",flags)!.Invoke(reloaded,null);
            var restored=(AppSettings)typeof(MainWindow).GetField("_settings",flags)!.GetValue(reloaded)!;
            if(restored.FontAnnotations["persistent-test"].Origin!="手动修改"||restored.FontTagBindings[aBinding]!="persistent-test"||!restored.Categories.ContainsKey("中文字体"))throw new Exception("Annotation/category persistence lost on restart");reloaded.Close();
            var blocked=Path.Combine(root,"validation","blocked-settings-file");File.WriteAllText(blocked,"test");var failing=new MainWindow(blocked);
            try{persist.Invoke(failing,new object[]{"binding","fingerprint",new FontAnnotation()});throw new Exception("Expected settings save failure");}catch(TargetInvocationException){}
            var rolled=(AppSettings)typeof(MainWindow).GetField("_settings",flags)!.GetValue(failing)!;
            if(rolled.FontAnnotations.Count!=0||rolled.FontTagBindings.Count!=0)throw new Exception("Failed persistence did not roll back");failing.Close();
            Console.WriteLine("PASS: annotation/category restart persistence and rollback on failed save, isolated from real user settings.");
            var api=new ApiSettingsWindow(new ApiProfile(),"","");RenderOther(api,620,660,"api-settings.png");api.Close();
            if(((Button)window.FindName("ApiSettingsButton")).FontWeight!=FontWeights.Normal)throw new Exception("UI weight too bold");
            Console.WriteLine("PASS: full three-column/compact layouts, category union, taxonomy intersection, search, clear and regular UI weights.");
            ((TabControl)window.FindName("Tabs")).SelectedIndex=1;
            ((DataGrid)window.FindName("ExternalGrid")).ItemsSource=result.Fonts;
            Render(window,1380,820,"folder.png");
            var script=(string)typeof(IllustratorBridge).GetMethod("BuildScript",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{"Arial","Regular","ArialMT"})!;
            File.WriteAllText(Path.Combine(root,"validation","apply-script.js"),script);
            Console.WriteLine("PASS: recursive font scan, invalid font, actual file preview, cancellation, compact/full layouts; screenshots saved.");
            var aiRoot=Path.Combine(root,"validation","ai-multi-fixtures");
            Directory.CreateDirectory(Path.Combine(aiRoot,"nested"));
            File.WriteAllText(Path.Combine(aiRoot,"one.ai"),"/BaseFont /Alpha-Regular");
            File.WriteAllText(Path.Combine(aiRoot,"nested","two.ai"),"/BaseFont /Alpha-Regular /BaseFont /Beta-Regular");
            var aiResult=new IllustratorScanner().Scan(new[]{aiRoot,Path.Combine(aiRoot,"nested"),aiRoot},new List<FontItem>());
            if(aiResult.ScannedFileCount!=2 || aiResult.Fonts.Count!=2 || aiResult.Fonts.Single(f=>f.FontName=="Alpha-Regular").FileCount!=2 || aiResult.Fonts.Any(f=>f.IsSelected))throw new Exception("Multi-folder dedup/default selection failed");
            var aiItems=(System.Collections.ObjectModel.ObservableCollection<IllustratorFontUsage>)typeof(MainWindow).GetField("_illustratorFonts",flags)!.GetValue(window)!;
            aiItems.Clear();
            aiItems.Add(new IllustratorFontUsage{FontName="Alpha",MatchedFont=new FontItem{DisplayName="Alpha"}});
            aiItems.Add(new IllustratorFontUsage{FontName="Beta",MatchedFont=new FontItem{DisplayName="Beta",IsUserLocked=true}});
            aiItems.Add(new IllustratorFontUsage{FontName="Missing"});
            var aiStatus=(ComboBox)window.FindName("AiStatusFilter");var search=(TextBox)window.FindName("AiSearchBox");var grid=(DataGrid)window.FindName("AiResultsGrid");
            aiStatus.SelectedIndex=2;if(grid.Items.Count!=1)throw new Exception("Unlocked filter failed");
            aiStatus.SelectedIndex=3;if(grid.Items.Count!=1)throw new Exception("Locked filter failed");
            aiStatus.SelectedIndex=4;if(grid.Items.Count!=1)throw new Exception("Missing filter failed");
            aiStatus.SelectedIndex=0;search.Text="alpha";if(grid.Items.Count!=1)throw new Exception("Search failed");
            typeof(MainWindow).GetMethod("SelectAllAiMatches_Click",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
            if(!aiItems[0].IsSelected || aiItems[1].IsSelected || aiItems[2].IsSelected)throw new Exception("Filtered select all failed");
            search.Text="";((TabControl)window.FindName("Tabs")).SelectedIndex=2;
            Render(window,1480,860,"illustrator-filters.png");Render(window,1180,740,"illustrator-filters-small.png");
            Console.WriteLine("PASS: multiple roots, overlapping-directory dedup, no default selection, status/search filters and filtered select-all.");
            ((ColumnDefinition)window.FindName("CompactCategoryColumn")).Width=new GridLength(300);
            ((TabControl)window.FindName("Tabs")).SelectedIndex=0;
            Render(window,1480,1000,"compact-facets.png");
            var basicItems=Enumerable.Range(0,basic.Items.Count).Select(i=>(ListBoxItem)basic.ItemContainerGenerator.ContainerFromIndex(i)).ToList();
            if(basicItems[0].TranslatePoint(new Point(),basic).Y!=basicItems[1].TranslatePoint(new Point(),basic).Y)throw new Exception("Basic types must share a row at normal sidebar width");
            Console.WriteLine("PASS: compact basic-type chips share rows and wrap within the sidebar.");
            var protectionReport=new SystemFontProtectionWindow(scan.Result);
            RenderOther(protectionReport,1060,680,"system-font-protection.png");protectionReport.Close();
            Console.WriteLine($"PASS: system font rules, protected deletion guard, and report with {scan.Result.Count(f=>f.IsWindowsFont)} Windows font entries.");
            if(Environment.GetEnvironmentVariable("FH_COVER")=="1"){
                typeof(MainWindow).GetMethod("ClearFacetFilters_Click",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
                settings.FontAnnotations.Clear();settings.FontTagBindings.Clear();settings.Categories.Clear();settings.Tags.Clear();
                collection.Clear();foreach(var font in scan.Result)collection.Add(font);
                typeof(MainWindow).GetMethod("ApplyFacetFilters",flags)!.Invoke(window,null);
                typeof(MainWindow).GetMethod("RefreshCategorySidebar",flags)!.Invoke(window,null);
                fonts.SelectedItem=collection.FirstOrDefault(f=>f.FilePath.EndsWith("arial.ttf",StringComparison.OrdinalIgnoreCase));
                Render(window,1600,1000,"real-font-library.png");
            }
            window.Close();app.Shutdown();return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
    static void RenderOther(Window window,int width,int height,string name)
    {
        var content=(FrameworkElement)window.Content;content.SetValue(System.Windows.Controls.Panel.BackgroundProperty,appBrush());window.Content=null;
        var host=new Window{Content=content,Width=width,Height=height,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Opacity=0};host.Show();host.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine("validation",name));png.Save(stream);host.Content=null;host.Close();window.Content=content;
    }
    static Brush appBrush() => (Brush)Application.Current.FindResource("AppBackground");
    static void Render(MainWindow window,int width,int height,string name)
    {
        var selectedCategories=((ListBox)window.FindName("CompactCategoryList")).SelectedItems.Cast<MainWindow.CategoryChoice>().Select(c=>c.Name).ToArray();
        var content=(FrameworkElement)window.Content;
        window.Content=null;
        content.SetValue(System.Windows.Controls.Panel.BackgroundProperty,appBrush());
        var host=new Window {DataContext=window.DataContext,Content=content,Width=width,Height=height,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
        host.Show();
        var categoryList=(ListBox)window.FindName("CompactCategoryList");
        foreach(var item in categoryList.Items.Cast<MainWindow.CategoryChoice>().Where(c=>selectedCategories.Contains(c.Name)))categoryList.SelectedItems.Add(item);
        host.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine("validation",name));png.Save(stream);
        host.Content=null;host.Close();window.Content=content;
    }
}


