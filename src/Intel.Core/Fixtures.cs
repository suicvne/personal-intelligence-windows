using System.Net;
using System.Text;

namespace Intel.Core;
public static class Fixtures {
    public static string Read(string name) {
        var assembly = typeof(Fixtures).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith("." + name, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
    public static byte[] Wave() {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        const int rate = 24000;
        writer.Write("RIFF"u8); writer.Write(uint.MaxValue); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(uint.MaxValue);
        for (var i = 0; i < rate * 3; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 1800));
        return memory.ToArray();
    }
}
public sealed class FixtureHandler : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        if (request.Headers.Authorization?.Parameter != "fixture-token")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"Use fixture-token in fixture mode.\"}") });
        var path = request.RequestUri!.AbsolutePath;
        byte[] bytes;
        string type;
        if (path.EndsWith("/api/status")) { bytes = Encoding.UTF8.GetBytes(Fixtures.Read("status.json")); type = "application/json"; }
        else if (path.EndsWith("/api/ask/stream")) { bytes = Encoding.UTF8.GetBytes(Fixtures.Read("ask_stream_ui.txt")); type = "text/event-stream"; }
        else if (path.EndsWith("/api/speak/stream")) { bytes = Fixtures.Wave(); type = "audio/wav"; }
        else return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream(bytes, type == "audio/wav" ? 2400 : 80, 45)) };
        response.Content.Headers.ContentType = new(type);
        return Task.FromResult(response);
    }
}
public sealed class DelayedStream(byte[] bytes, int chunkSize, int delayMs) : MemoryStream(bytes, false) {
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
        await Task.Delay(delayMs, cancellationToken);
        return await base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
    }
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
}
