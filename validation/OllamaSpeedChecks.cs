using FontHarbor;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

static class OllamaSpeedChecks
{
    public static void Run()
    {
        var image=FontSample.Render(new FontFamily("Arial"),FontWeights.Normal,FontStyles.Normal);
        using var client=new FontAiClient();
        var profile=new ApiProfile{BaseUrl="http://127.0.0.1:11434/v1",Model="qwen3.5:0.8b"};
        if(Environment.GetEnvironmentVariable("FH_FAST_VISION_CHECK")=="1")
        {
            foreach(var model in new[]{"qwen3.5:0.8b","ornith-1.5:9b"})
            {
                profile.Model=model;
                var clock=Stopwatch.StartNew();var digits="601938";
                var probe=FontSample.Render(new FontFamily("Segoe UI"),FontWeights.Normal,FontStyles.Normal,digits);
                Console.WriteLine($"{model} fast vision={client.TestVisionAsync(profile,"",probe,digits,default).GetAwaiter().GetResult()} time={clock.Elapsed.TotalSeconds:F2}s");
                clock.Restart();var result=client.ClassifyAsync(profile,"","Arial",image,default).GetAwaiter().GetResult();
                Console.WriteLine($"{model} fast classification={result.Basic} styles={string.Join(',',result.Styles)} time={clock.Elapsed.TotalSeconds:F2}s");
            }
            return;
        }
        foreach(var mode in new[]{"fast-cold","original-warm","fast-warm"})
        {
            profile.LocalModelFastMode=mode!="original-warm";
            var clock=Stopwatch.StartNew();
            try
            {
                var result=client.ClassifyAsync(profile,"","Arial",image,default).GetAwaiter().GetResult();
                Console.WriteLine($"{mode}: {clock.Elapsed.TotalSeconds:F2}s basic={result.Basic} styles={string.Join(',',result.Styles)} review={result.NeedsReview}");
            }
            catch(Exception ex){Console.WriteLine($"{mode}: {clock.Elapsed.TotalSeconds:F2}s ERROR {ex.Message}");throw;}
        }
    }
}
