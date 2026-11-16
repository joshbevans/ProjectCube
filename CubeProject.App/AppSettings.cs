using System.IO;
using System.Text.Json;

namespace CubeProject.App;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CubeProject", "settings.json");

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    public string? AudioDeviceId { get; set; }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
