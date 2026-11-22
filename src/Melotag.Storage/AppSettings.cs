namespace Melotag.Storage;

public sealed class SearchSettings
{
    public bool UseOnlyFileName { get; set; }
    public bool UseArtist { get; set; } = true;
    public bool UseAlbum { get; set; } = true;
    public int ItemsPerSource { get; set; } = 10;
    public int Threads { get; set; } = 4;
    public int AutoMatchThreads { get; set; } = 2;
}

/// <summary>各类别默认的在线源顺序；设置界面「恢复默认」与旧配置迁移共用这一份。</summary>
public static class SourceDefaults
{
    public static readonly string[] Tags =
        ["netease", "qq", "kugou", "kuwo", "musicbrainz", "acoustid", "itunes", "lastfm"];
    public static readonly string[] Pictures = ["netease", "kugou", "kuwo", "itunes", "coverart"];
    public static readonly string[] Lyrics = ["netease", "qq", "kugou", "lrclib"];

    public static readonly string[] All =
        ["netease", "qq", "kugou", "kuwo", "musicbrainz", "acoustid", "coverart", "itunes", "lastfm", "lrclib"];
}

public sealed class SourceSettings
{
    public List<string> Tags { get; set; } = [.. SourceDefaults.Tags];
    public List<string> Pictures { get; set; } = [.. SourceDefaults.Pictures];
    public List<string> Lyrics { get; set; } = [.. SourceDefaults.Lyrics];
    public string ConnectorsArtists { get; set; } = " / ";
    public string ConnectorsLyricAndTranslation { get; set; } = "\n";
    public string LastFmApiKey { get; set; } = "";
    public string ITunesCountry { get; set; } = "CN";
}

public sealed class LyricsSettings
{
    public bool ReformatTimeTags { get; set; } = true;
    public bool RemoveTimeTags { get; set; }
    public bool DeleteBlankLines { get; set; } = true;
    public bool DeleteHeadTags { get; set; } = true;
    public bool DownloadTranslation { get; set; }
    public bool DontDownloadLyricWithInstrumentInTitle { get; set; } = true;
    public string SaveDirectory { get; set; } = "";
    public string FileNameFormat { get; set; } = "{title}";
    public string Encoding { get; set; } = "utf-8";
}

public sealed class PictureSettings
{
    /// <summary>插入封面时保留原始图片（不缩放、不重压缩）；关闭后才按下面的上限处理。</summary>
    public bool KeepOriginal { get; set; } = true;

    public int MaxSizeKb { get; set; } = 1024;
    public int MaxWidth { get; set; } = 1200;
    public int MaxHeight { get; set; } = 1200;
    public List<string> AllowedFormats { get; set; } = ["jpg", "png"];
    public bool OverwriteEditor { get; set; } = true;
}

public sealed class WriteSettings
{
    public string Id3v2Version { get; set; } = "2.3";
    public bool KeepModifiedTime { get; set; } = true;
    public bool WriteLyricsToTag { get; set; } = true;
    public bool CommentTagWrite163Key { get; set; }
}

public sealed class UiSettings
{
    /// <summary>"auto" = 跟随系统；也可为 zh-Hans / ja / en。</summary>
    public string Language { get; set; } = "auto";
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool UseCompactRows { get; set; } = true;
    public string Theme { get; set; } = "Default";
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;

    /// <summary>已提示过更新日志的版本；与当前版本不同（或为空）时启动后弹一次更新内容。</summary>
    public string LastSeenVersion { get; set; } = "";

    /// <summary>桌面列表可见列（含顺序）；空表示使用内置默认列。</summary>
    public List<string> VisibleColumns { get; set; } = [];

    /// <summary>列表排序字段（列 id）；空表示不排序，保持导入顺序。</summary>
    public string SortColumn { get; set; } = "";

    public bool SortDescending { get; set; }
}

public sealed class AppSettings
{
    /// <summary>配置结构版本；用于把新增的数据源合并进旧配置（0 = 旧配置）。</summary>
    public int SettingsVersion { get; set; }

    public SearchSettings Search { get; set; } = new();
    public SourceSettings Sources { get; set; } = new();
    public LyricsSettings Lyrics { get; set; } = new();
    public PictureSettings Pictures { get; set; } = new();
    public WriteSettings Writing { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public List<string> RecentDirectories { get; set; } = [];
    public List<string> ManagedDirectories { get; set; } = [];

    public void Migrate()
    {
        // 旧配置（或 JSON 里显式写 null）会让所有子对象 / 集合为 null；
        // 这里一次性兜底，后续迁移逻辑才能安全地假设它们非 null。
        Search ??= new SearchSettings();
        Sources ??= new SourceSettings();
        Lyrics ??= new LyricsSettings();
        Pictures ??= new PictureSettings();
        Writing ??= new WriteSettings();
        Ui ??= new UiSettings();
        RecentDirectories ??= [];
        ManagedDirectories ??= [];

        Sources.Tags ??= [];
        Sources.Pictures ??= [];
        Sources.Lyrics ??= [];

        Pictures.AllowedFormats ??= ["jpg", "png"];
        Ui.VisibleColumns ??= [];

        if (SettingsVersion < 4)
        {
            Sanitize(Sources.Tags);
            Sanitize(Sources.Pictures);
            Sanitize(Sources.Lyrics);

            MergeInto(Sources.Tags, ["netease", "qq", "kugou", "kuwo"]);
            MergeInto(Sources.Pictures, ["netease", "kugou", "kuwo"]);
            MergeInto(Sources.Lyrics, ["netease", "qq", "kugou", "lrclib"]);
            SettingsVersion = 4;
        }

        if (SettingsVersion < 5)
        {
            if (string.IsNullOrWhiteSpace(Ui.Language)) Ui.Language = "auto";
            else if (Ui.Language is "zh-CHS" or "zh-CN") Ui.Language = "zh-Hans";

            SettingsVersion = 5;
        }

        if (SettingsVersion < 7)
        {
            // AcoustID 指纹源并入已有列表，排在 MusicBrainz 之后，其余顺序不变。
            Sanitize(Sources.Tags);
            if (!Sources.Tags.Contains("acoustid", StringComparer.OrdinalIgnoreCase))
            {
                var anchor = Sources.Tags.FindIndex(
                    id => id.Equals("musicbrainz", StringComparison.OrdinalIgnoreCase));
                Sources.Tags.Insert(anchor < 0 ? Sources.Tags.Count : anchor + 1, "acoustid");
            }

            SettingsVersion = 7;
        }

        static void MergeInto(List<string> list, string[] ids)
        {
            // 先按 ids 顺序收集缺失项，再一次性插到最前面：
            // 逐条 Insert(0, id) 会把新默认源顺序反转（旧配置升级后源顺序错乱）。
            var missing = new List<string>();
            foreach (var id in ids)
            {
                if (!list.Contains(id, StringComparer.OrdinalIgnoreCase)) missing.Add(id);
            }

            if (missing.Count > 0) list.InsertRange(0, missing);
        }

        static void Sanitize(List<string> list)
        {
            list.RemoveAll(id => !SourceDefaults.All.Contains(id, StringComparer.OrdinalIgnoreCase));
        }
    }
}
