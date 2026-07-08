using System.Text.Json;

namespace Runway.Settings;

public static class SettingsLoader
{
    private const string FileName = "settings.json";

    // Если файла нет — создаём с значениями по умолчанию и возвращаем их же
    public static AppSettings Load()
    {
        if (!File.Exists(FileName))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        string json = File.ReadAllText(FileName);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(FileName, json);
    }
}
