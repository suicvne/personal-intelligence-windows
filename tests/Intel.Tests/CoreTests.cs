using System.Net;
using System.Text;
using Intel.Core;
using Xunit;
namespace Intel.Tests;
public class CoreTests {
    [Fact] public void ContractSamplesDecode() {
        var answer = Json.Read<Answer>(Fixtures.Read("ask.json"));
        Assert.NotEmpty(answer.Text); Assert.NotEmpty(answer.References); Assert.Contains(answer.References, x => x.Uri != null);
        using var status = System.Text.Json.JsonDocument.Parse(Fixtures.Read("status.json"));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, status.RootElement.ValueKind);
        var statements = Json.Read<Answer>("{\"text\":\"x\",\"evidence\":[{\"statement\":\"Fact\",\"refs\":[\"R1\"]}]}");
        Assert.Equal("Fact", statements.Evidence[0].Text);
    }
    [Fact] public async Task SseHandlesFragmentedUtf8MultilineAndComments() {
        var text = ": heartbeat\r\nevent: delta\r\ndata: {\"text\":\r\ndata: \"café 🧠\"}\r\n\r\n";
        using var stream = new DelayedStream(Encoding.UTF8.GetBytes(text), 1, 0);
        var events = new List<AskEvent>();
        await foreach (var item in IntelClient.ReadEventsAsync(stream)) events.Add(item);
        Assert.Single(events); Assert.Equal("delta", events[0].Name);
        Assert.Contains("café 🧠", events[0].Data); Assert.Contains('\n', events[0].Data);
    }
    [Fact] public async Task SseDropsUnterminatedEvent() {
        using var stream = new MemoryStream("event: delta\ndata: unfinished"u8.ToArray());
        var items = new List<AskEvent>(); await foreach (var item in IntelClient.ReadEventsAsync(stream)) items.Add(item);
        Assert.Empty(items);
    }
    [Fact] public async Task RequestHasAuthAndSnakeCasePayloadAndSkipsUnknownEvents() {
        var handler = new Handler(async request => {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("secret", request.Headers.Authorization?.Parameter);
            Assert.Equal("/api/ask/stream", request.RequestUri!.AbsolutePath);
            Assert.Contains("\"use_llm\":false", await request.Content!.ReadAsStringAsync());
            return Sse("event: future\ndata: {}\n\nevent: answer\ndata: {\"text\":\"done\"}\n\n");
        });
        using var http = new HttpClient(handler); var client = new IntelClient(http, new Uri("http://test/"), "secret");
        var events = new List<AskEvent>(); await foreach (var item in client.AskAsync("question", false)) events.Add(item);
        Assert.Single(events); Assert.Equal("answer", events[0].Name);
    }
    [Theory] [InlineData(401, "Authentication failed")] [InlineData(500, "HTTP 500")]
    public async Task ServerFailuresAreActionable(int code, string expected) {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{\"error\":\"fixture failure\"}") })));
        var error = await Assert.ThrowsAsync<ApiException>(() => new IntelClient(http, new Uri("http://test/"), "x").StatusAsync());
        Assert.Contains(expected, error.Message); Assert.Contains("fixture failure", error.Message);
    }
    [Theory] [InlineData("event: delta\ndata: {\"text\":\"partial\"}\n\n", "before the final answer")]
    [InlineData("event: error\ndata: {\"error\":\"model unavailable\"}\n\n", "model unavailable")]
    public async Task StreamFailuresPreserveReason(string data, string expected) {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Sse(data))));
        var error = await Assert.ThrowsAsync<ApiException>(async () => { await foreach (var _ in new IntelClient(http, new Uri("http://test/"), "x").AskAsync("q", true)) { } });
        Assert.Contains(expected, error.Message);
    }
    [Fact] public async Task StreamingCancelsWhileAwaitingBytes() {
        using var http = new HttpClient(new FixtureHandler());
        using var stop = new CancellationTokenSource(80);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await foreach (var _ in new IntelClient(http, new Uri("http://fixture/"), "fixture-token").AskAsync("q", true, stop.Token)) { } });
    }
    [Theory] [InlineData("file:///tmp/intel")] [InlineData("http://user:secret@host/")] [InlineData("http://host/?token=x")]
    public void InvalidEndpointRejected(string input) => Assert.Throws<ArgumentException>(() => IntelClient.ValidateEndpoint(input));
    [Fact] public void WavParsesEverySplitBoundary() {
        var bytes = Fixtures.Wave();
        for (var size = 1; size < 80; size += 7) {
            var parser = new WavReader(); var count = 0;
            for (var offset = 0; offset < bytes.Length; offset += size) count += parser.Feed(bytes.AsSpan(offset, Math.Min(size, bytes.Length - offset))).Length;
            parser.Complete(); Assert.Equal(new WavFormat(24000, 1), parser.Format); Assert.Equal(bytes.Length - 44, count);
        }
    }
    [Fact] public void WavRejectsUnsupportedFormat() {
        var data = Fixtures.Wave(); data[34] = 8;
        Assert.Throws<InvalidDataException>(() => new WavReader().Feed(data));
    }
    [Fact] public void WavRejectsShortFormatWithoutOutOfBoundsRead() {
        var data = Fixtures.Wave(); data[16] = 2;
        Assert.Throws<InvalidDataException>(() => new WavReader().Feed(data));
    }
    [Fact] public void WavRejectsTruncatedHeaderFrameAndFinitePayload() {
        var header = new WavReader(); header.Feed("RIFF"u8); Assert.Throws<InvalidDataException>(header.Complete);
        var bytes = Fixtures.Wave(); var frame = new WavReader(); frame.Feed(bytes.AsSpan(0, 45)); Assert.Throws<InvalidDataException>(frame.Complete);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1000);
        var finite = new WavReader(); finite.Feed(bytes.AsSpan(0, 80)); Assert.Throws<InvalidDataException>(finite.Complete);
    }
    [Fact] public async Task AudioWritesBeforeDownloadCompletesAndDisposesOnCancel() {
        var stream = new DelayedStream(Fixtures.Wave(), 2444, 2);
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        using var stop = new CancellationTokenSource();
        var output = new FakeAudio(() => { Assert.True(stream.Position < stream.Length); stop.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IntelClient(http, new Uri("http://test/"), "x").SpeakAsync("x", output, stop.Token));
        Assert.True(output.Disposed);
    }
    internal static HttpResponseMessage Sse(string data) { var content = new StringContent(data); content.Headers.ContentType = new("text/event-stream"); return new(HttpStatusCode.OK) { Content = content }; }
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
    internal sealed class FakeAudio(Action? write = null) : IAudioFactory, IAudioOutput {
        public bool Disposed;
        public IAudioOutput Open(WavFormat format) => this;
        public Task WriteAsync(byte[] pcm, CancellationToken cancellationToken) { write?.Invoke(); cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }
}
