using FontHarbor;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

static class ApiChecks
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>send(r,t);}
    private static HttpResponseMessage Json(string body,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    public static void Run()
    {
        var profile=new ApiProfile{BaseUrl="http://127.0.0.1:8769/v1",Model="test-vision",LocalModelFastMode=false};
        var png=FontSample.Render(new FontFamily("Arial"),FontWeights.Normal,FontStyles.Normal,"601938");
        if(png.Length<1000||png[0]!=137)throw new Exception("Sample PNG rendering failed");
        var nativeCalls=0;var probes=0;
        using(var nativeClient=new FontAiClient(new Handler(async(r,t)=>
        {
            if(r.RequestUri!.AbsolutePath=="/api/version"){probes++;return Json("{\"version\":\"0.34.4\"}");}
            if(r.RequestUri.AbsolutePath!="/api/chat")throw new Exception("Ollama native endpoint missing");
            using var doc=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(t));var b=doc.RootElement;
            if(b.GetProperty("think").GetBoolean()||b.GetProperty("stream").GetBoolean()||b.GetProperty("keep_alive").GetString()!="10m")throw new Exception("Ollama fast options missing");
            if(Convert.FromBase64String(b.GetProperty("messages")[0].GetProperty("images")[0].GetString()!).Length!=png.Length)throw new Exception("Native image transport changed");
            nativeCalls++;var classify=b.TryGetProperty("format",out var format);
            if(b.GetProperty("options").GetProperty("num_predict").GetInt32()!=(classify?256:32))throw new Exception("Output budget missing");
            if(classify&&format.GetString()!="json")throw new Exception("JSON mode missing");
            return Json(JsonSerializer.Serialize(new{message=new{content=classify?"{\"basic\":\"衬线\",\"styles\":[\"古典\"]}":"601938"},done_reason="stop"}));
        })))
        {
            var local=new ApiProfile{BaseUrl=profile.BaseUrl,Model=profile.Model};
            if(!nativeClient.TestVisionAsync(local,"",png,"601938",default).GetAwaiter().GetResult())throw new Exception("Native vision failed");
            if(!FontTaxonomy.IsComplete(nativeClient.ClassifyAsync(local,"","Arial",png,default).GetAwaiter().GetResult()))throw new Exception("Native classification failed");
            if(probes!=1||nativeCalls!=2)throw new Exception("Ollama detection was not cached");
        }
        using(var fallback=new FontAiClient(new Handler((r,t)=>Task.FromResult(r.RequestUri!.AbsolutePath=="/api/version"?Json("{}",HttpStatusCode.NotFound):Json("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}")))))
        {if(fallback.ChatAsync(new ApiProfile{BaseUrl=profile.BaseUrl,Model="other"},"","Hello",null,default).GetAwaiter().GetResult()!="OK")throw new Exception("Non-Ollama local service fallback failed");}
        var count=0;
        using(var client=new FontAiClient(new Handler(async(r,t)=>
        {
            count++;if(r.Headers.Authorization?.Parameter!="unit-test-key")throw new Exception("Bearer auth missing");
            if(r.Method==HttpMethod.Get){if(r.RequestUri!.AbsolutePath!="/v1/models")throw new Exception("Models path invalid");return Json("{\"data\":[{\"id\":\"test-vision\"},{\"id\":\"test-vision\"},{\"id\":\"text\"}]}");}
            var body=await r.Content!.ReadAsStringAsync(t);using var json=JsonDocument.Parse(body);var root=json.RootElement;
            if(root.GetProperty("model").GetString()!="test-vision")throw new Exception("Selected model ignored");
            var parts=root.GetProperty("messages")[0].GetProperty("content");
            string answer;
            if(parts.ValueKind==JsonValueKind.String)answer="OK";
            else
            {
                var prompt=parts[0].GetProperty("text").GetString()!;var image=parts[1].GetProperty("image_url").GetProperty("url").GetString()!;
                if(!image.StartsWith("data:image/png;base64,"))throw new Exception("Image transport invalid");
                if(prompt.Contains("Read the six")){if(prompt.Contains("601938"))throw new Exception("Vision answer leaked into prompt");answer="601938";}
                else answer="```json\n{\"basic\":\"衬线\",\"styles\":[\"复古\"],\"confidence\":0.8}\n```";
            }
            return Json(JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=answer}}}}));
        })))
        {
            if(client.ModelsAsync(profile,"unit-test-key",default).GetAwaiter().GetResult().Count!=2)throw new Exception("Model listing failed");
            if(client.ChatAsync(profile,"unit-test-key","Hello",null,default).GetAwaiter().GetResult()!="OK")throw new Exception("Connection test failed");
            if(!client.TestVisionAsync(profile,"unit-test-key",png,"601938",default).GetAwaiter().GetResult())throw new Exception("Vision test failed");
            if(client.TestVisionAsync(profile,"unit-test-key",png,"000000",default).GetAwaiter().GetResult())throw new Exception("Incorrect visual answer accepted");
            var a=client.ClassifyAsync(profile,"unit-test-key","Arial",png,default).GetAwaiter().GetResult();if(a.Basic!="衬线"||a.Styles.Single()!="复古")throw new Exception("Classification failed");
        }
        using(var client=new FontAiClient(new Handler((r,t)=>Task.FromResult(Json("echo-secret",HttpStatusCode.Unauthorized)))))
        {try{client.ModelsAsync(profile,"private-key",default).GetAwaiter().GetResult();throw new Exception("Unauthorized accepted");}catch(HttpRequestException ex){if(ex.Message.Contains("private-key")||ex.Message.Contains("echo-secret"))throw new Exception("Error leaked credential/response");}}
        var attempts=0;
        using(var client=new FontAiClient(new Handler((r,t)=>{attempts++;return Task.FromResult(attempts==1?Json("{}",HttpStatusCode.ServiceUnavailable):Json("{\"data\":[]}"));})))client.ModelsAsync(profile,"",default).GetAwaiter().GetResult();
        if(attempts!=2)throw new Exception("Retry policy failed");
        using(var cts=new CancellationTokenSource(30))
        using(var client=new FontAiClient(new Handler(async(r,t)=>{await Task.Delay(10000,t);return Json("{}");})))
        {try{client.ModelsAsync(profile,"",cts.Token).GetAwaiter().GetResult();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}}
        var repairCalls=0;
        using(var client=new FontAiClient(new Handler((r,t)=>
        {
            repairCalls++;var answer=repairCalls==1?"{\"basic\":\"无衬线\",\"styles\":[]}":"{\"basic\":\"无衬线\",\"styles\":[\"现代\",\"简约\"],\"confidence\":0.9}";
            return Task.FromResult(Json(JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=answer}}}})));
        })))
        {var repaired=client.ClassifyAsync(profile,"","Arial",png,default).GetAwaiter().GetResult();if(repairCalls!=2||repaired.Styles.Count!=2||!FontTaxonomy.IsComplete(repaired))throw new Exception("Empty style response was not repaired");}
        var legacy=new FontAnnotation{Basic="无衬线",Styles=[]};FontTaxonomy.NormalizeLegacy(legacy);
        if(legacy.Styles.Count!=0||FontTaxonomy.IsComplete(legacy))throw new Exception("Legacy incomplete labels skipped");
        var unknown=FontTaxonomy.Parse("{\"basic\":\"无衬线\",\"styles\":[],\"confidence\":0.3,\"needs_review\":true}","test");
        if(FontTaxonomy.IsComplete(unknown)||!unknown.NeedsReview)throw new Exception("Unknown style incorrectly completed");
        try{FontTaxonomy.Parse("{\"basic\":\"无衬线\",\"styles\":[]}","test");throw new Exception("Empty styles accepted");}catch(System.IO.InvalidDataException){}
        if(FontTaxonomy.Groups.Length!=7||FontTaxonomy.Styles.Length!=34||FontTaxonomy.Styles.Distinct().Count()!=34)throw new Exception("Grouped taxonomy invalid");
        if(FontTaxonomy.Styles.Contains("待确认")||FontTaxonomy.Basic.Contains("等宽"))throw new Exception("Status or width mixed into taxonomy");
        var old=new FontAnnotation{Basic="等宽",Styles=["手写","经典"]};FontTaxonomy.NormalizeLegacy(old);FontTaxonomy.NormalizeLegacy(old);
        if(old.Basic!="其他/待确认"||old.Attributes.Single()!="等宽"||old.Styles.Contains("硬笔手写")||!old.Styles.Contains("古典")||!old.LegacyStyles.Contains("手写")||FontTaxonomy.IsComplete(old))throw new Exception("Legacy migration not safe/idempotent");
        var historical=FontTaxonomy.Parse("{\"basic\":\"衬线\",\"styles\":[\"古典\",\"正式\"],\"needs_review\":false}","test");
        if(!FontTaxonomy.IsComplete(historical))throw new Exception("Grouped labels rejected");
        if(FontSample.IsMonospace(new FontFamily("Arial"),FontWeights.Normal,FontStyles.Normal))throw new Exception("Proportional font called monospace");
        if(!FontSample.IsMonospace(new FontFamily("Consolas"),FontWeights.Normal,FontStyles.Normal))throw new Exception("Monospace metadata not detected");
        Console.WriteLine("PASS: 7 groups / 34 styles, review separate from styles, monospace independent, conservative legacy migration and visual width metadata.");
        Console.WriteLine("PASS: both tag dimensions mandatory, empty-style correction retry, legacy requeue and explicit pending labels.");
        foreach(var invalid in new[]{"{\"basic\":\"wrong\",\"styles\":[]}","{\"basic\":\"衬线\",\"styles\":[\"wrong\"]}","{\"basic\":\"衬线\",\"styles\":[],\"confidence\":2}"})
        {try{FontTaxonomy.Parse(invalid,"test");throw new Exception("Invalid taxonomy accepted");}catch(System.IO.InvalidDataException){}}
        foreach(var bad in new[]{"http://example.com/v1","https://user:key@example.com/v1","https://example.com/v1?key=secret"})
        {try{FontAiClient.Endpoint(bad,"models");throw new Exception("Unsafe API base accepted");}catch(ArgumentException){}}
        var protectedKey=UserSecret.Protect("unit-test-key");if(protectedKey.Contains("unit-test-key")||UserSecret.Unprotect(protectedKey)!="unit-test-key")throw new Exception("Windows secret protection failed");
        Console.WriteLine("PASS: model list/auth, text/vision/image transport, JSON taxonomy, rate retry, cancellation, error redaction, URL validation and Windows key encryption.");
    }
}

