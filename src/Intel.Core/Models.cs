using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intel.Core;

public static class Json {
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("Empty JSON response.");
}
public sealed record Lookup(string Text, string Description, string? Result);
public sealed record Statement {
    [JsonPropertyName("statement")] public string Text { get; init; } = "";
    public string[] Refs { get; init; } = [];
}
public sealed record Citation {
    [JsonPropertyName("ref")] public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Uri { get; init; }
    public bool IsWeb => System.Uri.TryCreate(Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";
    public string ActionLabel => IsWeb ? "Open in browser" : "Copy remote path";
}
public sealed record Answer {
    public string Text { get; init; } = "";
    public string? SpokenText { get; init; }
    public Statement[] Evidence { get; init; } = [];
    public Statement[] Inferences { get; init; } = [];
    public string[] Gaps { get; init; } = [];
    public Citation[] References { get; init; } = [];
    public Dictionary<string, string[]> Ambiguous { get; init; } = [];
    public string[] InvalidRefs { get; init; } = [];
    public string? Note { get; init; }
    public Lookup[] Lookups { get; init; } = [];
}
public sealed record AskEvent(string Name, string Data);
public sealed class ApiException(string message) : Exception(message);
public interface ICredentialStore {
    string? Read(string endpoint);
    void Write(string endpoint, string token);
}
public interface IReferenceActions {
    Task OpenBrowserAsync(string url);
    Task CopyAsync(string text);
}
public interface IAudioOutput : IDisposable {
    Task WriteAsync(byte[] pcm, CancellationToken cancellationToken);
    Task DrainAsync(CancellationToken cancellationToken);
}
public interface IAudioFactory { IAudioOutput Open(WavFormat format); }
