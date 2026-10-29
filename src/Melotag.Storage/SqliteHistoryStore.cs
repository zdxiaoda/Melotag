namespace Melotag.Storage;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Melotag.Core.Abstractions;
using Melotag.Core.Models;

/// <summary>标签历史/撤销。用 root_id + rel_path 作为稳定键，便于后续迁移到 Android SAF。</summary>
public sealed class SqliteHistoryStore : IHistoryStore
{
    private readonly string _connectionString;

    public SqliteHistoryStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(AppPaths.DataDirectory, "history.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
            """;
        cmd.ExecuteNonQuery();
    }

    public HistoryEntry AddSnapshot(string path, TrackTags tags)
    {
        var (root, rel, full) = Split(path);
        var info = new FileInfo(full);
        var created = DateTime.Now;
        var payload = JsonSerializer.Serialize(HistoryPayload.From(tags));

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var counter = NextCounter(conn, tx);
        var serial = $"{counter}-{created:yyyyMMddHHmmssfff}";

        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO tag_history(serial, root_id, rel_path, size, mtime, created_at, payload)
            VALUES ($serial, $root, $rel, $size, $mtime, $created, $payload);
            """;
        insert.Parameters.AddWithValue("$serial", serial);
        insert.Parameters.AddWithValue("$root", root);
        insert.Parameters.AddWithValue("$rel", rel);
        insert.Parameters.AddWithValue("$size", info.Exists ? info.Length : 0);
        insert.Parameters.AddWithValue("$mtime", info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds() : 0);
        insert.Parameters.AddWithValue("$created", created.ToString("O"));
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
        cmd.CommandText = """
            SELECT serial, created_at, payload
            FROM tag_history
            WHERE root_id = $root AND rel_path = $rel
            ORDER BY created_at DESC;
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
                DateTime.Parse(reader.GetString(1)),
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
        var rel = reader.GetString(1).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.Combine(root, rel);
        var payload = JsonSerializer.Deserialize<HistoryPayload>(reader.GetString(3));

        return new HistoryEntry(serial, full, DateTime.Parse(reader.GetString(2)), payload?.ToTags() ?? new TrackTags());
    }

    public void ClearAll()
    {
        using (var conn = Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM tag_history;
                UPDATE app_config SET history_serial_prefix = 1;
                """;
            cmd.ExecuteNonQuery();
        }

        using var vacuum = Open();
        using var vacuumCmd = vacuum.CreateCommand();
        vacuumCmd.CommandText = "VACUUM;";
        vacuumCmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static int NextCounter(SqliteConnection conn, SqliteTransaction tx)
    {
        using var read = conn.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT history_serial_prefix FROM app_config LIMIT 1;";
        var current = Convert.ToInt32(read.ExecuteScalar() ?? 1);

        using var update = conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE app_config SET history_serial_prefix = $next;";
        update.Parameters.AddWithValue("$next", current + 1);
        update.ExecuteNonQuery();

        return current;
    }

    internal static (string Root, string Relative, string Full) Split(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var relative = full[root.Length..].Replace('\\', '/');
        return (root.ToUpperInvariant(), relative, full);
    }

    private sealed record HistoryPayload(
        string? Title, string? Artist, string? Album, string? AlbumArtist,
        uint? Year, uint? Track, uint? Disc, string? Genre,
        string? Comment, string? Composer, string? Lyricist,
        string? Lyrics, int PictureCount)
    {
        public static HistoryPayload From(TrackTags t) => new(
            t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Track, t.Disc,
            t.Genre, t.Comment, t.Composer, t.Lyricist, t.Lyrics, t.Pictures.Count);

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
