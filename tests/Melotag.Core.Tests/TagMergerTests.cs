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
    public void Merge_DoesNotMutateInputs()
    {
        var current = new TrackTags { Title = "A" };
        var candidate = new TrackTags { Title = "B" };

        _ = TagMerger.Merge(current, candidate, new OverwriteOptions { Default = OverwriteRule.Replace });

        Assert.Equal("A", current.Title);
        Assert.Equal("B", candidate.Title);
    }
}
