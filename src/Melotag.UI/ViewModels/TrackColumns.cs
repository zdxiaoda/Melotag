namespace Melotag.UI.ViewModels;

/// <summary>
/// 列表列目录：DataGrid 列的 Tag、本地化键（Column.*）、可见性持久化与排序字段共用这一份 id。
/// </summary>
public static class TrackColumns
{
    public const string FileName = "FileName";
    public const string Title = "Title";
    public const string Artist = "Artist";
    public const string Album = "Album";
    public const string AlbumArtist = "AlbumArtist";
    public const string Year = "Year";
    public const string Track = "Track";
    public const string Disc = "Disc";
    public const string Genre = "Genre";
    public const string Duration = "Duration";
    public const string Size = "Size";
    public const string Format = "Format";
    public const string Bitrate = "Bitrate";
    public const string SampleRate = "SampleRate";
    public const string Channels = "Channels";
    public const string Modified = "Modified";
    public const string Created = "Created";
    public const string Status = "Status";

    /// <summary>全部列，顺序即 XAML 中的声明顺序。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        FileName, Title, Artist, Album, AlbumArtist, Year, Track, Disc, Genre,
        Duration, Size, Format, Bitrate, SampleRate, Channels, Modified, Created, Status,
    ];

    /// <summary>默认显示的列；其余列默认隐藏，可在表头右键菜单里打开。</summary>
    public static readonly IReadOnlyList<string> DefaultVisible =
    [
        FileName, Title, Artist, Album, Year, Track, Duration, Format, Status,
    ];

    public static bool IsKnown(string? id) => id is not null && All.Contains(id);

    public static bool TryParseSortField(string? id, out TrackSortField field)
    {
        field = default;
        return IsKnown(id) && Enum.TryParse(id!, ignoreCase: true, out field);
    }

    public static string LabelKey(string id) => $"Column.{id}";
}

/// <summary>列表可排序的字段；名称与 <see cref="TrackColumns"/> 的列 id 一致，便于直接从设置解析。</summary>
public enum TrackSortField
{
    FileName,
    Title,
    Artist,
    Album,
    AlbumArtist,
    Year,
    Track,
    Disc,
    Genre,
    Duration,
    Size,
    Format,
    Bitrate,
    SampleRate,
    Channels,
    Modified,
    Created,
    Status,
}

public sealed record SortOption(TrackSortField Field, string Label);

/// <summary>行列表排序；桌面与移动端共用。</summary>
public static class TrackSorter
{
    public static List<TrackRowViewModel> Sort(
        IEnumerable<TrackRowViewModel> rows,
        TrackSortField? field,
        bool descending)
    {
        if (field is null)
            return rows.ToList();

        var ordered = Order(rows, field.Value, descending)
            .ThenBy(row => row.FileName, StringComparer.CurrentCultureIgnoreCase);

        return ordered.ToList();
    }

    private static IOrderedEnumerable<TrackRowViewModel> Order(
        IEnumerable<TrackRowViewModel> rows,
        TrackSortField field,
        bool descending)
    {
        // 空值统一按最小值处理：升序在前、降序在后。
        static string Text(string? value) => value ?? "";
        static int Number(int? value) => value ?? 0;
        static uint UInt(uint? value) => value ?? 0;
        static DateTime Date(DateTime? value) => value ?? DateTime.MinValue;

        return field switch
        {
            TrackSortField.FileName => OrderText(rows, r => r.FileName, descending),
            TrackSortField.Title => OrderText(rows, r => Text(r.Title), descending),
            TrackSortField.Artist => OrderText(rows, r => Text(r.Artist), descending),
            TrackSortField.Album => OrderText(rows, r => Text(r.Album), descending),
            TrackSortField.AlbumArtist => OrderText(rows, r => Text(r.AlbumArtist), descending),
            TrackSortField.Genre => OrderText(rows, r => Text(r.Genre), descending),
            TrackSortField.Format => OrderText(rows, r => Text(r.FormatText), descending),
            TrackSortField.Year => OrderNumber(rows, r => UInt(r.YearNumber), descending),
            TrackSortField.Track => OrderNumber(rows, r => UInt(r.TrackNumber), descending),
            TrackSortField.Disc => OrderNumber(rows, r => UInt(r.DiscNumber), descending),
            TrackSortField.Duration => OrderNumber(rows, r => r.DurationSeconds, descending),
            TrackSortField.Size => OrderNumber(rows, r => r.SizeBytes, descending),
            TrackSortField.Bitrate => OrderNumber(rows, r => Number(r.BitrateKbps), descending),
            TrackSortField.SampleRate => OrderNumber(rows, r => Number(r.SampleRateHz), descending),
            TrackSortField.Channels => OrderNumber(rows, r => Number(r.Channels), descending),
            TrackSortField.Modified => OrderNumber(rows, r => Date(r.ModifiedUtc), descending),
            TrackSortField.Created => OrderNumber(rows, r => Date(r.CreatedUtc), descending),
            TrackSortField.Status => OrderNumber(rows, r => (int)r.Status, descending),
            _ => OrderText(rows, r => r.FileName, descending),
        };
    }

    private static IOrderedEnumerable<TrackRowViewModel> OrderText(
        IEnumerable<TrackRowViewModel> rows,
        Func<TrackRowViewModel, string> key,
        bool descending)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        return descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
    }

    private static IOrderedEnumerable<TrackRowViewModel> OrderNumber<T>(
        IEnumerable<TrackRowViewModel> rows,
        Func<TrackRowViewModel, T> key,
        bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
}
