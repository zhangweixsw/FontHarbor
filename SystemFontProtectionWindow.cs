using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FontHarbor;

public partial class MainWindow
{
    private async void ScanSystemFonts_Click(object sender,RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try { await RefreshAsync(); new SystemFontProtectionWindow(_fonts.ToList()) { Owner=this }.ShowDialog(); }
        catch(Exception ex) { MessageBox.Show(ex.Message,"系统字体保护"); }
        finally { button.IsEnabled=true; }
    }
}

public sealed class SystemFontProtectionWindow : Window
{
    public SystemFontProtectionWindow(IReadOnlyList<FontItem> fonts)
    {
        Title="系统字体保护清单"; Width=1060; Height=680; MinWidth=760; MinHeight=480;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel { Margin=new Thickness(20) }; Content=root;
        var header=new StackPanel { Margin=new Thickness(0,0,0,12) }; DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock { Text="Windows 字体已自动保护",FontSize=20,Margin=new Thickness(0,0,0,8) });
        header.Children.Add(new TextBlock { Text=$"系统界面 {fonts.Count(f=>f.SystemProtectionKind=="系统界面")} · 语言支持 {fonts.Count(f=>f.SystemProtectionKind=="语言支持")} · Windows 预装兼容 {fonts.Count(f=>f.SystemProtectionKind=="Windows预装")} · 集合保护 {fonts.Count(f=>f.IsCollection && !f.IsWindowsFont)}",TextWrapping=TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text="扫描和刷新字体时自动锁定，禁止在字港中删除；解锁所选只解除手动锁定。Windows 预装兼容字体不等于全部必需。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,8,0,0) });
        header.Children.Add(new TextBlock { Text="识别依据：系统级注册 + Windows Fonts 目录 + 官方文件名清单（含语言可选字体）；不将第三方系统级安装字体视为预装。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,FontSize=11,Margin=new Thickness(0,5,0,0) });
        var footer=new StackPanel { Margin=new Thickness(0,10,0,0) };DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text="清单来源："+SystemFontProtection.SourceUrl,FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray });
        var search=new TextBox { Margin=new Thickness(0,12,0,0),ToolTip="搜索字体名称、保护分类或原因" };header.Children.Add(search);
        var view=new ListCollectionView(fonts.Where(f=>f.IsWindowsFont || f.IsCollection).OrderBy(f=>f.SystemProtectionKind switch { "系统界面"=>0, "语言支持"=>1, "Windows预装"=>2, _=>3 }).ThenBy(f=>f.DisplayName).ToList());
        var grid=new DataGrid { AutoGenerateColumns=false,IsReadOnly=true,ItemsSource=view,RowHeight=double.NaN,CanUserAddRows=false };
        grid.Columns.Add(new DataGridTextColumn { Header="字体名称",Binding=new Binding("DisplayName"),Width=new DataGridLength(2,DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header="保护状态",Binding=new Binding("ProtectionText"),Width=150 });
        var wrap=new Style(typeof(TextBlock));wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));wrap.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,4,0,4)));
        grid.Columns.Add(new DataGridTextColumn { Header="锁定原因",Binding=new Binding("ProtectionReason"),Width=new DataGridLength(3,DataGridLengthUnitType.Star),ElementStyle=wrap });
        grid.Columns.Add(new DataGridTextColumn { Header="文件",Binding=new Binding("FilePath"),Width=new DataGridLength(2,DataGridLengthUnitType.Star),ElementStyle=wrap });
        root.Children.Add(grid);
        search.TextChanged+=(_,_)=> { var query=search.Text.Trim(); view.Filter=value=>value is FontItem f && (f.DisplayName.Contains(query,StringComparison.OrdinalIgnoreCase)||f.ProtectionText.Contains(query,StringComparison.OrdinalIgnoreCase)||f.ProtectionReason.Contains(query,StringComparison.OrdinalIgnoreCase)); };
    }
}

