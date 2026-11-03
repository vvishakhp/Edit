using System.Text.Json;
using Edit.Core;
using Edit.Platform;

namespace Edit.App.Settings;

/// <summary>Persists user settings under <see cref="AppPaths.SettingsPath"/>.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsModel Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                return JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
            }
        }
        catch
        {
            // Corrupt or unreadable settings — fall through to defaults.
        }

        var model = CreateDefaults();
        Save(model);
        return model;
    }

    public void Save(SettingsModel model)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsPath)!);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(model, JsonOptions));
    }

    public static SettingsModel CreateDefaults() => new()
    {
        SchemaVersion = 1,
        LanguageServers = [],
        Keybindings = KeybindingDefaults.Create()
    };
}
