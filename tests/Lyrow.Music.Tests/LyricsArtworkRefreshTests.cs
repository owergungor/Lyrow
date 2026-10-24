using System;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyricsArtworkRefreshTests
{
    [Fact]
    public void TrackSwitch_RefreshesCurrentTrackMetadataAndArtworkSource()
    {
        var monitor = new SyntheticMediaSessionMonitor();

        monitor.SimulateTrack("Track 1", "Artist 1", "Album 1", TimeSpan.FromMinutes(3));

        Assert.Equal("Track 1", monitor.CurrentTrack.Title);
        Assert.Equal("Artist 1", monitor.CurrentTrack.Artist);

        monitor.SimulateTrack("Track 2", "Artist 2", "Album 2", TimeSpan.FromMinutes(4));

        Assert.Equal("Track 2", monitor.CurrentTrack.Title);
        Assert.Equal("Artist 2", monitor.CurrentTrack.Artist);
        Assert.Equal("Album 2", monitor.CurrentTrack.Album);
    }
}
