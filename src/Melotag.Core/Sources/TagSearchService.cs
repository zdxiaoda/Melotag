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
        // sourceOrder 同时表示“启用的源”和顺序：用户取消勾选的源不应再查询。
        // 新增源通过 AppSettings.Migrate 并入旧配置，不能靠“缺省即启用”绕过用户的禁用选择。
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sourceOrder.Count; i++)
            ranks.TryAdd(sourceOrder[i], i);

        var selected = _sources
            .Where(s => (s.Capabilities & capability) != 0)
            .Where(s => s.IsConfigured)
            .Where(s => ranks.ContainsKey(s.Id))
            .OrderBy(s => ranks[s.Id])
            .ToList();

        var gate = new SemaphoreSlim(Math.Clamp(threads, 1, 8));
        var tasks = selected.Select(async source =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (source is TagSourceBase tagSourceEntry) tagSourceEntry.ResetError();
                return await source.SearchAsync(query, perSource, capability, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // 失败仍然返回空结果，但要让用户看到「这个源有问题」，而不是像「没有匹配」。
                // 源自己已经写过更具体的原因时保留它。
                if (source is TagSourceBase tagSource && string.IsNullOrWhiteSpace(tagSource.LastError))
                    tagSource.SetError("查询失败");
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
