namespace Melotag.Storage;

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;

/// <summary>标签历史/撤销。用 root_id + rel_path 作为稳定键，便于后续迁移到 Android SAF。</summary>
public sealed class SqliteHistoryStore : IHistoryStore
{
    /// <summary>非文件系统路径（Android content://）的伪 root，避免和真实盘符混淆。</summary>
    private const string UriRoot = "uri";

    private readonly string _connectionString;

    public SqliteHistoryStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(AppPaths.DataDirectory, "history.db");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // WAL 与 Shared 缓存组合不推荐，单进程桌面应用用 Private 即可
            Cache = SqliteCacheMode.Private,
        }.ToString();
    }

    public void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // journal_mode=WAL 只需（也只能可靠地）在这里设一次：它是持久化到库文件里的属性。
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS tag_history (
                serial     TEXT PRIMARY KEY,
                root_id    TEXT NOT NULL,
                rel_path   TEXT NOT NULL,
                size       INTEGER NOT NULL DEFAULT 0,
                mtime      INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                payload    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_history_path ON tag_history(root_id, rel_path);
            CREATE TABLE IF NOT EXISTS app_config (
                history_serial_prefix INTEGER NOT NULL
            );
            INSERT INTO app_config(history_serial_prefix)
            SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM app_config);
            DELETE FROM app_config WHERE rowid NOT IN (SELECT MIN(rowid) FROM app_config);
            """;
        cmd.ExecuteNonQuery();
    }

    public HistoryEntry AddSnapshot(string path, TrackTags tags)
    {
        var (root, rel, full) = Split(path);
        var created = DateTime.Now;
        var payload = JsonSerializer.Serialize(HistoryPayload.From(tags));
        var (size, mtime) = ReadFileStats(root, full);

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var counter = NextCounter(conn, tx);
        var serial = counter.ToString(CultureInfo.InvariantCulture) + "-" +
                     created.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO tag_history(serial, root_id, rel_path, size, mtime, created_at, payload)
            VALUES ($serial, $root, $rel, $size, $mtime, $created, $payload);
            """;
        insert.Parameters.AddWithValue("$serial", serial);
        insert.Parameters.AddWithValue("$root", root);
        insert.Parameters.AddWithValue("$rel", rel);
        insert.Parameters.AddWithValue("$size", size);
        insert.Parameters.AddWithValue("$mtime", mtime);
        insert.Parameters.AddWithValue("$created", created.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$payload", payload);
        insert.ExecuteNonQuery();

        tx.Commit();
        return new HistoryEntry(serial, full, created, tags.Clone());
    }

    public IReadOnlyList<HistoryEntry> GetHistory(string path)
    {
        var (root, rel, full) = Split(path);
        var list = new List<HistoryEntry>();

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // rowid 单调递增，比秒级/毫秒级字符串时间戳更稳定（同毫秒内的多条快照顺序不再随机）。
        cmd.CommandText = """
            SELECT serial, created_at, payload
            FROM tag_history
            WHERE root_id = $root AND rel_path = $rel
            ORDER BY rowid DESC;
            """;
        cmd.Parameters.AddWithValue("$root", root);
        cmd.Parameters.AddWithValue("$rel", rel);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var payload = JsonSerializer.Deserialize<HistoryPayload>(reader.GetString(2));
            list.Add(new HistoryEntry(
                reader.GetString(0),
                full,
                ParseTimestamp(reader.GetString(1)),
                payload?.ToTags() ?? new TrackTags()));
        }

        return list;
    }

    public HistoryEntry? Get(string serial)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT root_id, rel_path, created_at, payload
            FROM tag_history
            WHERE serial = $serial
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$serial", serial);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var root = reader.GetString(0);
        var rel = reader.GetString(1);
        var full = ComposePath(root, rel);
        var payload = JsonSerializer.Deserialize<HistoryPayload>(reader.GetString(3));

        return new HistoryEntry(serial, full, ParseTimestamp(reader.GetString(2)), payload?.ToTags() ?? new TrackTags());
    }

    public void ClearAll()
    {
        using (var conn = Open())
        using (var tx = conn.BeginTransaction())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                // 两条语句必须同事务，否则可能出现「历史已删、计数已重置」中间态
                cmd.CommandText = """
                    DELETE FROM tag_history;
                    UPDATE app_config SET history_serial_prefix = 1;
                    """;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        try
        {
            using var vacuum = Open();
            using var vacuumCmd = vacuum.CreateCommand();
            vacuumCmd.CommandText = "VACUUM;";
            vacuumCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // WAL 下页会被复用，压缩失败（库被占用等）不影响清空结果
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        // 每个连接都要设的项：等待锁 + 同步级别（WAL 下 NORMAL 已足够安全）
        pragma.CommandText = "PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static int NextCounter(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // 单条原子语句读旧值并自增，避免「先 SELECT 再 UPDATE」两个进程读到同一个计数
        cmd.CommandText = """
            UPDATE app_config
            SET history_serial_prefix = history_serial_prefix + 1
            RETURNING history_serial_prefix - 1;
            """;
        var value = cmd.ExecuteScalar();
        if (value is null or DBNull) return SeedCounter(conn, tx);

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>app_config 只有一行；极端情况下（旧库/被外部清空）补种一行并返回起始计数。</summary>
    private static int SeedCounter(SqliteConnection conn, SqliteTransaction tx)
    {
        using var seed = conn.CreateCommand();
        seed.Transaction = tx;
        seed.CommandText = "INSERT INTO app_config(history_serial_prefix) VALUES (1);";
        seed.ExecuteNonQuery();
        return 1;
    }

    private static (long Size, long Mtime) ReadFileStats(string root, string full)
    {
        // content:// 之类不是真实路径，FileInfo 会直接抛异常
        if (root == UriRoot) return (0, 0);

        var info = new FileInfo(full);
        if (!info.Exists) return (0, 0);

        return (info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds());
    }

    private static string ComposePath(string root, string rel) =>
        root == UriRoot ? rel : Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

    private static DateTime ParseTimestamp(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;

    internal static (string Root, string Relative, string Full) Split(string path)
    {
        // Android SAF 的 content:// 不是文件系统路径，Path.GetFullPath 会污染或直接抛异常
        if (path.Contains("://", StringComparison.Ordinal))
            return (UriRoot, path, path);

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var relative = full[root.Length..].Replace('\\', '/');
        return (root.ToUpperInvariant(), relative, full);
    }

    private sealed record HistoryPayload(
        string? Title, string? Artist, string? Album, string? AlbumArtist,
        uint? Year, uint? Track, uint? Disc, string? Genre,
        string? Comment, string? Composer, string? Lyricist,
        string? Lyrics)
    {
        public static HistoryPayload From(TrackTags t) => new(
            t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Track, t.Disc,
            t.Genre, t.Comment, t.Composer, t.Lyricist, t.Lyrics);

        public TrackTags ToTags() => new()
        {
            Title = Title,
            Artist = Artist,
            Album = Album,
            AlbumArtist = AlbumArtist,
            Year = Year,
            Track = Track,
            Disc = Disc,
            Genre = Genre,
            Comment = Comment,
            Composer = Composer,
            Lyricist = Lyricist,
            Lyrics = Lyrics,
        };
    }
}
