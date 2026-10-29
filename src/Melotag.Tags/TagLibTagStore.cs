namespace Melotag.Tags;

using Melotag.Core.Abstractions;
using Melotag.Core.Models;
using TL = TagLib;

/// <summary>TagLib# 后端。原程序的引擎就是 TagLib C++ 的改名分支，语义最接近。</summary>
public sealed class TagLibTagStore : ITagStore
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ape", ".wv", ".tta", ".mpc",
        ".ogg", ".opus", ".m4a", ".mp4", ".wav", ".aif", ".aiff",
        ".wma", ".dsf", ".dff",
    };

    public bool CanHandle(string extension) => SupportedExtensions.Contains(extension);

    public TrackItem Read(IFileHandle file)
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

        foreach (var picture in tag.Pictures)
        {
            if (picture.Data is null || picture.Data.Count == 0) continue;
            tags.Pictures.Add(new TagPicture(
                picture.Data.Data,
                picture.MimeType ?? GuessMime(picture.Data.Data),
                (PictureKind)(int)picture.Type,
                picture.Description));
        }

        return new TrackItem
        {
            Path = file.DisplayPath,
            DisplayName = file.FileName,
            Tags = tags,
            Info = new AudioInfo(
                Format: tl.MimeType,
                Duration: properties?.Duration ?? TimeSpan.Zero,
                BitrateKbps: properties?.AudioBitrate is > 0 ? properties.AudioBitrate : null,
                SampleRateHz: properties?.AudioSampleRate is > 0 ? properties.AudioSampleRate : null,
                Channels: properties?.AudioChannels is > 0 ? properties.AudioChannels : null,
                SizeBytes: file.Length),
            Status = TrackStatus.Loaded,
            Source = file,
        };
    }

    public void Write(IFileHandle file, TrackTags tags, TagWriteOptions options)
    {
        var originalWriteTime = file.LastWriteTimeUtc;
        var isMp3 = file.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);

        if (isMp3)
        {
            TL.Id3v2.Tag.DefaultVersion = options.Id3v2Version.StartsWith("2.4", StringComparison.Ordinal) ? (byte)4 : (byte)3;
            TL.Id3v2.Tag.ForceDefaultVersion = true;
        }

        using (var tl = TL.File.Create(new HandleAbstraction(file)))
        {
            var tag = tl.Tag;

            tag.Title = tags.Title ?? string.Empty;
            tag.Album = tags.Album ?? string.Empty;
            tag.Comment = tags.Comment ?? string.Empty;
            tag.Year = tags.Year ?? 0;
            tag.Track = tags.Track ?? 0;
            tag.Disc = tags.Disc ?? 0;
            tag.Genres = string.IsNullOrWhiteSpace(tags.Genre) ? [] : [tags.Genre];
            tag.Performers = string.IsNullOrWhiteSpace(tags.Artist) ? [] : [tags.Artist];
            tag.AlbumArtists = string.IsNullOrWhiteSpace(tags.AlbumArtist) ? [] : [tags.AlbumArtist];
            tag.Composers = string.IsNullOrWhiteSpace(tags.Composer) ? [] : [tags.Composer];

            if (options.WriteLyricsToTag)
                tag.Lyrics = tags.Lyrics ?? string.Empty;

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

        if (options.KeepModifiedTime)
            file.SetLastWriteTimeUtc(originalWriteTime);
    }

    private static string GuessMime(byte[] data)
    {
        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8) return "image/jpeg";
        if (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        if (data.Length > 2 && data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
        if (data.Length > 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return "image/gif";
        return "image/jpeg";
    }

    private sealed class HandleAbstraction : TL.File.IFileAbstraction
    {
        private readonly IFileHandle _handle;

        public HandleAbstraction(IFileHandle handle) => _handle = handle;

        public string Name => _handle.DisplayPath;
        public Stream ReadStream => _handle.OpenRead();
        public Stream WriteStream => _handle.OpenReadWrite();
        public void CloseStream(Stream stream) => stream.Dispose();
    }
}
