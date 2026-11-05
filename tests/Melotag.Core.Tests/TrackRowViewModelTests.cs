namespace Melotag.Core.Tests;

using Melotag.Core.Models;
using Melotag.UI.ViewModels;

public class TrackRowViewModelTests
{
    [Fact]
    public void SpooledCover_RoundTripsWithoutRetainingPictureBytesInRow()
    {
        using var row = CreateRow("track.mp3");
        var data = new byte[] { 1, 2, 3, 4, 5 };

        Assert.True(row.SetCover(data, "image/jpeg", spoolToDisk: true));

        Assert.Empty(row.Pictures);
        Assert.True(row.PicturesDirty);
        Assert.True(row.HasCover);
        Assert.Equal(data, Assert.Single(row.LoadPictures()).Data);
        Assert.Equal(data, Assert.Single(row.ToTags().Pictures).Data);
    }

    [Fact]
    public void SpooledCover_FailureLeavesPreviousCoverUntouched()
    {
        using var row = CreateRow("track.mp3");
        var original = new byte[] { 1, 2, 3 };
        Assert.True(row.SetCover(original, "image/jpeg"));

        Assert.False(row.SetCover([], "image/png", spoolToDisk: true));

        Assert.Equal(original, Assert.Single(row.ToTags().Pictures).Data);
        Assert.Equal("image/jpeg", row.ToTags().Pictures[0].MimeType);
    }

    [Fact]
    public void ToTagsWithoutPictures_DoesNotConsumeSpooledCover()
    {
        using var row = CreateRow("track.mp3");
        var data = new byte[] { 5, 4, 3, 2, 1 };
        Assert.True(row.SetCover(data, "image/png", spoolToDisk: true));

        Assert.Empty(row.ToTags(includePictures: false).Pictures);

        Assert.True(row.PicturesDirty);
        Assert.Equal(data, Assert.Single(row.LoadPictures()).Data);
    }

    [Fact]
    public void ClearCover_RemovesSpooledPictureFromPendingTags()
    {
        using var row = CreateRow("track.mp3");
        Assert.True(row.SetCover([1, 2, 3], "image/jpeg", spoolToDisk: true));

        row.ClearCover();

        Assert.False(row.HasCover);
        Assert.True(row.PicturesDirty);
        Assert.Empty(row.LoadPictures());
        Assert.Empty(row.ToTags().Pictures);
    }

    [Fact]
    public void TrackSorter_UsesNumericTrackAndFileNameTieBreak()
    {
        using var ten = CreateRow("a.mp3", track: 10);
        using var twoB = CreateRow("b.mp3", track: 2);
        using var twoA = CreateRow("a.mp3", track: 2);
        var rows = new[] { ten, twoB, twoA };

        var ascending = TrackSorter.Sort(rows, TrackSortField.Track, descending: false);
        var descending = TrackSorter.Sort(rows, TrackSortField.Track, descending: true);

        Assert.Equal([twoA, twoB, ten], ascending);
        Assert.Equal([ten, twoA, twoB], descending);
    }

    [Fact]
    public void TrackSorter_NullFieldKeepsImportOrder()
    {
        using var first = CreateRow("z.mp3");
        using var second = CreateRow("a.mp3");
        var rows = new[] { first, second };

        Assert.Equal(rows, TrackSorter.Sort(rows, field: null, descending: false));
    }

    [Theory]
    [InlineData("Title", true)]
    [InlineData("Modified", true)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TrackColumns_ValidatesSortFieldIds(string? id, bool expected)
    {
        Assert.Equal(expected, TrackColumns.TryParseSortField(id, out _));
    }

    private static TrackRowViewModel CreateRow(string fileName, uint? track = null) => new(new TrackItem
    {
        Path = Path.Combine("music", fileName),
        DisplayName = fileName,
        Status = TrackStatus.Loaded,
        Tags = new TrackTags { Track = track },
    });
}
