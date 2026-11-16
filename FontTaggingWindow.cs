using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FontHarbor;
public sealed class FontTaggingWindow : Window
{
    private readonly MainWindow _main;
    private ApiProfile _profile;private string _key,_vision;
    private readonly Func<int,string,bool,CancellationToken,Task<List<TagTarget>>> _getTargets;
    private readonly Func<string,string,string,FontAnnotation?> _annotation;
    private readonly Action<string,string,FontAnnotation> _persist;
    private readonly Func<string,string,string,string> _binding;
    private readonly ComboBox _source=new(){ItemsSource=new[]{"已安装字体","备份目录","任意字体文件夹"},SelectedIndex=0};
    private readonly TextBox _folder=new();private readonly CheckBox _selected=new(){Content="只处理勾选/选中的字体（重新标注）"};
    private readonly TextBlock _status=DialogUi.Text("选择来源后点击开始，程序将自动准备、预览、上传并标注。默认补齐缺少任一维度或待确认的字体。",12),_model=DialogUi.Text("",11),_name=DialogUi.Text("",11);
    private readonly ProgressBar _progress=new(){Height=9,Margin=new Thickness(0,15,0,10)};private readonly Image _sample=new(){Height=150,Stretch=System.Windows.Media.Stretch.Uniform};
    private readonly Button _start,_cancel,_api,_browse;
    private readonly List<TagTarget> _targets=[];private CancellationTokenSource? _cancellation;private bool _running,_closeWhenFinished;
    public FontTaggingWindow(MainWindow main,ApiProfile profile,string key,string vision,Func<int,string,bool,CancellationToken,Task<List<TagTarget>>> getTargets,Func<string,string,string,FontAnnotation?> annotation,Action<string,string,FontAnnotation> persist,Func<string,string,string,string> binding)
    {
        _main=main;_profile=profile;_key=key;_vision=vision;_getTargets=getTargets;_annotation=annotation;_persist=persist;_binding=binding;
        Title="AI 字体标注";Width=660;Height=700;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel{Margin=new Thickness(24)};Content=dock;
        var bottom=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        _api=DialogUi.Button("API 设置",(_,_)=>{var config=_main.ConfigureTaggingApi();_profile=config.Profile;_key=config.Key;_vision=config.Vision;Invalidate();});
        _start=DialogUi.Button("开始自动标注",async(_,_)=>await Run());_cancel=DialogUi.Button("取消任务",(_,_)=>RequestCancel());bottom.Children.Add(_api);bottom.Children.Add(_start);bottom.Children.Add(_cancel);
        var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(DialogUi.Text("按字体样张识别类型与风格",20));panel.Children.Add(DialogUi.Text("开始后会向下方 API 服务发送字体名称和固定样张图片，不上传字体文件；样张会省略不支持的字符，避免使用替代字体误判。可能产生服务商费用。结果可在字体详情中手动修正。",11));panel.Children.Add(_model);
        DialogUi.Label(panel,"字体来源");panel.Children.Add(_source);DialogUi.Label(panel,"文件夹路径");
        var pathRow=new DockPanel();_browse=DialogUi.Button("选择",(_,_)=>{var dialog=new Microsoft.Win32.OpenFolderDialog{Title="选择字体文件夹"};if(dialog.ShowDialog(this)==true)_folder.Text=dialog.FolderName;});DockPanel.SetDock(_browse,Dock.Right);pathRow.Children.Add(_browse);pathRow.Children.Add(_folder);panel.Children.Add(pathRow);_folder.Text=main.CurrentFontFolder;
        _selected.Margin=new Thickness(0,15,0,10);panel.Children.Add(_selected);panel.Children.Add(DialogUi.Text("已安装字体使用勾选框；文件夹字体使用列表选中的行。",10));
        panel.Children.Add(_name);panel.Children.Add(_sample);panel.Children.Add(_progress);panel.Children.Add(_status);
        _source.SelectionChanged+=(_,_)=>{if(_source.SelectedIndex==1)_folder.Text=main.CurrentBackupFolder;else if(_source.SelectedIndex==2)_folder.Text=main.CurrentFontFolder;Invalidate();};_folder.TextChanged+=(_,_)=>Invalidate();_selected.Checked+=(_,_)=>Invalidate();_selected.Unchecked+=(_,_)=>Invalidate();
        Closing+=(_,e)=>{if(_running){e.Cancel=true;_closeWhenFinished=true;RequestCancel();}};UpdateControls();
    }
    public void RequestCancel(){_cancellation?.Cancel();_status.Text="正在取消，将保留已保存的结果…";}
    private void Invalidate(){if(_running)return;_targets.Clear();_sample.Source=null;_name.Text="";_status.Text="来源已变更。点击开始将自动准备样张并标注。";UpdateControls();}
    private bool Ready=>!string.IsNullOrEmpty(_vision)&&_vision==ApiSettingsWindow.Signature(_profile,_key);
    private void UpdateControls()
    {
        _source.IsEnabled=_folder.IsEnabled=_browse.IsEnabled=_selected.IsEnabled=_api.IsEnabled=!_running;
        _folder.IsReadOnly=_source.SelectedIndex!=2;_browse.IsEnabled=!_running&&_source.SelectedIndex==2;
        _start.IsEnabled=!_running&&!string.IsNullOrWhiteSpace(_profile.Model);_start.Content=_running?"正在自动标注…":"开始自动标注";_cancel.IsEnabled=_running;
        _model.Text=$"API：{_profile.BaseUrl}\n模型：{_profile.Model}\n"+(Ready?"视觉能力已验证":string.IsNullOrWhiteSpace(_profile.Model)?"请先在 API 设置中选择模型。":"开始时将自动验证视觉能力，无需单独测试。");
    }
    private byte[] Preview(TagTarget target)
    {
        var bytes=FontSample.Render(target.Preview,target.Weight,target.Slant);using var stream=new MemoryStream(bytes);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();_sample.Source=image;_name.Text=target.Name;return bytes;
    }
    private async Task Run()
    {
        if(_running||string.IsNullOrWhiteSpace(_profile.Model))return;
        _targets.Clear();
        _running=true;_cancellation=new CancellationTokenSource();var token=_cancellation.Token;UpdateControls();_sample.Source=null;_name.Text="";_progress.IsIndeterminate=true;_progress.Value=0;_progress.Maximum=1;var startedUtc=DateTime.UtcNow;int success=0,failed=0,cached=0,processed=0,consecutiveFailures=0,pending=0;string? failure=null;bool discovered=false;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();var itemElapsed=System.Diagnostics.Stopwatch.StartNew();string currentName="准备标注",phase="准备中";bool retrying=false;
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        timer.Tick+=(_,_)=>_name.Text=$"{currentName}\n{(retrying?"格式校验重试 · ":"")}{phase} · 本项 {itemElapsed.Elapsed.TotalSeconds:F0} 秒 · 总计 {elapsed.Elapsed.TotalMinutes:F1} 分钟";
        timer.Start();
        try
        {
            using var client=new FontAiClient();
            client.RequestStatus+=message=>{phase=message;if(message.Contains("重试"))retrying=true;};
            token.ThrowIfCancellationRequested();_status.Text="正在自动扫描字体、检查已有标签…";
            var targets=await _getTargets(_source.SelectedIndex,_folder.Text.Trim(),_selected.IsChecked==true,token);
            foreach(var target in targets)
            {
                token.ThrowIfCancellationRequested();
                if(_selected.IsChecked==true||!FontTaxonomy.IsComplete(_annotation(target.Path,target.Family,target.Style)))_targets.Add(target);
            }
            discovered=true;_progress.IsIndeterminate=false;_progress.Maximum=Math.Max(1,_targets.Count);
            if(_targets.Count==0){_status.Text="没有需要标注的字体：所选范围为空，或标签已完整。可更换来源或勾选字体后重新标注。";return;}
            if(!Ready)
            {
                currentName="验证模型视觉能力";itemElapsed.Restart();
                _status.Text="正在自动验证模型视觉能力…";_progress.IsIndeterminate=true;
                var challenge=System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000,1000000).ToString();
                var probe=FontSample.Render(new System.Windows.Media.FontFamily("Segoe UI"),FontWeights.Normal,FontStyles.Normal,challenge);
                if(!await client.TestVisionAsync(_profile,_key,probe,challenge,token))throw new InvalidDataException("视觉验证未通过，尚未上传字体样张。请在 API 设置中检查模型。再次开始会自动重试验证。");
                _vision=ApiSettingsWindow.Signature(_profile,_key);_main.RememberTaggingVision(_profile,_key,_vision);_progress.IsIndeterminate=false;UpdateControls();
            }
            var fingerprints=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var t in _targets.ToList())
            {
                currentName=t.Name;phase="生成样张";retrying=false;itemElapsed.Restart();
                token.ThrowIfCancellationRequested();_status.Text=$"正在识别 {processed+1}/{_targets.Count}：{t.Name}\n成功 {success} · 复用 {cached} · 失败 {failed}";
                try
                {
                    _status.Text=$"正在自动生成样张并识别 {processed+1}/{_targets.Count}：{t.Name}\n成功 {success} · 复用 {cached} · 失败 {failed}";
                    var image=Preview(t);
                    var binding=_binding(t.Path,t.Family,t.Style);
                    if(!fingerprints.TryGetValue(t.Path,out var hash)){hash=await Task.Run(()=>FontTaxonomy.FileHash(t.Path),token);fingerprints[t.Path]=hash;}
                    var fingerprint=hash+"|"+t.Family+"|"+t.Style;
                    FontAnnotation annotation;
                    if(_selected.IsChecked!=true&&_main.TryGetCachedAnnotation(fingerprint,out var existing)&&FontTaxonomy.IsComplete(existing)){annotation=existing;cached++;}
                    else{annotation=await client.ClassifyAsync(_profile,_key,t.Name,image,token);annotation.Attributes=FontSample.IsMonospace(t.Preview,t.Weight,t.Slant)?["等宽"]:[];}
                    token.ThrowIfCancellationRequested();if(binding!=_binding(t.Path,t.Family,t.Style))throw new IOException("扫描期间字体文件发生变化。");
                    var current=_annotation(t.Path,t.Family,t.Style);
                    if(current?.Origin=="手动修改")
                    {
                        if(current.UpdatedUtc>=startedUtc||(_selected.IsChecked!=true&&FontTaxonomy.IsComplete(current)))annotation=current;
                        else if(_selected.IsChecked!=true&&FontTaxonomy.Basic.Contains(current.Basic)&&current.Basic!="其他/待确认")
                        {annotation.Basic=current.Basic;annotation.Origin="AI 补全风格，保留人工基础类型";annotation.NeedsReview=annotation.NeedsReview||annotation.Styles.Count==0;annotation.Attributes=current.Attributes.ToList();}
                    }
                    if(!FontTaxonomy.IsComplete(annotation))pending++;
                    _persist(binding,fingerprint,annotation);success++;consecutiveFailures=0;
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception ex){failed++;consecutiveFailures++;failure=DialogUi.Error(ex);if(ex is System.Net.Http.HttpRequestException&&(ex.Message.Contains("401")||ex.Message.Contains("403")||ex.Message.Contains("404")))throw;if(consecutiveFailures>=3)throw new IOException("连续三个字体处理失败，任务已停止。请检查服务或样张后重试。");}
                processed++;_progress.Value=processed;
            }
            _status.Text=$"完成：处理 {success} · 待确认 {pending} · 复用 {cached} · 失败 {failed}\n总耗时 {elapsed.Elapsed.TotalMinutes:F1} 分钟"+(failure is null?"":"\n最近错误："+failure);
        }
        catch(OperationCanceledException){_status.Text=discovered?$"已取消：成功 {success} · 失败 {failed} · 未处理 {_targets.Count-processed}，已保存结果保留。":"已取消自动检查/扫描，未开始上传字体样张。已保存的标签不变。";}
        catch(Exception ex){_status.Text=$"任务停止：成功 {success} · 失败 {failed}\n"+DialogUi.Error(ex);}
        finally{timer.Stop();elapsed.Stop();_targets.Clear();EndOperation();}
    }
    private void EndOperation(){_progress.IsIndeterminate=false;_running=false;_cancellation?.Dispose();_cancellation=null;UpdateControls();if(_closeWhenFinished)Close();}
}
