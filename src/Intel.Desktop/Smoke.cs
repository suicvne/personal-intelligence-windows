using System.Text.Json;
using Intel.Core;
using Intel.Platform;
namespace Intel.Desktop;
public static class Smoke {
    public static async Task<int> RunAsync(string[] args) {
        var results = new Dictionary<string, object>();
        var index = Array.IndexOf(args, "--fixture-url");
        var endpoint = index >= 0 && index + 1 < args.Length ? IntelClient.ValidateEndpoint(args[index + 1]) : new Uri("http://fixture.invalid/");
        using var http = new HttpClient(index >= 0 ? new HttpClientHandler { AllowAutoRedirect = false } : new FixtureHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new IntelClient(http, endpoint, "fixture-token");
        async Task Check(string name, Func<Task> action) {
            try { await action(); results[name] = "PASS"; } catch (Exception ex) { results[name] = "FAIL: " + ex.Message; }
        }
        await Check("authenticated_http", async () => { await client.StatusAsync(); });
        await Check("answer_stream", async () => {
            var names = new HashSet<string>();
            await foreach (var item in client.AskAsync("fixture", true)) names.Add(item.Name);
            if (!new[] { "lookup", "delta", "answer_text", "answer" }.All(names.Contains)) throw new Exception("Missing events.");
        });
        await Check("cancellation", async () => {
            using var stop = new CancellationTokenSource(100);
            try { await foreach (var _ in client.AskAsync("fixture", true, stop.Token)) { } throw new Exception("Cancellation did not interrupt the stream."); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        if (args.Contains("--credentials") || args.Contains("--credentials-read")) await Check("credentials", () => {
            var store = new Credentials();
            if (args.Contains("--credentials")) store.Write("IntelPrototype-smoke-test", "synthetic-token");
            if (store.Read("IntelPrototype-smoke-test") != "synthetic-token") throw new Exception("Credential roundtrip mismatch.");
            return Task.CompletedTask;
        });
        if (args.Contains("--audio")) {
            await Check("audio_stream", () => client.SpeakAsync("synthetic tone", new AudioFactory(), CancellationToken.None));
            await Check("audio_cancel", async () => {
                using var stop = new CancellationTokenSource(700);
                try { await client.SpeakAsync("synthetic tone", new AudioFactory(), stop.Token); throw new Exception("Audio did not cancel."); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
        }
        var output = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var outputIndex = Array.IndexOf(args, "--report");
        if (outputIndex >= 0 && outputIndex + 1 < args.Length) File.WriteAllText(args[outputIndex + 1], output);
        Console.WriteLine(output);
        return results.Values.Any(x => x.ToString()!.StartsWith("FAIL")) ? 1 : 0;
    }
}
