namespace Melotag.Core.Tests;

using Melotag.Core.Sources;

public class TagSearchServiceTests
{
    [Fact]
    public void LastFm_WithoutApiKey_IsNotConfigured()
    {
        Assert.False(new LastFmSource(() => "").IsConfigured);
        Assert.False(new LastFmSource(() => "   ").IsConfigured);
        Assert.True(new LastFmSource(() => "key").IsConfigured);
    }

    [Fact]
    public async Task Search_SkipsSourcesThatAreNotConfigured()
    {
        var unconfigured = new FakeSource("off", isConfigured: false);
        var configured = new FakeSource("on", isConfigured: true);
        var service = new TagSearchService([unconfigured, configured]);

        var results = await service.SearchAsync(
            new TagQuery { Title = "hello" },
            SourceCapability.Tags,
            ["off", "on"],
            perSource: 10,
            threads: 1,
            CancellationToken.None);

        Assert.Equal(["on"], results.Select(c => c.SourceId));
        Assert.Equal(0, unconfigured.Calls);
        Assert.Equal(1, configured.Calls);
    }

    private sealed class FakeSource(string id, bool isConfigured) : TagSourceBase
    {
        public int Calls { get; private set; }

        public override string Id => id;
        public override string DisplayName => id;
        public override SourceCapability Capabilities => SourceCapability.Tags;
        public override bool IsConfigured => isConfigured;

        public override Task<IReadOnlyList<TagCandidate>> SearchAsync(
            TagQuery query, int limit, SourceCapability capability, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<TagCandidate>>(
                [new TagCandidate(Id, $"{Id} title")]);
        }
    }
}
