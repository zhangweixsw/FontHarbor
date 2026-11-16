using FontHarbor;
using System.Net;
using System.Net.Http;
class UpdateChecks
{
    sealed class Handler(string primary):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;
            var text=request.RequestUri!.Host=="api.github.com"?"{\"tag_name\":\"v1.3.99\"}":primary;
            return Task.FromResult(new HttpResponseMessage(primary=="FAIL" && Calls==1?HttpStatusCode.Forbidden:HttpStatusCode.OK){Content=new StringContent(text)});
        }
    }
    public static void Run()
    {
        foreach(var value in new[]{"FAIL","<html>challenge</html>","{\"product\":\"other\",\"version\":\"99.0\"}"})
        {
            using var handler=new Handler(value);using var client=new HttpClient(handler);
            var result=ReleaseUpdate.FetchAsync(client).GetAwaiter().GetResult();
            if(!result.Fallback||handler.Calls!=2||ReleaseUpdate.NewerVersion(result.Json,new Version(1,3,16,0)) is null)throw new Exception("Update fallback failed");
        }
        using var good=new Handler("{\"product\":\"font-harbor\",\"version\":\"1.3.16\"}");using var goodClient=new HttpClient(good);
        if(ReleaseUpdate.FetchAsync(goodClient).GetAwaiter().GetResult().Fallback||good.Calls!=1)throw new Exception("Primary update source failed");
        Console.WriteLine("PASS: primary version, HTTP error, invalid response and wrong-product fallback.");
    }
}
