using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Intel.Desktop;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Intel.Tests.TestApp))]
namespace Intel.Tests;
public static class TestApp {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public class UiTests {
    [AvaloniaFact] public void WindowLoadsAndHasKeyboardAccessibleInputs() {
        var window = new MainWindow(); window.Show();
        Assert.Equal(2, window.FindControl<TabControl>("Navigation")!.ItemCount);
        Assert.True(window.FindControl<TextBox>("TokenBox")!.PasswordChar != '\0');
        Assert.True(window.FindControl<Button>("ConnectButton")!.IsTabStop);
        window.FindControl<TabControl>("Navigation")!.SelectedIndex = 1;
        Assert.True(window.FindControl<TextBox>("QuestionBox")!.Focus());
        window.Close();
    }
    [AvaloniaFact] public async Task ConnectionAndStreamingStateBindToControls() {
        var window = new MainWindow(); window.Show();
        window.FindControl<TextBox>("TokenBox")!.Text = "wrong";
        await window.Model.ConnectAsync();
        Assert.Contains("Authentication failed", window.FindControl<TextBlock>("StatusText")!.Text);
        window.FindControl<TextBox>("TokenBox")!.Text = "fixture-token";
        window.FindControl<TabControl>("Navigation")!.SelectedIndex = 1;
        var ask = window.Model.AskAsync();
        Assert.False(window.FindControl<Button>("AskButton")!.IsEnabled);
        Assert.True(window.FindControl<Button>("StopButton")!.IsEnabled);
        window.Model.Stop(); await ask;
        Assert.True(window.FindControl<Button>("AskButton")!.IsEnabled);
        window.Close();
    }
}
