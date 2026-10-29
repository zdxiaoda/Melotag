namespace Melotag.Core.Sources;

public sealed class TagSearchService
{
    private readonly IReadOnlyList<ITagSource> _sources;

    public TagSearchService(IEnumerable<ITagSource> sources) => _sources = [.. sources];

    public IReadOnlyList<ITagSource> Sources => _sources;

    /// <summary>按能力过滤、按配置顺序并发查询；单源失败不影响其它源。</summary>
    public async Task<IReadOnlyList<TagCandidate>> SearchAsync(
        TagQuery query,
        SourceCapability capability,
        IReadOnlyList<string> sourceOrder,
        int perSource,
        int threads,
        CancellationToken ct)
    {
        var selected = _sources
            .Where(s => (s.Capabilities & capability) != 0)
            .Where(s => sourceOrder.Contains(s.Id))
            .OrderBy(s => sourceOrder.ToList().IndexOf(s.Id))
            .ToList();

        var gate = new SemaphoreSlim(Math.Clamp(threads, 1, 8));
        var tasks = selected.Select(async source =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await source.SearchAsync(query, perSource, capability, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return (IReadOnlyList<TagCandidate>)[];
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    public ITagSource? Find(string id) => _sources.FirstOrDefault(s => s.Id == id);
}
