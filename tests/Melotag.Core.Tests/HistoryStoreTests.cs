namespace Melotag.Core.Tests;

using Melotag.Core.Models;
using Melotag.Storage;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;

    public HistoryStoreTests()
    {
        _dir = Directory.CreateTempSubdirectory("musictag-history-").FullName;
        _db = Path.Combine(_dir, "history.db");
    }

    [Fact]
    public void AddSnapshot_ThenGetHistory_ReturnsNewestFirst()
    {
        var store = new SqliteHistoryStore(_db);
        store.Initialize();

        var file = Path.Combine(_dir, "a.mp3");
        File.WriteAllBytes(file, [1, 2, 3]);

        var first = store.AddSnapshot(file, new TrackTags { Title = "one" });
        Thread.Sleep(5);
        var second = store.AddSnapshot(file, new TrackTags { Title = "two" });

        var history = store.GetHistory(file);

        Assert.Equal(2, history.Count);
        Assert.Equal(second.Serial, history[0].Serial);
        Assert.Equal("two", history[0].Tags.Title);
        Assert.Equal("one", history[1].Tags.Title);
        Assert.NotEqual(first.Serial, second.Serial);
    }

    [Fact]
    public void ClearAll_RemovesRows()
    {
        var store = new SqliteHistoryStore(_db);
        store.Initialize();

        var file = Path.Combine(_dir, "b.mp3");
        File.WriteAllBytes(file, [1]);
        store.AddSnapshot(file, new TrackTags { Title = "x" });

        store.ClearAll();

        Assert.Empty(store.GetHistory(file));
    }

    [Fact]
    public void GetHistory_UnknownFile_IsEmpty()
    {
        var store = new SqliteHistoryStore(_db);
        store.Initialize();

        Assert.Empty(store.GetHistory(Path.Combine(_dir, "missing.mp3")));
    }

    /// <summary>
    /// Android 的 content:// 不是文件系统路径：Path.GetFullPath 会污染它（或直接抛异常），
    /// 历史记录必须以 uri 伪 root + 原串存储，取回时原样返回。
    /// </summary>
    [Fact]
    public void Split_ContentUri_KeepsOriginalString()
    {
        const string uri = "content://media/external/audio/1";

        var (root, relative, full) = SqliteHistoryStore.Split(uri);

        Assert.Equal("uri", root);
        Assert.Equal(uri, relative);
        Assert.Equal(uri, full);
    }

    [Fact]
    public void Split_RealPath_UsesDriveRootAndRelativePath()
    {
        var path = Path.Combine(_dir, "song.mp3");

        var (root, relative, full) = SqliteHistoryStore.Split(path);

        Assert.NotEqual("uri", root);
        Assert.Equal(Path.GetFullPath(path), full);
        Assert.StartsWith(root, full, StringComparison.Ordinal);
        Assert.EndsWith("song.mp3", relative, StringComparison.Ordinal);
        Assert.False(relative.StartsWith('/'), $"相对路径不应以分隔符开头：{relative}");
    }

    [Fact]
    public void AddSnapshot_ContentUri_PathSurvivesRoundTrip()
    {
        const string uri = "content://media/external/audio/1";
        var store = new SqliteHistoryStore(_db);
        store.Initialize();

        var entry = store.AddSnapshot(uri, new TrackTags { Title = "sa" });

        Assert.Equal(uri, entry.Path);
        Assert.Equal(uri, store.Get(entry.Serial)!.Path);

        var history = store.GetHistory(uri);
        var reloaded = Assert.Single(history);
        Assert.Equal(uri, reloaded.Path);
        Assert.Equal("sa", reloaded.Tags.Title);
    }

    /// <summary>同一毫秒内连续两次快照：serial 里带计数器前缀，主键不能撞。</summary>
    [Fact]
    public void AddSnapshot_TwiceInSameMillisecond_ProducesDistinctSerials()
    {
        const string uri = "content://media/external/audio/2";
        var store = new SqliteHistoryStore(_db);
        store.Initialize();

        var first = store.AddSnapshot(uri, new TrackTags { Title = "one" });
        var second = store.AddSnapshot(uri, new TrackTags { Title = "two" });

        Assert.NotEqual(first.Serial, second.Serial);
        Assert.Equal(2, store.GetHistory(uri).Count);
        Assert.NotNull(store.Get(first.Serial));
        Assert.NotNull(store.Get(second.Serial));
    }

    [Fact]
    public void ClearAll_RestartsSerialCounter()
    {
        var store = new SqliteHistoryStore(_db);
        store.Initialize();
        var file = Path.Combine(_dir, "c.mp3");
        File.WriteAllBytes(file, [1]);

        var before = store.AddSnapshot(file, new TrackTags());
        Assert.StartsWith("1-", before.Serial);

        store.ClearAll();

        // 计数器回到 1（serial 前缀），旧记录不再可读
        Assert.Null(store.Get(before.Serial));
        Assert.Empty(store.GetHistory(file));

        var after = store.AddSnapshot(file, new TrackTags());
        Assert.StartsWith("1-", after.Serial);

        var next = store.AddSnapshot(file, new TrackTags());
        Assert.StartsWith("2-", next.Serial);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败不影响结论 */ }
    }
}
