using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;

namespace Vista.Tests.Tracks;

public class TrackTests
{
    // ShowsAnchor is a placed anchor on a track that doesn't follow a character.

    [Fact]
    public void APlacedAnchorShowsUnlessTheTrackFollowsACharacter()
    {
        Assert.True((TrackEditing.Empty() with { AnchorPlaced = true }).ShowsAnchor);
        Assert.False(TrackEditing.Empty().ShowsAnchor);
        Assert.False((TrackEditing.Empty(AimMode.FollowTarget) with { AnchorPlaced = true }).ShowsAnchor);
    }

    // UsesLookAt is a Look At aim with its point placed.

    [Fact]
    public void TheLookAtPointIsUsedOnlyPlacedAndAimedAt()
    {
        Assert.True((TrackEditing.Empty(AimMode.LookAt) with { LookAtPlaced = true }).UsesLookAt);
        Assert.False(TrackEditing.Empty(AimMode.LookAt).UsesLookAt);
        Assert.False((TrackEditing.Empty() with { LookAtPlaced = true }).UsesLookAt);
    }
}
