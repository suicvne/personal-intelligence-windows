using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Intel.Core;

public sealed class IntelClient(HttpClient http, Uri endpoint, string token) {
    public static Uri ValidateEndpoint(string value) {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP or HTTPS server URL without credentials, a query, or a fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null) {
        var request = new HttpRequestMessage(method, new Uri(endpoint, path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");
        return request;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex) { throw new ApiException($"Cannot reach the daemon. Check the server URL and network connection. {ex.Message}"); }
        if (response.IsSuccessStatusCode) return response;
        using (response) {
            var message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Authentication failed. Check the API token in Connection."
                : $"Server returned HTTP {(int)response.StatusCode}.";
            var body = await response.Content.ReadAsStringAsync(ct);
            try {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var error)) message += " " + error.GetString();
            } catch (JsonException) { }
            throw new ApiException(message);
        }
    }
    public async Task<string> StatusAsync(CancellationToken ct = default) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = Request(HttpMethod.Get, "api/status");
        using var response = await SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        using var json = JsonDocument.Parse(text);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ApiException("The daemon returned an invalid status object.");
        return text;
    }
    public async IAsyncEnumerable<AskEvent> AskAsync(string question, bool useLlm, [EnumeratorCancellation] CancellationToken ct = default) {
        using var request = Request(HttpMethod.Post, "api/ask/stream", new { question, useLlm });
        using var response = await SendAsync(request, ct);
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new ApiException("Expected an event stream from the daemon.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var completed = false;
        await foreach (var item in ReadEventsAsync(stream, ct)) {
            if (item.Name == "error") {
                using var error = JsonDocument.Parse(item.Data);
                throw new ApiException(error.RootElement.GetProperty("error").GetString() ?? "Streaming failed.");
            }
            if (item.Name is not ("lookup" or "delta" or "answer_text" or "answer")) continue;
            if (item.Name == "answer") completed = true;
            yield return item;
        }
        if (!completed) throw new ApiException("The connection ended before the final answer arrived. Partial text has been preserved.");
    }
    public static async IAsyncEnumerable<AskEvent> ReadEventsAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default) {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1024, leaveOpen: true);
        var name = "message";
        var data = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line) {
            if (line.Length == 0) {
                if (data.Count > 0) yield return new AskEvent(name, string.Join('\n', data));
                name = "message"; data.Clear(); continue;
            }
            if (line.StartsWith(':')) continue;
            var split = line.IndexOf(':');
            var field = split < 0 ? line : line[..split];
            var value = split < 0 ? "" : line[(split + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            if (field == "event") name = value;
            if (field == "data") data.Add(value);
            if (data.Sum(x => x.Length) > 4 * 1024 * 1024) throw new ApiException("An event exceeded the 4 MiB safety limit.");
        }
        // SSE dispatches only complete, blank-line-terminated events.
    }
    public async Task SpeakAsync(string text, IAudioFactory factory, CancellationToken ct) {
        using var request = Request(HttpMethod.Post, "api/speak/stream", new { text });
        using var response = await SendAsync(request, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var parser = new WavReader();
        IAudioOutput? output = null;
        try {
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0) {
                var pcm = parser.Feed(buffer.AsSpan(0, read));
                if (parser.Format != null) output ??= factory.Open(parser.Format);
                if (pcm.Length > 0) await output!.WriteAsync(pcm, ct);
            }
            parser.Complete();
            if (output != null) await output.DrainAsync(ct);
        } finally { output?.Dispose(); }
    }
}
