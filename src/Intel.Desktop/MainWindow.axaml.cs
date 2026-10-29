using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Intel.Core;
using Intel.Platform;
namespace Intel.Desktop;
public partial class MainWindow : Window, IReferenceActions {
    private readonly HttpClient http;
    private readonly Credentials credentials = new();
    private readonly bool fixture;
    public MainViewModel Model { get; }
    public MainWindow() {
        InitializeComponent();
        var args = Program.Arguments;
        fixture = !args.Contains("--live") && !args.Contains("--fixture-url");
        http = new HttpClient(fixture ? new FixtureHandler() : new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        Model = new MainViewModel(Client, new AudioFactory(), this); DataContext = Model;
        ModeLabel.Text = fixture ? "Fixture mode · synthetic records and a quiet 440 Hz test tone. No daemon connection." : args.Contains("--live") ? "Live mode · connects to the server you specify." : "Loopback fixture server · real HTTP, synthetic data.";
        if (fixture || args.Contains("--fixture-url")) {
            var index = Array.IndexOf(args, "--fixture-url");
            ServerBox.Text = index >= 0 && index + 1 < args.Length ? args[index + 1] : "http://fixture.invalid";
            TokenBox.Text = "fixture-token";
        } else {
            try {
                var settings = SettingsStore.Load(); ServerBox.Text = settings.ServerUrl; Model.ReadAloud = settings.ReadAloud;
                TokenBox.Text = credentials.Read(IntelClient.ValidateEndpoint(settings.ServerUrl).AbsoluteUri) ?? "";
            } catch (Exception ex) { ConnectionNote.Text = ex.Message + " Paste a token to use this session."; }
        }
        Closed += (_, _) => { Model.Stop(); http.Dispose(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Model.Stop(); e.Handled = true; } };
    }
    private IntelClient Client() {
        var endpoint = IntelClient.ValidateEndpoint(ServerBox.Text ?? "");
        var token = TokenBox.Text?.Trim() ?? "";
        if (token.Length == 0) throw new ArgumentException("Paste an API token in Connection first.");
        return new IntelClient(http, endpoint, token);
    }
    private async void Connect(object? sender, RoutedEventArgs e) { ConnectButton.IsEnabled = false; try { await Model.ConnectAsync(); } finally { ConnectButton.IsEnabled = true; } }
    private void Save(object? sender, RoutedEventArgs e) {
        try {
            _ = Client();
            var endpoint = IntelClient.ValidateEndpoint(ServerBox.Text!).AbsoluteUri;
            credentials.Write(endpoint, TokenBox.Text!.Trim());
            // Fixture settings must never replace a user's live endpoint.
            if (Program.Arguments.Contains("--live")) SettingsStore.Save(new Settings(endpoint, Model.ReadAloud));
            ConnectionNote.Text = "Token saved in the operating system credential store.";
        } catch (Exception ex) { ConnectionNote.Text = ex.Message + " You can choose ‘Use for this session’; the token will remain in memory only."; }
    }
    private void Session(object? sender, RoutedEventArgs e) {
        try { _ = Client(); ConnectionNote.Text = "Using the entered token in memory for this session. Nothing was saved."; Navigation.SelectedIndex = 1; }
        catch (Exception ex) { ConnectionNote.Text = ex.Message; }
    }
    private async void Ask(object? sender, RoutedEventArgs e) => await Model.AskAsync();
    private void Stop(object? sender, RoutedEventArgs e) => Model.Stop();
    private void StopAudio(object? sender, RoutedEventArgs e) => Model.StopSpeech();
    private void ReadAnswer(object? sender, RoutedEventArgs e) { try { Model.ReadAnswer(); } catch (Exception ex) { ConnectionNote.Text = ex.Message; Navigation.SelectedIndex = 0; } }
    private async void QuestionKey(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Model.AskAsync(); } }
    private async void Reference(object? sender, RoutedEventArgs e) { if (sender is Control { DataContext: Citation citation }) await Model.ReferenceAsync(citation); }
    public Task OpenBrowserAsync(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Only HTTP/HTTPS references may open in the browser.");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return Task.CompletedTask;
    }
    public async Task CopyAsync(string text) {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable.");
        await clipboard.SetTextAsync(text);
    }
}
