using Vista.Core.Display;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Display;

public class PlaylistHeadingTests
{
    // Five tracks, each an entry of the one playlist in order; the second is "Hairpin".
    private static Scene FiveEntries()
    {
        var tracks = new[] { "Opening", "Hairpin", "Orbit", "Crane", "Close" }
            .Select(name => TrackEditing.Empty() with { Name = name })
            .ToArray();
        return PlaylistEditing.Add(OnePlaylist(tracks), tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void TheHeadingIsTheEntrysNumberThePlaylistsCountAndItsTracksName()
    {
        var scene = FiveEntries();

        Assert.Equal("2 / 5 — Hairpin", PlaylistHeading.NowPlaying(scene, Entries(scene)[1].Id));
    }

    [Fact]
    public void AnEntryThePlaylistDoesntHoldHasNoHeading()
    {
        Assert.Null(PlaylistHeading.NowPlaying(FiveEntries(), Guid.NewGuid()));
    }
}
