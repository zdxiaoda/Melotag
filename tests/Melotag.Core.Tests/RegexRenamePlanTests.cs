namespace Melotag.Core.Tests;

using Melotag.Core.Models;
using Melotag.Core.Rename;

public class RegexRenamePlanTests
{
    [Fact]
    public void Map_ExtractsMappedGroups()
    {
        var plan = RegexRenamePlan.Create(@"^(\d+)\. (.+) - (.+)$", new Dictionary<int, FieldKey>
        {
            [1] = FieldKey.Track,
            [2] = FieldKey.Artist,
            [3] = FieldKey.Title,
        });

        var tags = plan.Map("03. xxartist - xxtitle");

        Assert.NotNull(tags);
        Assert.Equal(3u, tags!.Track);
        Assert.Equal("xxartist", tags.Artist);
        Assert.Equal("xxtitle", tags.Title);
        Assert.Null(tags.Album);
    }

    [Fact]
    public void Map_NoMatch_ReturnsNull()
    {
        var plan = RegexRenamePlan.Create(@"^(?<n>\d+)", new Dictionary<int, FieldKey> { [1] = FieldKey.Track });
        Assert.Null(plan.Map("abc"));
    }

    [Fact]
    public void Create_RejectsGroupZero()
    {
        Assert.Throws<ArgumentException>(() =>
            RegexRenamePlan.Create(@"^(\d+)", new Dictionary<int, FieldKey> { [0] = FieldKey.Track }));
    }

    [Fact]
    public void Create_RejectsMissingGroup()
    {
        Assert.Throws<ArgumentException>(() =>
            RegexRenamePlan.Create(@"^(\d+)", new Dictionary<int, FieldKey> { [2] = FieldKey.Track }));
    }

    [Fact]
    public void Create_RejectsEmptyPattern()
    {
        Assert.Throws<ArgumentException>(() =>
            RegexRenamePlan.Create("", new Dictionary<int, FieldKey> { [1] = FieldKey.Track }));
    }

    /// <summary>两个捕获组写同一个字段会互相覆盖，结果取决于字典顺序，必须直接拒绝。</summary>
    [Fact]
    public void Create_RejectsSameFieldMappedTwice()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RegexRenamePlan.Create(@"^(.+) - (.+)$", new Dictionary<int, FieldKey>
            {
                [1] = FieldKey.Title,
                [2] = FieldKey.Title,
            }));

        Assert.Contains("同一个字段", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void Create_RejectsInvalidGroupNumbers(int group)
    {
        Assert.Throws<ArgumentException>(() =>
            RegexRenamePlan.Create(@"^(.+) - (.+)$", new Dictionary<int, FieldKey> { [group] = FieldKey.Title }));
    }

    [Fact]
    public void Create_RejectsEmptyGroupMap()
    {
        Assert.Throws<ArgumentException>(() =>
            RegexRenamePlan.Create(@"^(.+)$", new Dictionary<int, FieldKey>()));
    }
}
