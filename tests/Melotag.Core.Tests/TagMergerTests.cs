namespace Melotag.Core.Tests;

using Melotag.Core.Merge;
using Melotag.Core.Models;

public class TagMergerTests
{
    [Fact]
    public void FillEmptyOnly_KeepsExisting_AndFillsBlank()
    {
        var current = new TrackTags { Title = "A", Artist = null };
        var candidate = new TrackTags { Title = "B", Artist = "C" };

        var result = TagMerger.Merge(current, candidate, new OverwriteOptions());

        Assert.Equal("A", result.Title);
        Assert.Equal("C", result.Artist);
    }

    [Fact]
    public void Replace_OverwritesEverythingPresentInCandidate()
    {
        var current = new TrackTags { Title = "A", Album = "Keep" };
        var candidate = new TrackTags { Title = "B" };

        var result = TagMerger.Merge(current, candidate, new OverwriteOptions { Default = OverwriteRule.Replace });

        Assert.Equal("B", result.Title);
        Assert.Equal("Keep", result.Album);
    }

    [Fact]
    public void KeepExisting_OnlyFillsWhenCurrentIsNull()
    {
        var current = new TrackTags { Title = null, Artist = "A" };
        var candidate = new TrackTags { Title = "B", Artist = "C" };

        var result = TagMerger.Merge(current, candidate, new OverwriteOptions { Default = OverwriteRule.KeepExisting });

        Assert.Equal("B", result.Title);
        Assert.Equal("A", result.Artist);
    }

    [Fact]
    public void Numbers_TreatZeroAsEmpty()
    {
        var current = new TrackTags { Year = 0 };
        var candidate = new TrackTags { Year = 1999 };

        var result = TagMerger.Merge(current, candidate, new OverwriteOptions());

        Assert.Equal(1999u, result.Year);
    }

    [Fact]
    public void PerFieldRule_OverridesDefault()
    {
        var options = new OverwriteOptions { Default = OverwriteRule.FillEmptyOnly };
        options.PerField[FieldKey.Comment] = OverwriteRule.Replace;

        var result = TagMerger.Merge(
            new TrackTags { Comment = "old" },
            new TrackTags { Comment = "new" },
            options);

        Assert.Equal("new", result.Comment);
    }

    [Fact]
    public void Ignore_LeavesFieldUntouched_EvenWhenCurrentIsNull()
    {
        var options = new OverwriteOptions { Default = OverwriteRule.Replace };
        options.PerField[FieldKey.Artist] = OverwriteRule.Ignore;

        var result = TagMerger.Merge(
            new TrackTags { Title = "old", Artist = null, Year = null },
            new TrackTags { Title = "new", Artist = "candidate", Year = 2024 },
            options);

        Assert.Equal("new", result.Title);
        Assert.Null(result.Artist);
        Assert.Equal(2024u, result.Year);
    }

    [Fact]
    public void Merge_DoesNotMutateInputs()
    {
        var current = new TrackTags { Title = "A" };
        var candidate = new TrackTags { Title = "B" };

        _ = TagMerger.Merge(current, candidate, new OverwriteOptions { Default = OverwriteRule.Replace });

        Assert.Equal("A", current.Title);
        Assert.Equal("B", candidate.Title);
    }

    private static readonly byte[] CurrentCover = [0xFF, 0xD8, 0x11];
    private static readonly byte[] CandidateCover = [0xFF, 0xD8, 0x22];

    private static TrackTags WithCurrentCover() => new()
    {
        Title = "T",
        Pictures = [new TagPicture(CurrentCover, "image/jpeg")],
    };

    private static TrackTags CandidateWithCover() => new()
    {
        Pictures = [new TagPicture(CandidateCover, "image/jpeg")],
    };

    /// <summary>
    /// 封面只看 <see cref="OverwriteOptions.PicturesRule"/>：KeepExisting/FillEmptyOnly 只在当前无封面时补，
    /// Replace 总是替换，Ignore 永不动手。
    /// </summary>
    [Theory]
    [InlineData(OverwriteRule.KeepExisting, /* hasCurrent */ true, /* expectCandidate */ false)]
    [InlineData(OverwriteRule.KeepExisting, false, true)]
    [InlineData(OverwriteRule.FillEmptyOnly, true, false)]
    [InlineData(OverwriteRule.FillEmptyOnly, false, true)]
    [InlineData(OverwriteRule.Replace, true, true)]
    [InlineData(OverwriteRule.Replace, false, true)]
    [InlineData(OverwriteRule.Ignore, true, false)]
    [InlineData(OverwriteRule.Ignore, false, false)]
    public void PicturesRule_Matrix(OverwriteRule rule, bool hasCurrentCover, bool expectCandidate)
    {
        var current = hasCurrentCover ? WithCurrentCover() : new TrackTags { Title = "T" };
        var options = new OverwriteOptions { PicturesRule = rule };

        var result = TagMerger.Merge(current, CandidateWithCover(), options);

        if (expectCandidate)
        {
            var picture = Assert.Single(result.Pictures);
            Assert.Equal(CandidateCover, picture.Data);
        }
        else if (hasCurrentCover)
        {
            var picture = Assert.Single(result.Pictures);
            Assert.Equal(CurrentCover, picture.Data);
        }
        else
        {
            Assert.Empty(result.Pictures);
        }
    }

    [Theory]
    [InlineData(OverwriteRule.KeepExisting)]
    [InlineData(OverwriteRule.FillEmptyOnly)]
    [InlineData(OverwriteRule.Replace)]
    [InlineData(OverwriteRule.Ignore)]
    public void CandidateWithoutCover_KeepsCurrentCover(OverwriteRule rule)
    {
        var options = new OverwriteOptions { PicturesRule = rule };

        var result = TagMerger.Merge(WithCurrentCover(), new TrackTags { Title = "new" }, options);

        var picture = Assert.Single(result.Pictures);
        Assert.Equal(CurrentCover, picture.Data);
    }

    /// <summary>
    /// 封面不在 <see cref="FieldKey"/> 里：默认规则与逐字段规则（Title 等）都不应再影响封面，
    /// 否则 UI 上「不覆盖封面」的勾选会因为别的字段被改而失效。
    /// </summary>
    [Fact]
    public void DefaultAndFieldRules_DoNotAffectPictures()
    {
        var options = new OverwriteOptions
        {
            Default = OverwriteRule.Replace,
            PicturesRule = OverwriteRule.FillEmptyOnly,
        };
        options.PerField[FieldKey.Title] = OverwriteRule.Replace;

        var result = TagMerger.Merge(WithCurrentCover(), CandidateWithCover(), options);

        Assert.Equal(CurrentCover, Assert.Single(result.Pictures).Data);
    }

    [Fact]
    public void DefaultIgnore_DoesNotBlockPicturesReplace()
    {
        var options = new OverwriteOptions
        {
            Default = OverwriteRule.Ignore,
            PicturesRule = OverwriteRule.Replace,
        };

        var result = TagMerger.Merge(WithCurrentCover(), CandidateWithCover(), options);

        Assert.Equal(CandidateCover, Assert.Single(result.Pictures).Data);
    }

    [Fact]
    public void PicturesRule_IsNotDerivedFromFieldKey()
    {
        // FieldKey 里没有 Pictures：枚举值本身不应包含封面相关项，防止 UI 勾选列表多出不可编辑项。
        Assert.DoesNotContain("Picture", string.Join(",", Enum.GetNames<FieldKey>()), StringComparison.OrdinalIgnoreCase);
    }
}
