namespace Melotag.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;

public interface ISettingsStore
{
    string SettingsPath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public JsonSettingsStore(string? path = null)
    {
        SettingsPath = path ?? Path.Combine(AppPaths.DataDirectory, "settings.json");
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var fresh = new AppSettings();
                fresh.Migrate();
                return fresh;
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            settings.Migrate();
            return settings;
        }
        catch
        {
            // 配置损坏不应阻止启动
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, SettingsPath, overwrite: true);
    }
}

public static class AppPaths
{
    /// <summary>平台头（Android）在启动最早期设置，优先于环境变量。</summary>
    public static string? OverrideDataDirectory { get; set; }

    public static string DataDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OverrideDataDirectory)) return OverrideDataDirectory!;

            var explicitDir = Environment.GetEnvironmentVariable("MELOTAG_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;

            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(root, "Melotag");
        }
    }
}
