using FontHarbor;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;

static class TagWorkflowChecks
{
    public static void Run(MainWindow main,string fontPath)
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server=Task.Run(async()=>
        {
            for(var i=0;i<4;i++)
            {
                using var socket=await listener.AcceptTcpClientAsync(timeout.Token);using var stream=socket.GetStream();var header=new List<byte>();var one=new byte[1];
                while(true){if(await stream.ReadAsync(one,timeout.Token)!=1)throw new IOException("Unexpected socket close");header.Add(one[0]);if(header.Count>=4&&Encoding.ASCII.GetString(header.TakeLast(4).ToArray())=="\r\n\r\n")break;if(header.Count>16000)throw new IOException("Header too long");}
                var headers=Encoding.ASCII.GetString(header.ToArray());
                if(!headers.StartsWith("POST /v1/chat/completions"))throw new Exception("Actual endpoint mismatch");
                var length=int.Parse(headers.Split("\r\n").Single(s=>s.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Split(':')[1]);var body=new byte[length];await stream.ReadExactlyAsync(body,timeout.Token);
                using var json=JsonDocument.Parse(body);var root=json.RootElement;
                if(root.GetProperty("model").GetString()!="workflow-test")throw new Exception("Actual model selection ignored");
                var image=root.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()!;
                if(!image.StartsWith("data:image/png;base64,"))throw new Exception("Missing actual sample PNG");
                var reply=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content="{\"basic\":\"无衬线\",\"styles\":[\"科技\"],\"confidence\":0.9}"}}}});var bytes=Encoding.UTF8.GetBytes(reply);var head=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");await stream.WriteAsync(head,timeout.Token);await stream.WriteAsync(bytes,timeout.Token);
            }
        },timeout.Token);
        try
        {
            var op=Dispatcher.CurrentDispatcher.InvokeAsync(async()=>
            {
                var profile=new ApiProfile{BaseUrl=$"http://127.0.0.1:{port}/v1",Model="workflow-test",LocalModelFastMode=false};
                var targets=new List<TagTarget>{new(fontPath,"Flow A","Regular",new FontFamily("Arial"),FontWeights.Normal,FontStyles.Normal),new(fontPath,"Flow B","Regular",new FontFamily("Arial"),FontWeights.Normal,FontStyles.Normal)};
                int persisted=0;FontTaggingWindow? window=null;
                FontTaggingWindow New(bool cancel)=>new(main,profile,"",ApiSettingsWindow.Signature(profile,""),(_,_,_,_)=>Task.FromResult(targets),(_,_,_)=>new FontAnnotation{Basic="无衬线",Styles=[]},(binding,key,value)=>{if(value.Basic!="无衬线")throw new Exception("Bad result");persisted++;if(cancel)window!.RequestCancel();},(path,family,style)=>path+family+style);
                async Task Invoke(string name){var task=(Task)typeof(FontTaggingWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;await task;}
                window=New(false);
                var start=(System.Windows.Controls.Button)typeof(FontTaggingWindow).GetField("_start",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                if(!start.IsEnabled||start.Content.ToString()!="开始自动标注")throw new Exception("One-click action not ready before preparation");
                var first=Invoke("Run");await Invoke("Run");await first;if(persisted!=2)throw new Exception("Complete tagging job failed: "+((System.Windows.Controls.TextBlock)typeof(FontTaggingWindow).GetField("_status",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Text);window.Close();
                persisted=0;window=New(true);await Invoke("Run");if(persisted!=1)throw new Exception("Cancelled job did not retain completed result");window.Close();
                window=new FontTaggingWindow(main,profile,"","",(_,_,_,_)=>Task.FromResult(new List<TagTarget>()),(_,_,_)=>null,(_,_,_)=>throw new Exception("Empty source uploaded"),(_,_,_)=>"");
                await Invoke("Run");window.Close();
                window=new FontTaggingWindow(main,profile,"","",(_,_,_,_)=>Task.FromResult(targets),(_,_,_)=>new FontAnnotation{Basic="无衬线",Styles=["现代"]},(_,_,_)=>throw new Exception("Complete font uploaded"),(_,_,_)=>"");
                await Invoke("Run");window.Close();
                window=new FontTaggingWindow(main,profile,"","",(_,_,_,token)=>{window!.RequestCancel();token.ThrowIfCancellationRequested();return Task.FromResult(targets);},(_,_,_)=>null,(_,_,_)=>throw new Exception("Upload after scan cancellation"),(_,_,_)=>"");
                await Invoke("Run");window.Close();
                var scanned=false;
                window=new FontTaggingWindow(main,profile,"","",(_,_,_,_)=>{scanned=true;return Task.FromResult(targets);},(_,_,_)=>null,(_,_,_)=>throw new Exception("Unverified font uploaded"),(_,_,_)=>"");
                await Invoke("Run");
                var status=(System.Windows.Controls.TextBlock)typeof(FontTaggingWindow).GetField("_status",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                if(!scanned||!status.Text.Contains("视觉验证未通过"))throw new Exception("Automatic vision failure did not stop upload: "+status.Text);window.Close();

            });
            Pump(op.Task.Unwrap());Pump(server);
            Console.WriteLine("PASS: one-click scan/render/upload, duplicate-click guard, automatic vision failure gate, empty/complete sources, scan cancellation and completed-result retention over real loopback HTTP.");
        }
        finally{timeout.Cancel();listener.Stop();}
    }
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();
        // Use the UI dispatcher captured here rather than a worker dispatcher.
        var dispatcher=Dispatcher.CurrentDispatcher;
        task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
        if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
    }
}
