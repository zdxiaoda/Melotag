namespace Melotag.Core.Tests;

using Melotag.Storage;

public class AppSettingsTests
{
    [Fact]
    public void Migrate_InsertsAcoustIdAfterMusicBrainz()
    {
        var settings = new AppSettings();
        settings.Sources.Tags.Clear();
        settings.Sources.Tags.Add("netease");
        settings.Sources.Tags.Add("musicbrainz");
        settings.Sources.Tags.Add("itunes");
        settings.SettingsVersion = 6;

        settings.Migrate();

        var index = settings.Sources.Tags.IndexOf("acoustid");
        Assert.True(index > 0);
        Assert.Equal("musicbrainz", settings.Sources.Tags[index - 1]);
        Assert.Equal(7, settings.SettingsVersion);
    }
}
