namespace Melotag.Core.Tests;

using System.Text.Json;
using Melotag.Storage;

public class AppSettingsTests
{
    [Fact]
    public void Migrate_InsertsAcoustIdAfterMusicBrainz()
    {
        var settings = new AppSettings();
        settings.Sources.Tags.Clear();
        settings.Sources.Tags.Add("netease");
        settings.Sources.Tags.Add("musicbrainz");
        settings.Sources.Tags.Add("itunes");
        settings.SettingsVersion = 6;

        settings.Migrate();

        var index = settings.Sources.Tags.IndexOf("acoustid");
        Assert.True(index > 0);
        Assert.Equal("musicbrainz", settings.Sources.Tags[index - 1]);
        Assert.Equal(7, settings.SettingsVersion);
    }

    /// <summary>
    /// 旧配置或手写 JSON 里把子对象/集合写成 null 时，Migrate 必须先兜底再迁移，
    /// 否则启动直接 NullReferenceException（用户看到的是打不开的程序）。
    /// </summary>
    [Fact]
    public void Migrate_JsonWithNullSections_DoesNotThrowAndFillsDefaults()
    {
        const string json = """
        {
          "SettingsVersion": 0,
          "Search": null,
          "Writing": null,
          "RecentDirectories": null,
          "Sources": { "Tags": null, "Pictures": null }
        }
        """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;

        settings.Migrate();

        Assert.NotNull(settings.Search);
        Assert.NotNull(settings.Writing);
        Assert.NotNull(settings.Sources);
        Assert.NotNull(settings.RecentDirectories);
        Assert.Empty(settings.RecentDirectories);
        Assert.NotNull(settings.Sources.Tags);
        Assert.NotNull(settings.Sources.Pictures);
        Assert.Contains("acoustid", settings.Sources.Tags);
        Assert.Equal(7, settings.SettingsVersion);
    }

    [Fact]
    public void Migrate_NullSections_IsIdempotent()
    {
        var settings = new AppSettings
        {
            Search = null!,
            Sources = null!,
            Lyrics = null!,
            Pictures = null!,
            Writing = null!,
            Ui = null!,
            RecentDirectories = null!,
            ManagedDirectories = null!,
        };

        settings.Migrate();
        var first = string.Join(',', settings.Sources.Tags);

        settings.Migrate();

        Assert.Equal(first, string.Join(',', settings.Sources.Tags));
        Assert.Equal(7, settings.SettingsVersion);
    }

    /// <summary>
    /// 旧配置（SettingsVersion &lt; 4）补齐新默认源时，逐条 Insert(0, id) 会把默认顺序整个反转，
    /// 升级后源的优先级就乱了。合并后的顺序必须与 SourceDefaults 一致。
    /// </summary>
    [Fact]
    public void Migrate_LegacyTags_DoesNotReverseDefaultOrder()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 3,
            Sources = new SourceSettings { Tags = ["musicbrainz", "itunes"] },
        };

        settings.Migrate();

        Assert.Equal(
            new[] { "netease", "qq", "kugou", "kuwo", "musicbrainz", "acoustid", "itunes" },
            settings.Sources.Tags.ToArray());
    }

    [Fact]
    public void Migrate_LegacyEmptyTags_ProducesDefaultOrder()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 3,
            Sources = new SourceSettings { Tags = [] },
        };

        settings.Migrate();

        // 旧配置里没有 musicbrainz 时 AcoustID 追加到末尾，但四个默认源必须保持默认顺序。
        Assert.Equal(new[] { "netease", "qq", "kugou", "kuwo", "acoustid" }, settings.Sources.Tags.ToArray());
    }

    [Fact]
    public void Migrate_AlreadyCurrent_KeepsUserOrderAndDisables()
    {
        // 用户取消勾选的源不能被迁移重新加回来。
        var settings = new AppSettings
        {
            SettingsVersion = 7,
            Sources = new SourceSettings { Tags = ["itunes", "netease"] },
        };

        settings.Migrate();

        Assert.Equal(new[] { "itunes", "netease" }, settings.Sources.Tags.ToArray());
    }
}
