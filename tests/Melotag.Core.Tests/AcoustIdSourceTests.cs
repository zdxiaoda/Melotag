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
        // AcoustID 不直接给封面，release MBID 用于拼 Cover Art Archive 地址。
        Assert.Equal("https://coverartarchive.org/release/rel-1/front-500", first.CoverUrl);
        Assert.True((new AcoustIdSource(null).Capabilities & SourceCapability.Cover) != 0);

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

    /// <summary>
    /// AcoustID 实际返回的 <c>date</c> 是对象（year/month/day），旧实现按字符串读取会抛
    /// <see cref="InvalidOperationException"/>，被搜索服务吞掉后整次查询静默无结果。
    /// </summary>
    [Fact]
    public void Parse_ObjectDate_ReadsYear()
    {
        var json = """
        {
          "status": "ok",
          "results": [
            {
              "score": 0.99,
              "recordings": [
                {
                  "id": "rec-1",
                  "title": "Lemon",
                  "artists": [{ "id": "a1", "name": "米津玄師" }],
                  "releases": [
                    { "id": "rel-1", "title": "Lemon", "date": { "year": 2018, "month": 3, "day": 14 } }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Lemon", candidate.Title);
        Assert.Equal("米津玄師", candidate.Artist);
        Assert.Equal("Lemon", candidate.Album);
        Assert.Equal((uint)2018, candidate.Year);
    }

    [Fact]
    public void Parse_DateWithoutDay_ReadsYear()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.9,
            "recordings": [{
              "id": "rec-1",
              "title": "Love Story",
              "releases": [{ "title": "Fearless", "date": { "year": 2009 } }]
            }]
          }]
        }
        """;

        var candidate = Assert.Single(AcoustIdSource.Parse(json, "acoustid", 10));

        Assert.Equal((uint)2009, candidate.Year);
        Assert.Equal("Fearless", candidate.Album);
    }

    [Theory]
    [InlineData("""{ "title": "No Date" }""")]
    [InlineData("""{ "title": "Null Date", "date": null }""")]
    [InlineData("""{ "title": "Numeric Date", "date": 2018 }""")]
    [InlineData("""{ "title": "Bad String", "date": "unknown" }""")]
    public void Parse_UnusableDate_KeepsOtherFields(string releaseJson)
    {
        var json = $$"""
        {
          "status": "ok",
          "results": [{
            "score": 0.9,
            "recordings": [{
              "id": "rec-1",
              "title": "Weird",
              "releases": [{{releaseJson}}]
            }]
          }]
        }
        """;

        var candidate = Assert.Single(AcoustIdSource.Parse(json, "acoustid", 10));

        Assert.Equal("Weird", candidate.Title);
        Assert.Null(candidate.Year);
    }

    [Fact]
    public void Parse_MalformedNestedFields_DoesNotThrow()
    {
        var json = """
        {
          "status": "ok",
          "results": [
            {
              "score": 0.9,
              "recordings": [
                { "id": "rec-1", "title": "Ok", "artists": "not-an-array", "releases": {} }
              ]
            },
            { "score": 0.8, "recordings": null },
            { "score": 0.7, "recordings": [{ "id": 42, "title": null }] }
          ]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Ok", candidate.Title);
        Assert.Null(candidate.Artist);
        Assert.Null(candidate.Album);
    }

    /// <summary>
    /// AcoustID 会把同一指纹关联的多个版本（原曲、翻唱、同专辑其他曲目）挂在一条 result 下，
    /// 数组顺序不代表匹配度；时长差距明显的曲目要排到后面，否则批量自动匹配会写入错误曲目。
    /// </summary>
    [Fact]
    public void Parse_PrefersRecordingMatchingQueryDuration()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.9887,
            "recordings": [
              { "id": "rec-picture", "title": "Picture to Burn", "duration": 173.067 },
              { "id": "rec-cruel", "title": "Cruel Summer", "duration": 178.0 },
              { "id": "rec-lover", "title": "Lover", "duration": 221.0 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: 178);

        Assert.Equal("Cruel Summer", candidates[0].Title);
        Assert.Equal("Picture to Burn", candidates[1].Title);
        Assert.Equal("Lover", candidates[2].Title);
    }

    /// <summary>
    /// 同一录音的不同版本时长往往只差 1–2 秒（重录、变速母带），按秒数硬排序会打乱
    /// AcoustID 给出的可信顺序，所以容差内保持服务顺序。
    /// </summary>
    [Fact]
    public void Parse_DurationsWithinTolerance_KeepServerOrder()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.9999,
            "recordings": [
              { "id": "rec-official", "title": "Idol", "duration": 212.477 },
              { "id": "rec-cover", "title": "アイドル", "duration": 213.227 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: 213);

        Assert.Equal("Idol", candidates[0].Title);
        Assert.Equal("アイドル", candidates[1].Title);
    }

    [Fact]
    public void Parse_NoQueryDuration_KeepsServerOrder()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.9,
            "recordings": [
              { "id": "rec-1", "title": "First", "duration": 300 },
              { "id": "rec-2", "title": "Second", "duration": 100 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10);

        Assert.Equal("First", candidates[0].Title);
        Assert.Equal("Second", candidates[1].Title);
    }

    [Fact]
    public void Parse_RecordingWithoutDuration_SortsLast()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.9,
            "recordings": [
              { "id": "rec-none", "title": "Unknown Length" },
              { "id": "rec-match", "title": "Match", "duration": 200 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: 202);

        Assert.Equal("Match", candidates[0].Title);
        Assert.Equal("Unknown Length", candidates[1].Title);
    }

    /// <summary>
    /// AcoustID 对无效指纹返回 HTTP 400 + <c>status:"error"</c>，此时既不能当成「没有匹配」
    /// （用户会以为这首歌没收录），也不能把原始报文丢给用户。
    /// </summary>
    [Fact]
    public void Parse_ServiceError_ReturnsEmptyAndKeepsReason()
    {
        const string json = """
        {"status":"error","error":{"code":3,"message":"invalid fingerprint"}}
        """;

        Assert.Empty(AcoustIdSource.Parse(json, "acoustid", 10));

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: null, out var error);

        Assert.Empty(candidates);
        Assert.NotNull(error);
        Assert.Contains("invalid fingerprint", error);
        Assert.Contains("3", error);
    }

    [Fact]
    public void Parse_Success_ReportsNoError()
    {
        var candidates = AcoustIdSource.Parse(LookupJson, "acoustid", 10, queryDurationSeconds: null, out var error);

        Assert.NotEmpty(candidates);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("""{"results":[null]}""")]
    [InlineData("""{"results":{}}""")]
    [InlineData("""{"results":"nope"}""")]
    [InlineData("""{"results":[]}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""["not-an-object"]""")]
    [InlineData("\"plain string\"")]
    public void Parse_UnexpectedTopLevelShapes_ReturnEmptyWithoutThrowing(string json)
    {
        Assert.Empty(AcoustIdSource.Parse(json, "acoustid", 10));
        Assert.Empty(AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: 180, out _));
    }

    [Fact]
    public void Parse_MalformedArrayItems_AreSkippedWithoutThrowing()
    {
        var json = """
        {
          "status": "ok",
          "results": [
            { "score": 0.95, "recordings": ["x", 5, null] },
            {
              "score": 0.9,
              "recordings": [
                { "id": "rec-1", "title": "Ok", "artists": [null], "releases": [null] }
              ]
            }
          ]
        }
        """;

        var candidate = Assert.Single(AcoustIdSource.Parse(json, "acoustid", 10));

        Assert.Equal("Ok", candidate.Title);
        Assert.Null(candidate.Artist);
        Assert.Null(candidate.Album);
        Assert.Null(candidate.Year);
    }

    /// <summary>
    /// 时长容差 = max(3 秒, 2% 目标时长)，178 秒目标 → 约 3.56 秒：
    /// 差 3 秒仍视为同一录音（保持服务顺序），差 4 秒要排到后面。
    /// </summary>
    [Fact]
    public void Parse_DurationTolerance_IsAboutThreePointFiveSeconds()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.99,
            "recordings": [
              { "id": "rec-far", "title": "Far", "duration": 174.0 },
              { "id": "rec-near", "title": "Near", "duration": 181.0 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: 178);

        Assert.Equal("Near", candidates[0].Title);
        Assert.Equal("Far", candidates[1].Title);
    }

    [Fact]
    public void Parse_DurationWithinTolerance_AtBoundary_KeepsServerOrder()
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.99,
            "recordings": [
              { "id": "rec-a", "title": "A", "duration": 98.0 },
              { "id": "rec-b", "title": "B", "duration": 102.0 },
              { "id": "rec-c", "title": "C", "duration": 105.0 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: 100);

        Assert.Equal(new[] { "A", "B", "C" }, candidates.Select(c => c.Title).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Parse_UnusableQueryDuration_KeepsServerOrder(int duration)
    {
        var json = """
        {
          "status": "ok",
          "results": [{
            "score": 0.9,
            "recordings": [
              { "id": "rec-1", "title": "First", "duration": 300 },
              { "id": "rec-2", "title": "Second", "duration": 100 }
            ]
          }]
        }
        """;

        var candidates = AcoustIdSource.Parse(json, "acoustid", 10, queryDurationSeconds: duration);

        Assert.Equal("First", candidates[0].Title);
        Assert.Equal("Second", candidates[1].Title);
    }

    /// <summary>
    /// AcoustID 的 meta 必须用空格分隔。表单编码会把字面 '+' 转成 %2B，
    /// 服务端按空格拆分，写成 "recordings+releases" 会导致响应里没有 recordings。
    /// </summary>
    [Fact]
    public void LookupForm_UsesSpaceSeparatedMeta()
    {
        var form = AcoustIdSource.BuildLookupForm("AQAB", 261)
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("recordings releases", form["meta"]);
        Assert.DoesNotContain("+", form["meta"]);
        Assert.Equal("261", form["duration"]);
        Assert.Equal("AQAB", form["fingerprint"]);
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
