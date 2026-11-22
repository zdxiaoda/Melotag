namespace Melotag.Storage;

using System.Globalization;
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

    /// <summary>保证同一实例的写入串行化（读-改-写设置时不会互相覆盖半写文件）。</summary>
    private readonly object _saveLock = new();

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
                return CreateDefault();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            settings.Migrate();
            return settings;
        }
        catch (JsonException)
        {
            // 配置内容损坏：隔离后按默认值启动
            return RecoverFromBrokenFile();
        }
        catch (IOException)
        {
            return RecoverFromBrokenFile();
        }
        catch (UnauthorizedAccessException)
        {
            return RecoverFromBrokenFile();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_saveLock)
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 临时文件名带进程号：多进程（桌面 + 自启实例）共用同一 settings.json 时不会撞同一个 tmp。
            var tmp = SettingsPath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
    }

    /// <summary>默认配置也要走一次迁移，避免 SettingsVersion 一直是 0、每次启动重复迁移。</summary>
    private static AppSettings CreateDefault()
    {
        var fresh = new AppSettings();
        fresh.Migrate();
        return fresh;
    }

    private AppSettings RecoverFromBrokenFile()
    {
        QuarantineBrokenFile();
        return CreateDefault();
    }

    /// <summary>把损坏的 settings.json 改名/复制成 settings.json.bad-&lt;时间戳&gt;，便于事后排查。</summary>
    private void QuarantineBrokenFile()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var badPath = SettingsPath + ".bad-" + stamp;
            for (var index = 1; File.Exists(badPath); index++)
                badPath = SettingsPath + ".bad-" + stamp + "-" + index.ToString(CultureInfo.InvariantCulture);

            try
            {
                File.Move(SettingsPath, badPath);
                return;
            }
            catch (IOException)
            {
                // 文件被占用时退化为复制（下次启动还能再试一次改名）
                File.Copy(SettingsPath, badPath);
            }
        }
        catch (IOException)
        {
            // 隔离失败不影响启动
        }
        catch (UnauthorizedAccessException)
        {
            // 隔离失败不影响启动
        }
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
            if (!string.IsNullOrWhiteSpace(OverrideDataDirectory)) return OverrideDataDirectory;

            var explicitDir = Environment.GetEnvironmentVariable("MELOTAG_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;

            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(root, "Melotag");
        }
    }
}
