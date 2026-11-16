using System.Runtime.InteropServices;
using System.Text.Json;

namespace FontHarbor;

public static class IllustratorBridge
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object app);

    public static Task<string> ApplyAsync(string family, string style, string name)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? instance = null;
            try
            {
                Marshal.ThrowExceptionForHR(CLSIDFromProgID("Illustrator.Application", out var clsid));
                GetActiveObject(ref clsid, IntPtr.Zero, out instance);
                dynamic app = instance;
                string result = Convert.ToString(app.DoJavaScript(BuildScript(family, style, name))) ?? "";
                if (!result.StartsWith("OK|", StringComparison.Ordinal)) throw new InvalidOperationException(result.StartsWith("ERROR|") ? result[6..] : "Illustrator 未返回执行结果。");
                completion.SetResult("已应用字体：" + family + " · " + result[3..] + " 处文字（可在 Illustrator 中撤销）");
            }
            catch (COMException ex) { completion.SetException(new InvalidOperationException("无法连接 Illustrator。请先打开文档并选择文字；确保两个程序的管理员权限一致。\n" + ex.Message)); }
            catch (Exception ex) { completion.SetException(ex); }
            finally { if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
        }) { IsBackground = true, Name = "Illustrator font application" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    internal static string BuildScript(string family, string style, string name) => "(function(){ var family=" + JsonSerializer.Serialize(family) + ", style=" + JsonSerializer.Serialize(style) + ", name=" + JsonSerializer.Serialize(name) + ";" + """
        try {
            if (!app.documents.length) throw Error('请先在 Illustrator 中打开文档。');
            var font = null, i;
            for(i=0;i<app.textFonts.length;i++) {
                var candidate=app.textFonts[i];
                if (candidate.name === name || (candidate.family === family && candidate.style.toLowerCase() === style.toLowerCase())) {font=candidate;break;}
            }
            if(!font) throw Error('Illustrator 中未找到完全匹配的字体及样式。安装后请重启 Illustrator 再试。');
            var selection=app.activeDocument.selection, ranges=[];
            function collect(item) {
                if(item.locked || item.hidden) throw Error('选区包含锁定或隐藏对象，请先解锁。');
                if(item.typename==='TextRange') ranges.push(item);
                else if(item.typename==='TextFrame') ranges.push(item.textRange);
                else if(item.typename==='GroupItem') for(var j=0;j<item.pageItems.length;j++) collect(item.pageItems[j]);
            }
            if(selection && selection.typename==='TextRange') collect(selection);
            else if(selection) for(i=0;i<selection.length;i++) collect(selection[i]);
            if(!ranges.length) throw Error('请选择文字对象或文字范围。已转曲的文字无法应用字体。');
            var chars=[], previous=[];
            for(i=0;i<ranges.length;i++) for(var c=0;c<ranges[i].characters.length;c++) {
                var ch=ranges[i].characters[c]; chars.push(ch); previous.push(ch.characterAttributes.textFont);
            }
            if(!chars.length) throw Error('选区中没有文字字符。');
            var applied=0;
            try { for(i=0;i<chars.length;i++){chars[i].characterAttributes.textFont=font;applied++;} }
            catch(e) {for(i=applied-1;i>=0;i--) {try {chars[i].characterAttributes.textFont=previous[i];}catch(ignore){}} throw e;}
            app.redraw(); return 'OK|'+ranges.length;
        } catch(e) { return 'ERROR|'+e.message; }
        })();
        """;
}
