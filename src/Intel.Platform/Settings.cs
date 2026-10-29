using System.Text.Json;
using Intel.Core;
namespace Intel.Platform;
public sealed record Settings(string ServerUrl = "http://127.0.0.1:8765", bool ReadAloud = false);
public static class SettingsStore {
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelPrototype", "settings.json");
    public static Settings Load() => File.Exists(PathName) ? Json.Read<Settings>(File.ReadAllText(PathName)) : new();
    public static void Save(Settings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(settings, Json.Options));
        File.Move(PathName + ".tmp", PathName, true);
    }
}
