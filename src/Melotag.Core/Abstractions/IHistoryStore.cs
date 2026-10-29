using Melotag.Core.Models;

namespace Melotag.Core.Abstractions;

public sealed record HistoryEntry(
    string Serial,
    string Path,
    DateTime CreatedAt,
    TrackTags Tags);

public interface IHistoryStore
{
    void Initialize();
    HistoryEntry AddSnapshot(string path, TrackTags tags);
    IReadOnlyList<HistoryEntry> GetHistory(string path);
    HistoryEntry? Get(string serial);
    void ClearAll();
}
