namespace Melotag.Tags;

using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using TL = TagLib;

/// <summary>TagLib# 后端。原程序的引擎就是 TagLib C++ 的改名分支，语义最接近。</summary>
public sealed class TagLibTagStore : ITagStore
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // .tta / .dff: TagLib# 2.3 未实现，实测 Create 直接抛 UnsupportedFormatException
        ".mp3", ".flac", ".ape", ".wv", ".mpc",
        ".ogg", ".opus", ".m4a", ".mp4", ".wav", ".aif", ".aiff",
        ".wma", ".dsf",
    };

    /// <summary>Id3v2 的默认版本是全局静态状态，多线程同时写会互相污染，必须串行化。</summary>
    private static readonly object Id3v2VersionGate = new();

    public bool CanHandle(string extension) => SupportedExtensions.Contains(extension);

    public TrackItem Read(IFileHandle file, bool includePictures = true)
    {
        using var tl = TL.File.Create(new HandleAbstraction(file));
        var tag = tl.Tag;
        var properties = tl.Properties;

        var tags = new TrackTags
        {
            Title = TrackTags.Normalize(tag.Title),
            Artist = TrackTags.Normalize(tag.FirstPerformer),
            Album = TrackTags.Normalize(tag.Album),
            AlbumArtist = TrackTags.Normalize(tag.FirstAlbumArtist),
            Year = tag.Year == 0 ? null : tag.Year,
            Track = tag.Track == 0 ? null : tag.Track,
            Disc = tag.Disc == 0 ? null : tag.Disc,
            Genre = TrackTags.Normalize(tag.FirstGenre),
            Comment = TrackTags.Normalize(tag.Comment),
            Composer = TrackTags.Normalize(tag.FirstComposer),
            // TagLib# 2.3 未暴露 Lyricist（ID3v2 TEXT / Vorbis LYRICIST 需后续按容器单独实现）
            Lyricist = null,
            Lyrics = TrackTags.Normalize(tag.Lyrics),
        };

        var pictures = tag.Pictures;
        if (includePictures)
            CopyPictures(pictures, tags.Pictures);

        return new TrackItem
        {
            Path = file.DisplayPath,
            DisplayName = file.FileName,
            HasCover = pictures.Any(p => p.Data is { Count: > 0 }),
            ModifiedUtc = file.LastWriteTimeUtc > DateTime.UnixEpoch ? file.LastWriteTimeUtc : null,
            CreatedUtc = file.CreationTimeUtc,
            Tags = tags,
            // 展示用容器格式（"FLAC"），不要用 MIME（taglib/flac 对用户没有意义）
            Info = new AudioInfo(
                Format: FormatName(file.Extension),
                Duration: properties?.Duration ?? TimeSpan.Zero,
                BitrateKbps: properties?.AudioBitrate is > 0 ? properties.AudioBitrate : null,
                SampleRateHz: properties?.AudioSampleRate is > 0 ? properties.AudioSampleRate : null,
                Channels: properties?.AudioChannels is > 0 ? properties.AudioChannels : null,
                SizeBytes: file.Length),
            Status = TrackStatus.Loaded,
            Source = file,
        };
    }

    public IReadOnlyList<TagPicture> ReadPictures(IFileHandle file)
    {
        using var tl = TL.File.Create(new HandleAbstraction(file));
        var result = new List<TagPicture>();
        CopyPictures(tl.Tag.Pictures, result);
        return result;
    }

    private static void CopyPictures(IEnumerable<TL.IPicture> pictures, List<TagPicture> target)
    {
        foreach (var picture in pictures)
        {
            if (picture.Data is null || picture.Data.Count == 0) continue;
            var mime = string.IsNullOrWhiteSpace(picture.MimeType)
                ? GuessMime(picture.Data.Data)
                : picture.MimeType;
            target.Add(new TagPicture(
                picture.Data.Data,
                mime,
                ToPictureKind(picture.Type),
                picture.Description));
        }
    }

    /// <summary>只有 FrontCover / BackCover 有意义，其余（Artist 等各种编号）统一归为 Other。</summary>
    private static PictureKind ToPictureKind(TL.PictureType type) => type switch
    {
        TL.PictureType.FrontCover => PictureKind.FrontCover,
        TL.PictureType.BackCover => PictureKind.BackCover,
        _ => PictureKind.Other,
    };

    public void Write(IFileHandle file, TrackTags tags, TagWriteOptions options)
    {
        var originalWriteTime = file.LastWriteTimeUtc;
        var isMp3 = file.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);

        if (isMp3) WriteId3v2(file, tags, options);
        else WriteCore(file, tags, options);

        if (options.KeepModifiedTime) file.SetLastWriteTimeUtc(originalWriteTime);
    }

    private static void WriteId3v2(IFileHandle file, TrackTags tags, TagWriteOptions options)
    {
        lock (Id3v2VersionGate)
        {
            var previousVersion = TL.Id3v2.Tag.DefaultVersion;
            var previousForce = TL.Id3v2.Tag.ForceDefaultVersion;
            try
            {
                // Id3v2Version 允许为 null（旧配置 / JSON 里显式 null），不能用 ! 或直接解引用
                TL.Id3v2.Tag.DefaultVersion =
                    options.Id3v2Version?.StartsWith("2.4", StringComparison.Ordinal) == true ? (byte)4 : (byte)3;
                TL.Id3v2.Tag.ForceDefaultVersion = true;

                WriteCore(file, tags, options);
            }
            finally
            {
                TL.Id3v2.Tag.DefaultVersion = previousVersion;
                TL.Id3v2.Tag.ForceDefaultVersion = previousForce;
            }
        }
    }

    private static void WriteCore(IFileHandle file, TrackTags tags, TagWriteOptions options)
    {
        using var tl = TL.File.Create(new HandleAbstraction(file));
        var tag = tl.Tag;

        tag.Title = tags.Title ?? string.Empty;
        tag.Album = tags.Album ?? string.Empty;
        tag.Comment = tags.Comment ?? string.Empty;

        var year = tags.Year ?? 0;
        if (tag.Year != year) tag.Year = year;

        tag.Track = tags.Track ?? 0;
        tag.Disc = tags.Disc ?? 0;

        // 差分写入：首值相同时保持磁盘上的多值数组不动，
        // 否则「只改标题」会把 "A; B" 这类多值字段压平成单值。
        ApplyMultiValue(tag.Genres, tags.Genre, value => tag.Genres = value);
        ApplyMultiValue(tag.Performers, tags.Artist, value => tag.Performers = value);
        ApplyMultiValue(tag.AlbumArtists, tags.AlbumArtist, value => tag.AlbumArtists = value);
        ApplyMultiValue(tag.Composers, tags.Composer, value => tag.Composers = value);

        if (options.WriteLyricsToTag)
            tag.Lyrics = tags.Lyrics ?? string.Empty;

        // Lyricist：TagLib# 2.3 未暴露该字段，按容器单独实现前不写（不假装支持）

        if (options.ReplacePictures)
        {
            tag.Pictures = tags.Pictures
                .Select(p => (TL.IPicture)new TL.Picture(new TL.ByteVector(p.Data))
                {
                    MimeType = p.MimeType,
                    Type = (TL.PictureType)(int)p.Kind,
                    Description = p.Description,
                })
                .ToArray();
        }

        tl.Save();
    }

    private static void ApplyMultiValue(string[]? current, string? incoming, Action<string[]> apply)
    {
        var existing = current ?? Array.Empty<string>();
        var next = string.IsNullOrWhiteSpace(incoming) ? Array.Empty<string>() : new[] { incoming };

        // 首值相同：磁盘上可能还有更多值，保持原数组，别压平
        if (existing.Length > 0 && next.Length > 0 && string.Equals(existing[0], next[0], StringComparison.Ordinal))
            return;

        // 磁盘上本来就空、也不打算写：什么都不做
        if (existing.Length == 0 && next.Length == 0) return;

        apply(next);
    }

    private static string? FormatName(string extension)
    {
        var name = extension.TrimStart('.');
        return name.Length == 0 ? null : name.ToUpperInvariant();
    }

    private static string GuessMime(byte[] data)
    {
        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8) return "image/jpeg";
        if (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        if (data.Length > 2 && data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
        if (data.Length > 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return "image/gif";
        if (data.Length > 12 &&
            data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return "image/webp";

        // 认不出来时不要谎报成 jpeg
        return "application/octet-stream";
    }

    private sealed class HandleAbstraction : TL.File.IFileAbstraction
    {
        private readonly IFileHandle _handle;

        public HandleAbstraction(IFileHandle handle) => _handle = handle;

        // TagLib 用 Name 推断扩展名 / 拼临时文件路径，必须是带扩展名的文件名，而不是展示路径
        public string Name => _handle.FileName;
        public Stream ReadStream => _handle.OpenRead();
        public Stream WriteStream => _handle.OpenReadWrite();
        public void CloseStream(Stream stream) => stream.Dispose();
    }
}
