using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Security.Cryptography;

namespace FontHarbor;
public static class UserSecret
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob{public int Size;public IntPtr Data;}
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptProtectData(ref Blob data,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob data,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes,bool protect)
    {
        var input=new Blob{Size=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;
        try{Marshal.Copy(bytes,0,input.Data,bytes.Length);var ok=protect?CryptProtectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);if(!ok)throw new CryptographicException("无法使用当前 Windows 用户保护/读取 API Key。");var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;}
        finally{for(var i=0;i<input.Size;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
    }
    public static string Protect(string text)=>string.IsNullOrEmpty(text)?"":Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text),true));
    public static string Unprotect(string text)=>string.IsNullOrEmpty(text)?"":Encoding.UTF8.GetString(Transform(Convert.FromBase64String(text),false));
}
public static class DialogUi
{
    public static TextBlock Text(string text,double size=12)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,6)};
    public static void Label(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,Foreground=Brushes.Gray,FontSize=11,Margin=new Thickness(0,18,0,7)});
    public static Button Button(string text,RoutedEventHandler handler){var b=new Button{Content=text,Margin=new Thickness(0,0,8,0),FontWeight=FontWeights.Normal};b.Click+=handler;return b;}
    public static string Error(Exception ex)=>ex is OperationCanceledException?"已取消或请求超时。":ex is System.Net.Http.HttpRequestException?ex.Message:ex is ArgumentException or System.IO.InvalidDataException or System.IO.IOException or CryptographicException?ex.Message:"操作失败：返回格式不兼容或无法连接服务。请检查配置后重试。";
}
public sealed class ApiSettingsWindow : Window
{
    private readonly TextBox _base=new(),_manual=new();private readonly PasswordBox _key=new();private readonly ComboBox _models=new();private readonly CheckBox _remember=new(){Content="在本机记住密钥（使用当前 Windows 用户加密）"};
    private readonly TextBlock _status=DialogUi.Text("测试将实际请求你填写的服务；视觉测试仅发送随机数字样张，不上传字体文件。",11);
    private readonly CheckBox _localFast=new(){Content="本地 Ollama 快速模式（关闭思考、限制输出、保持模型加载）"};
    private readonly CancellationTokenSource _cancel=new();private readonly List<Control> _inputs=[];private string _verified="";private bool _busy;
    public ApiProfile Profile {get;private set;}=new();
    public string SessionKey=>_key.Password;
    public string VisionSignature=>_verified;
    public static string Signature(ApiProfile p,string key)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.BaseUrl.TrimEnd('/')+"|"+p.Model+"|"+key+"|"+p.LocalModelFastMode)));
    private ApiProfile Current()=>new(){BaseUrl=_base.Text.Trim(),Model=string.IsNullOrWhiteSpace(_manual.Text)?_models.SelectedItem?.ToString()??"":_manual.Text.Trim(),LocalModelFastMode=_localFast.IsChecked==true};
    public ApiSettingsWindow(ApiProfile profile,string sessionKey,string verified)
    {
        Title="API 与模型设置";Width=620;Height=700;MinHeight=530;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel{Margin=new Thickness(24)};Content=dock;
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        var save=DialogUi.Button("保存设置",(_,_)=>{try{var p=Current();_ = FontAiClient.Endpoint(p.BaseUrl,"models");p.ProtectedKey=_remember.IsChecked==true?UserSecret.Protect(_key.Password):"";Profile=p;DialogResult=true;}catch(Exception ex){_status.Text=DialogUi.Error(ex);}});bottom.Children.Add(save);_inputs.Add(save);
        var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(DialogUi.Text("连接你的模型服务",20));panel.Children.Add(_status);
        DialogUi.Label(panel,"OpenAI 兼容 API 基地址（一般以 /v1 结尾）");panel.Children.Add(_base);_base.Text=profile.BaseUrl;
        DialogUi.Label(panel,"API Key（只向此地址发送）");panel.Children.Add(_key);_key.Password=sessionKey;
        _remember.IsChecked=!string.IsNullOrEmpty(profile.ProtectedKey);_remember.Margin=new Thickness(0,10,0,0);panel.Children.Add(_remember);
        _localFast.IsChecked=profile.LocalModelFastMode;_localFast.Margin=new Thickness(0,10,0,0);panel.Children.Add(_localFast);
        panel.Children.Add(DialogUi.Text("快速模式可能降低小模型的分类准确度，请先抽查少量字体；发现误判可关闭此选项。仅自动用于本机 Ollama，其他服务保持兼容接口。",11));
        DialogUi.Label(panel,"模型列表");panel.Children.Add(_models);
        var fetch=DialogUi.Button("获取模型列表",async(_,_)=>await Run(async(client,p,key,token)=>{var models=await client.ModelsAsync(p,key,token);_models.ItemsSource=models;if(models.Contains(p.Model))_models.SelectedItem=p.Model;else if(models.Count>0)_models.SelectedIndex=0;return $"已获取 {models.Count} 个模型，可选择或手动填写模型 ID。";}));fetch.Margin=new Thickness(0,10,0,0);panel.Children.Add(fetch);
        DialogUi.Label(panel,"手动模型 ID（填写后优先使用）");panel.Children.Add(_manual);_manual.Text=profile.Model;
        var buttons=new WrapPanel{Margin=new Thickness(0,18,0,0)};panel.Children.Add(buttons);
        var connection=DialogUi.Button("测试所选模型",async(_,_)=>await Run(async(client,p,key,token)=>{await client.ChatAsync(p,key,"Reply with OK.",null,token);return "模型文本请求成功。请继续测试视觉能力。";}));
        var vision=DialogUi.Button("测试视觉能力",async(_,_)=>await Run(async(client,p,key,token)=>{var code=RandomNumberGenerator.GetInt32(100000,1000000).ToString();var image=FontSample.Render(new FontFamily("Segoe UI"),FontWeights.Normal,FontStyles.Normal,code);if(!await client.TestVisionAsync(p,key,image,code,token)){_verified="";return "未通过视觉校验：模型未正确读出图片中的随机数字。请检查模型是否支持图像输入。";}_verified=Signature(p,key);return "视觉能力验证通过，可对字体样张进行标注。";}));buttons.Children.Add(connection);buttons.Children.Add(vision);
        _inputs.AddRange([_base,_key,_models,_manual,_remember,_localFast,fetch,connection,vision]);
        void Invalidate(){_verified="";_status.Text="配置已更改，请重新测试视觉能力。";}
        _localFast.Checked+=(_,_)=>Invalidate();_localFast.Unchecked+=(_,_)=>Invalidate();
        _base.TextChanged+=(_,_)=>Invalidate();_key.PasswordChanged+=(_,_)=>Invalidate();_manual.TextChanged+=(_,_)=>Invalidate();_models.SelectionChanged+=(_,_)=>{_manual.Clear();Invalidate();};_verified=verified;
        Closed+=(_,_)=>_cancel.Cancel();
    }
    private async Task Run(Func<FontAiClient,ApiProfile,string,CancellationToken,Task<string>> action)
    {
        if(_busy)return;_busy=true;foreach(var c in _inputs)c.IsEnabled=false;_status.Text="正在请求服务…";
        try{using var client=new FontAiClient();_status.Text=await action(client,Current(),_key.Password,_cancel.Token);}
        catch(Exception ex){_verified="";if(!_cancel.IsCancellationRequested)_status.Text=DialogUi.Error(ex);}
        finally{_busy=false;foreach(var c in _inputs)c.IsEnabled=true;}
    }
}
