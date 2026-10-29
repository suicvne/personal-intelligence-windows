using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Intel.Core;
public sealed class MainViewModel(Func<IntelClient> client, IAudioFactory audio, IReferenceActions references) : INotifyPropertyChanged {
    private string question = "What is going on with Crawler?", text = "", status = "Ready", audioStatus = "";
    private bool busy, connecting, useModel = true, readAloud;
    private Answer? answer;
    private CancellationTokenSource? asking, speaking;
    private Task speechTask = Task.CompletedTask;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Question { get => question; set => Set(ref question, value); }
    public string Text { get => text; private set => Set(ref text, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string AudioStatus { get => audioStatus; private set => Set(ref audioStatus, value); }
    public bool Busy { get => busy; private set { Set(ref busy, value); Changed(nameof(CanAsk)); } }
    public bool CanAsk => !Busy;
    public bool Connecting { get => connecting; private set => Set(ref connecting, value); }
    public bool UseModel { get => useModel; set => Set(ref useModel, value); }
    public bool ReadAloud { get => readAloud; set => Set(ref readAloud, value); }
    public Answer? Answer { get => answer; private set { Set(ref answer, value); Changed(nameof(Details)); } }
    public ObservableCollection<string> Lookups { get; } = [];
    public ObservableCollection<Citation> Citations { get; } = [];
    public string Details => Answer == null ? "" : string.Join("\n\n", new[] {
        Lines("Evidence", Answer.Evidence.Select(x => x.Text + " " + string.Join(", ", x.Refs))),
        Lines("Inference", Answer.Inferences.Select(x => x.Text + " " + string.Join(", ", x.Refs))),
        Lines("Missing or uncertain", Answer.Gaps),
        Lines("Ambiguous names", Answer.Ambiguous.Select(x => x.Key + ": " + string.Join(", ", x.Value))),
        Lines("Discarded references", Answer.InvalidRefs), Answer.Note ?? ""
    }.Where(x => x.Length > 0));
    private static string Lines(string title, IEnumerable<string> items) { var values = items.ToArray(); return values.Length == 0 ? "" : title + "\n" + string.Join("\n", values.Select(x => "• " + x)); }
    public async Task ConnectAsync() {
        if (Connecting) return;
        Connecting = true;
        try { await client().StatusAsync(); Status = "Connected — authenticated status received."; }
        catch (OperationCanceledException) { Status = "Connection timed out after 15 seconds."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { Connecting = false; }
    }
    public async Task AskAsync() {
        if (Busy || string.IsNullOrWhiteSpace(Question)) return;
        StopSpeech();
        Busy = true; Text = ""; Answer = null; Citations.Clear(); Lookups.Clear(); Status = "Thinking…";
        using var cancellation = new CancellationTokenSource(); asking = cancellation;
        var earlySpeech = false;
        try {
            var activeClient = client();
            await foreach (var item in activeClient.AskAsync(Question.Trim(), UseModel, cancellation.Token)) {
                cancellation.Token.ThrowIfCancellationRequested();
                using var json = JsonDocument.Parse(item.Data);
                switch (item.Name) {
                    case "lookup":
                        var lookup = Json.Read<Lookup>(item.Data);
                        Lookups.Add(lookup.Description + (lookup.Result == null ? "" : " · " + lookup.Result));
                        Status = "Looking up records…"; break;
                    case "delta": Text += json.RootElement.GetProperty("text").GetString(); Status = "Writing…"; break;
                    case "answer_text":
                        if (ReadAloud) { StartSpeech(json.RootElement.TryGetProperty("spoken_text", out var spoken) && spoken.ValueKind == JsonValueKind.String ? spoken.GetString()! : json.RootElement.GetProperty("text").GetString()!, activeClient); earlySpeech = true; }
                        break;
                    case "answer":
                        Answer = Json.Read<Answer>(item.Data); Text = Answer.Text;
                        foreach (var citation in Answer.References) Citations.Add(citation);
                        if (ReadAloud && !earlySpeech) StartSpeech(Answer.SpokenText ?? Answer.Text, activeClient);
                        Status = "Answer complete."; break;
                }
            }
        } catch (OperationCanceledException) { Status = "Stopped. Partial answer preserved."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { asking = null; Busy = false; }
    }
    public void Stop() { asking?.Cancel(); StopSpeech(); }
    public void StopSpeech() { speaking?.Cancel(); }
    public void ReadAnswer() { if (Answer != null) StartSpeech(Answer.SpokenText ?? Answer.Text, client()); }
    private void StartSpeech(string value, IntelClient activeClient) {
        speaking?.Cancel();
        var previous = speechTask;
        var cancellation = new CancellationTokenSource(); speaking = cancellation;
        speechTask = PlayAsync(previous, cancellation, value, activeClient);
    }
    private async Task PlayAsync(Task previous, CancellationTokenSource cancellation, string value, IntelClient activeClient) {
        try {
            await previous;
            cancellation.Token.ThrowIfCancellationRequested();
            AudioStatus = "Streaming audio…";
            await activeClient.SpeakAsync(value, audio, cancellation.Token);
            AudioStatus = "Audio complete.";
        } catch (OperationCanceledException) { if (speaking == cancellation) AudioStatus = "Audio stopped."; }
        catch (Exception ex) { if (speaking == cancellation) AudioStatus = "Audio: " + ex.Message; }
        finally { if (speaking == cancellation) speaking = null; cancellation.Dispose(); }
    }
    public async Task ReferenceAsync(Citation citation) {
        try {
            if (citation.Uri == null) return;
            if (citation.IsWeb) await references.OpenBrowserAsync(citation.Uri);
            else { await references.CopyAsync(citation.Uri); Status = "Remote path copied. This path belongs to the daemon host."; }
        } catch (Exception ex) { Status = "Could not open or copy reference: " + ex.Message; }
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Changed(name); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
