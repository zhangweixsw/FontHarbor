using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
namespace FontHarbor;
public sealed class IconGeometryConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["通用气质"] = "M12 2 22 7 12 12 2 7ZM2 12l10 5 10-5M2 17l10 5 10-5",
        ["情绪感受"] = "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20M7 9h1M16 9h1M7 14q5 6 10 0",
        ["年代与文化"] = "M4 5h16v16H4ZM4 10h16M8 2v6M16 2v6M8 14h3M14 17h3",
        ["书写风格"] = "M5 4h9l5 5v12H5ZM14 4v5h5M8 13h8M8 17h5",
        ["趣味装饰"] = "M12 2 15 8 22 9 17 14 18 21 12 18 6 21 7 14 2 9 9 8Z",
        ["科技与工业"] = "M3 21V10l6 3V8l6 3V4h5v17ZM6 17h2M11 17h2M16 17h2",
        ["形态与力度"] = "M4 3v18M2 5h4M2 19h4M9 6h12v12H9ZM12 9l6 6M18 9l-6 6",
        ["简约"] = "M5 12h14",
        ["现代"] = "M4 20V9l8-5v16M12 12l8-4v12M7 12v2M7 17v1M15 13v2",
        ["商务"] = "M3 8h18v12H3ZM8 8V4h8v4M3 12q9 5 18 0",
        ["正式"] = "m9 3 3 3 3-3 3 5-4 3 1 9H9l1-9-4-3ZM12 6l-2 3 2 3 2-3Z",
        ["优雅"] = "M18 3C4 5 3 13 10 15c7 2 10-5 8-12ZM6 21 16 7",
        ["温暖"] = "M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10M12 2v2M12 20v2M2 12h2M20 12h2M5 5l2 2M17 17l2 2",
        ["亲切"] = "M12 20 4 12C-2 5 8 1 12 7c4-6 14-2 8 5Z",
        ["柔美"] = "M3 17C2 2 20 2 20 10c0 10-15 0-9-7M5 21c0-9 9-9 12-13",
        ["清新"] = "M4 19C2 7 9 3 20 4c0 12-5 16-16 15ZM4 20 15 9M9 15V9M9 15h6",
        ["可爱"] = "M5 9 4 3l6 4h4l6-4-1 6c5 12-19 12-14 0ZM8 12h1M15 12h1M10 16q2 2 4 0",
        ["趣味"] = "M5 6h14v12H5ZM8 10h1M15 10h1M8 14q4 5 8 0M3 4l2 2M19 18l2 2",
        ["古典"] = "M3 7 12 3l9 4ZM5 10v8M10 10v8M14 10v8M19 10v8M3 21h18",
        ["复古"] = "M12 4a8 8 0 1 1-7 4M3 3v6h6M12 7v5l3 2",
        ["中国风"] = "M3 9h18M5 9l7-6 7 6M6 9v10M18 9v10M3 20h18M9 13h6M12 13v7",
        ["新中式"] = "M4 5h16v15H4ZM4 10h16M10 10v10M10 15h10M8 3v2M16 3v2",
        ["民族风"] = "m12 2 9 10-9 10L3 12ZM12 6l5 6-5 6-5-6ZM12 10l2 2-2 2-2-2Z",
        ["硬笔手写"] = "m5 15 11-11 4 4L9 19l-6 2ZM5 15l4 4M14 6l4 4",
        ["毛笔书法"] = "M10 14 18 2l3 2-9 12ZM10 14c-6-1-3 6-8 7 7 1 13-3 10-5",
        ["连笔"] = "M2 15C4 2 11 3 8 13s-7 9-4 3c3-6 7 2 10-4s8-5 5 1-4 4-2 4h5",
        ["涂鸦"] = "m3 7 15-4L5 13l15-6L4 20l17-7M6 3l12 18",
        ["卡通"] = "M4 4h16v13h-7l-6 4v-4H4ZM8 9h1M15 9h1M8 12q4 3 8 0",
        ["装饰"] = "M12 2c-8 3-8 8 0 10-8 2-8 7 0 10 8-3 8-8 0-10 8-2 8-7 0-10ZM2 12h20",
        ["迷幻"] = "M12 12c-5-5 6-8 7-1s-10 13-15 5S5 0 14 3s11 15 3 19",
        ["哥特式"] = "M5 21V10l3-6 4 6 4-6 3 6v11ZM8 21v-8l4-5 4 5v8M12 13v8",
        ["科技"] = "M7 7h10v10H7ZM10 10h4v4h-4ZM9 3v4M15 3v4M9 17v4M15 17v4M3 9h4M3 15h4M17 9h4M17 15h4",
        ["机械"] = "m9 3 6 0 1 4 4 2-1 6-4 1-2 4-6-1-1-4-4-2 1-6 4-1ZM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6",
        ["几何"] = "m12 3 9 17H3ZM12 9l5 8H7Z",
        ["像素"] = "M4 4h6v6H4ZM14 4h6v6h-6ZM4 14h6v6H4ZM14 14h3v3h3v3h-6Z",
        ["故障"] = "M4 4h16v5H8v3h12v8H4v-5h12v-3M2 7h4M18 17h4",
        ["圆润"] = "M9 3h6a6 6 0 0 1 6 6v6a6 6 0 0 1-6 6H9a6 6 0 0 1-6-6V9a6 6 0 0 1 6-6Z",
        ["尖锐"] = "m12 2 3 8 7 2-7 3-3 7-2-7-8-3 8-2Z",
        ["力量"] = "M3 8v8M6 5v14M18 5v14M21 8v8M6 12h12",
        ["豪放"] = "m3 20 7-17 1 11L21 5l-6 16M3 9l18 8",
        ["动感"] = "m13 3 8 9-8 9M4 7h8M2 12h16M4 17h8",
        ["全部字体"] = "M3 3H10V10H3Z M14 3H21V10H14Z M3 14H10V21H3Z M14 14H21V21H14Z",
        ["清除"] = "M5 5L19 19M19 5L5 19",
        ["反选"] = "M3 3H21V21H3Z M12 3V21M3 3L12 12 3 21Z",
        ["展开全部"] = "M5 8L12 15 19 8",
        ["收起全部"] = "M5 16L12 9 19 16",
        ["已完整"] = "M5 12L10 17 20 6",
        ["待确认/未标注"] = "M9 7C9 2 18 3 17 8L12 13V15M12 19V20",
        ["API 设置"] = "M4 7h16M4 17h16",
        ["AI 标注"] = "m12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5Z",
        ["窗口置顶"] = "m8 3 8 0-1 6 4 4H5l4-4ZM12 13v8",
        ["取消置顶"] = "m8 3 8 0-1 6 4 4H5l4-4ZM12 13v8",
        ["精简模式"] = "M3 4H21V20H3Z M9 4V20M15 4V20",
        ["完整模式"] = "M3 4H21V20H3Z M9 4V20M15 4V20",
        ["刷新字体"] = "M20 10a8 8 0 1 0 0 5M20 4v6h-6",
        ["备份设置"] = "M3 6q9-6 18 0v12q-9 6-18 0ZM3 6q9 6 18 0M3 12q9 6 18 0",
        ["保存标签"] = "M3 3h15l3 3v15H3ZM7 3v6h10V3M7 21v-8h10v8",
        ["加入分类"] = "M3 7V4h7l3 3h8v13H3Z",
        ["备份并删除"] = "M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7",
        ["锁定/解锁分类"] = "M8 10V7a4 4 0 0 1 8 0v3",
        ["全选当前"] = "m5 12 4 4L19 6",
        ["清空选择"] = "M4 4h16v16H4ZM8 8l8 8M16 8l-8 8",
        ["已安装字体"] = "m4 20 8-17 8 17M7 14h10",
        ["备份 / 文件夹预览"] = "M3 7V4h7l3 3h8v13H3Z",
        ["Illustrator 字体检查"] = "M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14M15 15l6 6M7 10l2 2 4-5",
        ["备份与恢复"] = "M20 10a8 8 0 1 0 0 5M20 4v6h-6",
        ["应用 ↗"] = "M6 18 18 6M6 6h12v12",
        ["安装并应用 ↗"] = "M6 18 18 6M6 6h12v12",
        ["等宽"] = "M3 4H21V20H3Z M9 4V20M15 4V20",
        ["衬线"] = "M4 20 12 3 20 20M7 14h10M2 20h6M16 20h6M9 3h6",
        ["无衬线"] = "m4 20 8-17 8 17M7 14h10",
        ["手写/书法"] = "M3 20c5-1 2-6 7-6l8-11 3 2-9 12c-1 5-6 5-9 3M10 14l3 3",
        ["装饰/展示"] = "m12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5Z",
        ["其他/待确认"] = "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20M9 8c0-4 7-4 6 0 0 2-3 2-3 5M12 17v1",
    };
    public static Geometry For(string name)
    {
        name=name.Replace("  ×", "").Split(" · ")[0];
        if(!Paths.TryGetValue(name,out var path))
        {
            var group=FontTaxonomy.Groups.FirstOrDefault(g=>g.Name==name);
            if(group is not null)path=Paths[group.Tags[0]];
            else path=name.Contains("删除")?Paths["备份并删除"]:name.Contains("应用")?Paths["应用 ↗"]:name.Contains("扫描")||name.Contains("刷新")?Paths["刷新字体"]:name.Contains("设置")?Paths["API 设置"]:name.Contains("保存")?Paths["保存标签"]:name.Contains("全选")?Paths["全选当前"]:FontTaxonomy.Basic.Contains(name)?Paths["已安装字体"]:"M3 7H10L13 10H21V21H3Z";
        }
        var geometry=Geometry.Parse(path);geometry.Freeze();return geometry;
    }
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>For(value?.ToString()??"");
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public partial class MainWindow
{
    private void Facet_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        var list=(ListBox)sender;
        var item=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;
        if(item?.Content is not CategoryChoice choice)return;
        SelectFacet(list,choice,(Keyboard.Modifiers & ModifierKeys.Shift)!=0);
        item.Focus();e.Handled=true;
    }
    private void SelectFacet(ListBox list,CategoryChoice choice,bool additive)
    {
        var toggleOff=additive&&list.SelectedItems.Contains(choice);
        _syncingCategory=true;
        try
        {
            if(!additive||choice.Name==AllCategories)
                foreach(var facet in new[]{CompactCategoryList,BasicTagList,AttributeTagList,TagStatusList}.Concat(_styleLists))facet.SelectedItems.Clear();
            if(choice.Name!=AllCategories){if(toggleOff)list.SelectedItems.Remove(choice);else if(!list.SelectedItems.Contains(choice))list.SelectedItems.Add(choice);}
        }
        finally{_syncingCategory=false;}
        CompactCategory_Changed(CompactCategoryList,new SelectionChangedEventArgs(ListBox.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
        foreach(var facet in new[]{BasicTagList,AttributeTagList,TagStatusList}.Concat(_styleLists))TagFacet_Changed(facet,new SelectionChangedEventArgs(ListBox.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
    }
}
