using System.Net;
using System.Text;
using Intel.Core;
using Xunit;
namespace Intel.Tests;
public class ViewModelTests {
    [Fact] public async Task FinalAnswerReplacesDeltasAndPopulatesEvidence() {
        const string events = "event: delta\ndata: {\"text\":\"draft\"}\n\nevent: answer\ndata: {\"text\":\"final\",\"evidence\":[{\"statement\":\"fact\",\"refs\":[\"R1\"]}],\"references\":[{\"ref\":\"R1\",\"label\":\"note\",\"uri\":\"/remote/note\"}]}\n\n";
        using var http = new HttpClient(new CoreTests.Handler(_ => Task.FromResult(CoreTests.Sse(events))));
        var model = Create(http); await model.AskAsync();
        Assert.Equal("final", model.Text); Assert.Contains("fact", model.Details); Assert.Single(model.Citations); Assert.False(model.Busy);
    }
    [Fact] public async Task StopPreservesPartialAnswerAndRestoresControls() {
        var bytes = Encoding.UTF8.GetBytes("event: delta\ndata: {\"text\":\"partial\"}\n\n" + new string(' ', 10000));
        using var http = new HttpClient(new CoreTests.Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream(bytes, 45, 20)) };
            response.Content.Headers.ContentType = new("text/event-stream"); return Task.FromResult(response);
        }));
        var model = Create(http); var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, e) => { if (e.PropertyName == "Text" && model.Text == "partial") arrived.TrySetResult(); };
        var task = model.AskAsync(); await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3)); model.Stop(); await task;
        Assert.Equal("partial", model.Text); Assert.Contains("Stopped", model.Status); Assert.True(model.CanAsk);
    }
    [Fact] public async Task ConnectionFailureIsVisible() {
        using var http = new HttpClient(new CoreTests.Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var model = Create(http); await model.ConnectAsync(); Assert.Contains("Authentication failed", model.Status); Assert.False(model.Connecting);
    }
    [Theory] [InlineData("https://example.com/note", true)] [InlineData("/Users/mike/notes/a.md", false)] [InlineData("C:\\notes\\a.md", false)] [InlineData("file:///notes/a", false)]
    public async Task OnlyWebReferencesOpen(string uri, bool isWeb) {
        using var http = new HttpClient(new FixtureHandler()); var actions = new Actions(); var model = Create(http, actions);
        await model.ReferenceAsync(new Citation { Uri = uri });
        Assert.Equal(isWeb ? uri : null, actions.Opened); Assert.Equal(isWeb ? null : uri, actions.Copied);
    }
    [Fact] public async Task EarlyAnswerStartsAudioOnceBeforeFinalAnswer() {
        const string early = "event: answer_text\ndata: {\"text\":\"early\",\"spoken_text\":\"say early\"}\n\n";
        const string final = "event: answer\ndata: {\"text\":\"final\",\"spoken_text\":\"say final\"}\n\n";
        var audioStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audioRequests = 0;
        using var http = new HttpClient(new CoreTests.Handler(async request => {
            if (request.RequestUri!.AbsolutePath.EndsWith("speak/stream")) {
                Interlocked.Increment(ref audioRequests);
                Assert.Contains("say early", await request.Content!.ReadAsStringAsync());
                audioStarted.TrySetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixtures.Wave()) };
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream(Encoding.UTF8.GetBytes(early), Encoding.UTF8.GetBytes(final), releaseFinal.Task)) };
            response.Content.Headers.ContentType = new("text/event-stream"); return response;
        }));
        var model = Create(http); model.ReadAloud = true;
        var ask = model.AskAsync();
        await audioStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(model.Answer); Assert.True(model.Busy);
        releaseFinal.SetResult(); await ask;
        Assert.Equal("final", model.Text); Assert.Equal(1, audioRequests);
        model.Stop();
    }
    private sealed class GatedStream(byte[] early, byte[] final, Task gate) : MemoryStream(early.Concat(final).ToArray()) {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            if (Position < early.Length) return await base.ReadAsync(buffer[..Math.Min(buffer.Length, early.Length - (int)Position)], cancellationToken);
            await gate.WaitAsync(cancellationToken); return await base.ReadAsync(buffer, cancellationToken);
        }
    }
    private static MainViewModel Create(HttpClient http, Actions? actions = null) => new(() => new IntelClient(http, new Uri("http://fixture/"), "fixture-token"), new CoreTests.FakeAudio(), actions ?? new());
    private sealed class Actions : IReferenceActions {
        public string? Opened, Copied;
        public Task OpenBrowserAsync(string url) { Opened = url; return Task.CompletedTask; }
        public Task CopyAsync(string text) { Copied = text; return Task.CompletedTask; }
    }
}
