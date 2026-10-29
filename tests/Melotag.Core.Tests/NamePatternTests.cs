namespace Melotag.Core.Tests;

using Melotag.Core.Models;
using Melotag.Core.Rename;

public class NamePatternTests
{
    [Fact]
    public void Validate_Empty_ReturnsEmpty()
    {
        Assert.Contains(PatternError.Empty, NamePattern.Validate(""));
        Assert.Contains(PatternError.Empty, NamePattern.Validate(null));
    }

    [Fact]
    public void Validate_NoPlaceholder_ReturnsNoPlaceholder()
    {
        Assert.Contains(PatternError.NoPlaceholder, NamePattern.Validate("plain name"));
    }

    [Fact]
    public void Validate_OnlyUnused_ReturnsOnlyUnused()
    {
        Assert.Contains(PatternError.OnlyUnused, NamePattern.Validate("@0"));
    }

    [Fact]
    public void Validate_Duplicate_ReturnsDuplicate()
    {
        Assert.Contains(PatternError.DuplicatePlaceholder, NamePattern.Validate("@1 - @1"));
    }

    [Fact]
    public void Validate_Adjacent_AllowedForGenerate_RejectedForParse()
    {
        Assert.DoesNotContain(PatternError.AdjacentPlaceholders, NamePattern.Validate("@4@5. @1"));
        Assert.Contains(PatternError.AdjacentPlaceholders,
            NamePattern.Validate("@4@5. @1", PatternDirection.Parse));
    }

    [Fact]
    public void Validate_UnknownPlaceholder_IsReported()
    {
        Assert.Contains(PatternError.UnknownPlaceholder, NamePattern.Validate("@9"));
    }

    [Fact]
    public void Render_ReplacesKnownPlaceholders_AndFormatsTrackAsTwoDigits()
    {
        var tags = new TrackTags { Title = "Song", Artist = "Band", Track = 3 };
        Assert.Equal("03. Song - Band", NamePattern.Render("@5. @1 - @2", tags));
    }

    [Fact]
    public void Render_MissingValue_IsEmptyByDefault_OrKeepsPlaceholder()
    {
        var tags = new TrackTags { Title = "Song" };
        Assert.Equal("Song", NamePattern.Render("@1@3", tags));
        Assert.Equal("Song@3", NamePattern.Render("@1@3", tags, keepPlaceholderWhenEmpty: true));
    }

    [Fact]
    public void Presets_AreAllValidForGenerate()
    {
        foreach (var preset in RenamePresets.Defaults)
            Assert.Empty(NamePattern.Validate(preset));
    }
}
