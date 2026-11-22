namespace Melotag.Core.Tests;

using System.Text.Json;
using Melotag.Storage;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public JsonSettingsStoreTests()
    {
        _dir = Directory.CreateTempSubdirectory("melotag-settings-").FullName;
    }

    [Fact]
    public void Load_MissingFile_ReturnsMigratedDefaults()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "settings.json"));

        var settings = store.Load();

        Assert.Equal(7, settings.SettingsVersion);
        Assert.Contains("acoustid", settings.Sources.Tags);
        Assert.False(File.Exists(store.SettingsPath));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = store.Load();
        settings.Sources.Tags = ["itunes", "netease"];
        settings.Search.UseOnlyFileName = true;

        store.Save(settings);
        var reloaded = new JsonSettingsStore(store.SettingsPath).Load();

        Assert.Equal(new[] { "itunes", "netease" }, reloaded.Sources.Tags.ToArray());
        Assert.True(reloaded.Search.UseOnlyFileName);
    }

    /// <summary>
    /// 配置损坏时不能直接崩，也不能悄悄覆盖用户文件：按默认值启动，并把原文件改名成
    /// settings.json.bad-* 留档，方便事后排查。
    /// </summary>
    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndQuarantinesFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ this is not valid json ");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(7, settings.SettingsVersion);
        Assert.Contains("acoustid", settings.Sources.Tags);

        var quarantined = Directory.GetFiles(_dir, "settings.json.bad-*");
        Assert.Single(quarantined);
        Assert.False(File.Exists(path));
        Assert.Contains("not valid json", File.ReadAllText(quarantined[0]));
    }

    [Fact]
    public void Load_TruncatedJson_ReturnsDefaultsAndQuarantinesFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"SettingsVersion":3,"Sources":{"Tags":["netease"]""");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(7, settings.SettingsVersion);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.bad-*"));
    }

    [Fact]
    public void Load_JsonWithNullSections_IsMigratedInsteadOfThrowing()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
        {"SettingsVersion":0,"Search":null,"Writing":null,"RecentDirectories":null,"Sources":{"Tags":null}}
        """);
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.NotNull(settings.Search);
        Assert.NotNull(settings.Writing);
        Assert.NotNull(settings.RecentDirectories);
        Assert.NotNull(settings.Sources.Tags);
        Assert.Contains("acoustid", settings.Sources.Tags);
        // 合法 JSON 只是内容不完整，不应被隔离
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.bad-*"));
    }

    /// <summary>
    /// 设置路径可能是纯文件名（便携版放在工作目录）。此时 Path.GetDirectoryName 返回 ""，
    /// Save 不能对空路径调用 Directory.CreateDirectory。
    /// </summary>
    [Fact]
    public void Save_PlainFileNamePath_DoesNotThrow()
    {
        var fileName = "melotag-test-" + Guid.NewGuid().ToString("N") + ".json";
        var store = new JsonSettingsStore(fileName);
        var settings = store.Load();

        try
        {
            store.Save(settings);

            Assert.True(File.Exists(fileName));
            var reloaded = new JsonSettingsStore(fileName).Load();
            Assert.Equal(7, reloaded.SettingsVersion);
        }
        finally
        {
            if (File.Exists(fileName)) File.Delete(fileName);
            foreach (var tmp in Directory.GetFiles(".", fileName + ".*.tmp"))
                File.Delete(tmp);
        }
    }

    /// <summary>
    /// 多线程重复保存（设置页各处并发写）后，磁盘上的文件必须仍是一个完整的 JSON，
    /// 不能出现半写/交错内容。
    /// </summary>
    [Fact]
    public void Save_ConcurrentWriters_LeaveFileDeserializable()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);

        Parallel.For(0, 40, i =>
        {
            var settings = new AppSettings
            {
                SettingsVersion = 7,
                Sources = new SourceSettings { Tags = ["netease", "itunes"] },
                Search = new SearchSettings { ItemsPerSource = 5 + (i % 5) },
            };

            store.Save(settings);
        });

        var json = File.ReadAllText(path);
        var reloaded = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(reloaded);
        Assert.Equal(new[] { "netease", "itunes" }, reloaded!.Sources.Tags.ToArray());
        Assert.InRange(reloaded.Search.ItemsPerSource, 5, 9);
        // 临时文件不应残留
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败不影响结论 */ }
    }
}
