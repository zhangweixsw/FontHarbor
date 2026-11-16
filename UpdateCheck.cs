using System.Net.Http;
using System.Text.Json;
using System.Windows;
namespace FontHarbor;
public static class ReleaseUpdate
{
    public const string DownloadPage="https://mootop.top/software/font-harbor/#download";
    public const string GitHubPage="https://github.com/zhangweixsw/FontHarbor/releases/latest";
    public static async Task<(string Json,bool Fallback)> FetchAsync(HttpClient client)
    {
        try
        {
            var json=await client.GetStringAsync("https://mootop.top/wp-content/uploads/font-harbor/latest.json?check="+DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            using var document=JsonDocument.Parse(json);
            if(document.RootElement.GetProperty("product").GetString()!="font-harbor" || !Version.TryParse(document.RootElement.GetProperty("version").GetString(),out _))throw new InvalidOperationException("版本信息无效");
            return(json,false);
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            var json=await client.GetStringAsync("https://api.github.com/repos/zhangweixsw/FontHarbor/releases/latest");
            using var document=JsonDocument.Parse(json);
            var version=document.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            if(!Version.TryParse(version,out _))throw new InvalidOperationException("备用版本信息无效");
            return(JsonSerializer.Serialize(new{product="font-harbor",version}),true);
        }
    }
    public static Version? NewerVersion(string json,Version current)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement;
        if(root.GetProperty("product").GetString()!="font-harbor")return null;
        if(!Version.TryParse(root.GetProperty("version").GetString(),out var latest))return null;
        var normalized=new Version(latest.Major,latest.Minor,Math.Max(0,latest.Build),Math.Max(0,latest.Revision));
        return normalized>current?latest:null;
    }
}
public partial class MainWindow
{
    private bool _checkingUpdate,_hasUpdate;
    private string _updatePage=ReleaseUpdate.DownloadPage;
    private async void CheckUpdate_Click(object sender,RoutedEventArgs e)
    {
        if(_hasUpdate){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updatePage){UseShellExecute=true});}catch{MessageBox.Show(_updatePage,"下载最新版");}return;}
        await CheckForUpdateAsync(true);
    }
    private async Task CheckForUpdateAsync(bool manual=false)
    {
        if(_checkingUpdate)return;
        _checkingUpdate=true;
        try
        {
            using var handler=new HttpClientHandler {AllowAutoRedirect=false};
            using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=65536};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FontHarbor/" + typeof(MainWindow).Assembly.GetName().Version!.ToString(3));
            var result=await ReleaseUpdate.FetchAsync(client);
            var json=result.Json;
            _updatePage=result.Fallback?ReleaseUpdate.GitHubPage:ReleaseUpdate.DownloadPage;
            var latest=ReleaseUpdate.NewerVersion(json,typeof(MainWindow).Assembly.GetName().Version!);
            _hasUpdate=latest is not null;
            UpdateButton.Content=latest is not null?$"发现新版本 v{latest} · 下载 ↗":"检查更新";
            if(manual && latest is null)MessageBox.Show("当前已是最新版本。","字港更新");
        }
        catch {UpdateButton.Content="检查失败 · 重试"; if(manual)MessageBox.Show("暂时无法检查更新，请稍后重试，或访问 mootop.top/software/font-harbor/ 下载最新版。","字港更新");}
        finally{_checkingUpdate=false;}
    }
}
