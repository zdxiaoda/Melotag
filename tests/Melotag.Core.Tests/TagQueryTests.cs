namespace Melotag.Core.Tests;

using Melotag.Core.Sources;

/// <summary>
/// 组合搜索词：未打标签的文件只有文件名可用，标题为空时必须回落，
/// 否则扣掉文件名会让「有文件名、没标签」的文件查不到任何结果。
/// </summary>
public class TagQueryTests
{
    [Fact]
    public void SearchText_EmptyTitle_FallsBackToFileName()
    {
        var query = new TagQuery { Title = null, FileName = "03 - Artist - Song" };

        Assert.Equal("03 - Artist - Song", query.SearchText(useFileName: false, useArtist: false, useAlbum: false));
    }

    [Fact]
    public void SearchText_WhitespaceTitle_FallsBackToFileName()
    {
        var query = new TagQuery { Title = "   ", FileName = "Artist - Song" };

        Assert.Equal("Artist - Song", query.SearchText(useFileName: false, useArtist: false, useAlbum: false));
    }

    [Fact]
    public void SearchText_UseFileName_PrefersFileNameOverTitle()
    {
        var query = new TagQuery { Title = "Lemon", FileName = "03 - 米津玄師 - Lemon" };

        Assert.Equal("03 - 米津玄師 - Lemon",
            query.SearchText(useFileName: true, useArtist: false, useAlbum: false));
    }

    [Fact]
    public void SearchText_Default_PrefersTitleOverFileName()
    {
        var query = new TagQuery { Title = "Lemon", FileName = "03 - junk" };

        Assert.Equal("Lemon", query.SearchText(useFileName: false, useArtist: false, useAlbum: false));
    }

    [Fact]
    public void SearchText_NoTitleAndNoFileName_StillUsesArtistAndAlbum()
    {
        var query = new TagQuery { Title = null, FileName = "", Artist = "米津玄師", Album = "Lemon" };

        Assert.Equal("米津玄師 Lemon", query.SearchText(useFileName: false, useArtist: true, useAlbum: true));
    }

    [Fact]
    public void SearchText_AllEmpty_ReturnsEmptyString()
    {
        var query = new TagQuery();

        Assert.Equal(string.Empty, query.SearchText(useFileName: true, useArtist: true, useAlbum: true));
        Assert.Equal(string.Empty, query.SearchText(useFileName: false, useArtist: true, useAlbum: true));
    }

    [Fact]
    public void SearchText_HonoursArtistAndAlbumFlags()
    {
        var query = new TagQuery { Title = "T", Artist = "A", Album = "B" };

        Assert.Equal("T", query.SearchText(useFileName: false, useArtist: false, useAlbum: false));
        Assert.Equal("T A", query.SearchText(useFileName: false, useArtist: true, useAlbum: false));
        Assert.Equal("T B", query.SearchText(useFileName: false, useArtist: false, useAlbum: true));
        Assert.Equal("T A B", query.SearchText(useFileName: false, useArtist: true, useAlbum: true));
    }

    [Fact]
    public void SearchText_TrimsPartsAndCollapsesSeparators()
    {
        var query = new TagQuery { Title = " Song ", Artist = " Artist ", Album = "  " };

        Assert.Equal("Song Artist", query.SearchText(useFileName: false, useArtist: true, useAlbum: true));
    }
}
