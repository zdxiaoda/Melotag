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
    public static readonly string[] Tags = ["netease", "qq", "kugou", "kuwo", "musicbrainz", "itunes", "lastfm"];
    public static readonly string[] Pictures = ["netease", "kugou", "kuwo", "itunes", "coverart"];
    public static readonly string[] Lyrics = ["netease", "qq", "kugou", "lrclib"];

    public static readonly string[] All = ["netease", "qq", "kugou", "kuwo", "musicbrainz", "coverart", "itunes", "lastfm", "lrclib"];

    public static string[] For(string category) => category switch
    {
        "pictures" => Pictures,
        "lyrics" => Lyrics,
        _ => Tags,
    };
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
    public string UserAgent { get; set; } = Melotag.Core.ProductInfo.UserAgent;
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
        Ui ??= new UiSettings();
        Sources ??= new SourceSettings();

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

            if (string.IsNullOrWhiteSpace(Sources.UserAgent) ||
                Sources.UserAgent.Contains("example.com", StringComparison.OrdinalIgnoreCase))
                Sources.UserAgent = Melotag.Core.ProductInfo.UserAgent;

            SettingsVersion = 5;
        }

        if (SettingsVersion < 6)
        {
            // 内置默认 UA 跟随应用版本更新；用户自定义的 UA 保持原样。
            if (IsBuiltInUserAgent(Sources.UserAgent))
                Sources.UserAgent = Melotag.Core.ProductInfo.UserAgent;

            SettingsVersion = 6;
        }

        static bool IsBuiltInUserAgent(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.StartsWith("Melotag/", StringComparison.OrdinalIgnoreCase) &&
            value.EndsWith($"(+{Melotag.Core.ProductInfo.RepositoryUrl})", StringComparison.OrdinalIgnoreCase);

        static void MergeInto(List<string> list, string[] ids)
        {
            foreach (var id in ids)
                if (!list.Contains(id, StringComparer.OrdinalIgnoreCase))
                    list.Insert(0, id);
        }

        static void Sanitize(List<string> list)
        {
            list.RemoveAll(id => !SourceDefaults.All.Contains(id, StringComparer.OrdinalIgnoreCase));
        }
    }
}
