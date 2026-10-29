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

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败不影响结论 */ }
    }
}
