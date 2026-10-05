// dotnet run --project tests/SayMaker.Tests — no test framework needed.
using System.Net;
using System.Text;
using System.Text.Json;
using SayMaker;

var bodies = new List<JsonElement>();
HttpClient Fake(string final) => new(new Handler(bodies, final));
void Check(bool ok, string what) { if (!ok) throw new Exception("FAIL " + what); }

var sm = new SayMakerClient("sk", Fake("{\"images\":[\"m\"],\"cleanImages\":[\"c\"]}"), "https://t");
Check(await sm.GenerateAsync("x", new ImageOptions { PollEvery = TimeSpan.Zero }) == "c", "clean first");
Check(bodies[0].GetProperty("model").GetString() == "saymaker-image-v1", "default model");
Check(bodies[1].GetProperty("taskId").GetString() == "row1", "poll body");
bodies.Clear();
sm = new SayMakerClient("sk", Fake("{\"videos\":[\"v\"]}"), "https://t");
Check(await sm.GenerateVideoAsync("x", new VideoOptions { Duration = 6, PollEvery = TimeSpan.Zero }) == "v", "video");
Check(bodies[0].GetProperty("provider").GetString() == "vgenv", "video provider");
Check(bodies[0].GetProperty("options").GetProperty("duration").GetInt32() == 6, "duration");
Check(SayMakerException.Classify("insufficient credits").Kind == SayMakerErrorKind.Quota, "quota");
Check(SayMakerException.Classify("Your current run is rendering. Plans on the pricing page").Kind == SayMakerErrorKind.Busy, "busy");
Console.WriteLine("all tests passed");

sealed class Handler(List<JsonElement> bodies, string final) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        bodies.Add(JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct)).RootElement.Clone());
        var data = r.RequestUri!.AbsolutePath == "/api/ai/generate" ? "{\"id\":\"row1\"}" : final;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":0,\"data\":" + data + "}", Encoding.UTF8, "application/json") };
    }
}
