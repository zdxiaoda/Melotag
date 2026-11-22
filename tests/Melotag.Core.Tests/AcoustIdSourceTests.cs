namespace Melotag.Core.Tests;

using Melotag.Core.Files;
using Melotag.Core.Models;
using Melotag.Core.Sources;

public class AcoustIdSourceTests
{
    private const string LookupJson = """
    {
      "status": "ok",
      "results": [
        {
          "id": "fp-1",
          "score": 0.98,
          "recordings": [
            {
              "id": "0e93bd8a-df0f-4b0e-bbb4-39c8e5a2f3e1",
              "title": "Get Lucky",
              "artists": [
                { "id": "a1", "name": "Daft Punk" },
                { "id": "a2", "name": "Pharrell Williams" }
              ],
              "releases": [
                { "id": "rel-1", "title": "Random Access Memories", "date": "2013-05-17" }
              ]
            },
            {
              "id": "0e93bd8a-df0f-4b0e-bbb4-39c8e5a2f3e1",
              "title": "Get Lucky (duplicate)"
            }
          ]
        },
        {
          "id": "fp-2",
          "score": 0.41,
          "recordings": []
        },
        {
          "id": "fp-3",
          "score": 0.9,
          "recordings": [
            { "id": "8f2c1d1a-1111-2222-3333-444455556666", "title": "Get Lucky (Radio Edit)", "releases": [] }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Parse_MapsRecordingsAndDeduplicates()
    {
        var candidates = AcoustIdSource.Parse(LookupJson, "acoustid", 10);

        Assert.Equal(2, candidates.Count);
        var first = candidates[0];
        Assert.Equal("acoustid", first.SourceId);
        Assert.Equal("Get Lucky", first.Title);
        Assert.Equal("Daft Punk & Pharrell Williams", first.Artist);
        Assert.Equal("Random Access Memories", first.Album);
        Assert.Equal((uint)2013, first.Year);
        Assert.Equal("0e93bd8a-df0f-4b0e-bbb4-39c8e5a2f3e1", first.ReleaseId);
        Assert.Equal("98%", first.Detail);

        var second = candidates[1];
        Assert.Equal("Get Lucky (Radio Edit)", second.Title);
        Assert.Equal("90%", second.Detail);
    }

    [Fact]
    public void Parse_NoRecordings_ReturnsEmpty()
    {
        var candidates = AcoustIdSource.Parse("""{"status": "ok", "results": [{"id": "fp", "score": 0.9}]}""",
            "acoustid", 10);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Parse_RespectsLimit()
    {
        var candidates = AcoustIdSource.Parse(LookupJson, "acoustid", 1);

        Assert.Single(candidates);
    }

    [Fact]
    public void Parse_SortsResultsByScore()
    {
        var json = """
        {
          "status": "ok",
          "results": [
            { "score": 0.41, "recordings": [{ "id": "low", "title": "Low" }] },
            { "score": 0.98, "recordings": [{ "id": "high", "title": "High" }] }
          ]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10);

        Assert.Equal("High", candidates[0].Title);
        Assert.Equal("Low", candidates[1].Title);
    }

    [Fact]
    public void TagQuery_From_CarriesFileHandle()
    {
        var path = Path.GetTempFileName();
        try
        {
            var source = new PhysicalFileHandle(path);
            var item = new TrackItem { Path = path, DisplayName = "a", Source = source };

            var query = TagQuery.From(item);

            Assert.Same(source, query.File);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
