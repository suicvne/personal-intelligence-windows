using Avalonia;
using Avalonia.Win32;
using Intel.Core;
using Intel.Platform;
namespace Intel.Desktop;
public static class Program {
    public static string[] Arguments { get; private set; } = [];
    [STAThread] public static int Main(string[] args) {
        Arguments = args;
        if (args.Contains("--smoke")) return Smoke.RunAsync(args).GetAwaiter().GetResult();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); return 0;
    }
    public static AppBuilder BuildAvaloniaApp() {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        if (Arguments.Contains("--software")) builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        return builder;
    }
}
