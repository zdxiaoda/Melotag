namespace Melotag.Core.Tests;

using Melotag.Core.Services;

public class UpdateServiceTests
{
    private const string Json = """
    {
      "tag_name": "v1.2.0",
      "html_url": "https://github.com/zdxiaoda/Melotag/releases/tag/v1.2.0",
      "body": "## What's new\n- i18n",
      "draft": false,
      "prerelease": false
    }
    """;

    [Fact]
    public void Parse_ExtractsVersionUrlAndNotes()
    {
        var info = UpdateService.Parse(Json, "1.1.0");

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.Version);
        Assert.Equal("https://github.com/zdxiaoda/Melotag/releases/tag/v1.2.0", info.Url);
        Assert.Contains("i18n", info.Notes);
        Assert.True(info.IsNewer);
    }

    [Fact]
    public void Parse_SameVersion_IsNotNewer()
    {
        var info = UpdateService.Parse(Json, "1.2.0");

        Assert.NotNull(info);
        Assert.False(info!.IsNewer);
    }

    [Fact]
    public void Parse_DraftOrPrerelease_IsIgnored()
    {
        Assert.Null(UpdateService.Parse(Json.Replace("\"draft\": false", "\"draft\": true"), "1.1.0"));
        Assert.Null(UpdateService.Parse(Json.Replace("\"prerelease\": false", "\"prerelease\": true"), "1.1.0"));
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.Null(UpdateService.Parse("not json", "1.1.0"));
        Assert.Null(UpdateService.Parse("[]", "1.1.0"));
    }

    [Fact]
    public void CompareVersion_HandlesVPrefixAndSuffix()
    {
        Assert.True(UpdateService.CompareVersion("v1.2.0", "1.1.0") > 0);
        Assert.Equal(0, UpdateService.CompareVersion("1.1.0", "v1.1.0"));
        Assert.True(UpdateService.CompareVersion("1.2.0-beta", "1.2.0") == 0);
    }
}
