using System.Numerics;
using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class ClickSelectionTests
{
    private static readonly Vector2 At = new(100f, 100f);
    private static readonly (float Yaw, float Pitch) Level = (0f, 0f);

    private static ClickOutcome Click(
        ClickSelection clicks,
        int? marker,
        bool overUi = false,
        bool overGizmo = false,
        Vector2? releaseAt = null
    )
    {
        Assert.Equal(ClickKind.None, clicks.Update(true, At, Level, overUi, overGizmo, marker).Kind);
        return clicks.Update(false, releaseAt ?? At, Level, false, false, null);
    }

    [Fact]
    public void AClickOnAMarkerSelectsIt() =>
        Assert.Equal(new ClickOutcome(ClickKind.Select, 2), Click(new ClickSelection(), 2));

    [Fact]
    public void AClickOnEmptySpaceDeselects() =>
        Assert.Equal(ClickKind.Deselect, Click(new ClickSelection(), null).Kind);

    [Fact]
    public void ADragIsNotAClick()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, Level, false, false, 1);
        clicks.Update(true, At + new Vector2(ClickSelection.MaxTravel + 1f, 0f), Level, false, false, null);
        Assert.Equal(ClickKind.None, clicks.Update(false, At, Level, false, false, null).Kind);
    }

    [Fact]
    public void AWobbleUnderTheLimitIsStillAClick()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, Level, false, false, 1);
        clicks.Update(true, At + new Vector2(ClickSelection.MaxTravel - 1f, 0f), Level, false, false, null);
        Assert.Equal(ClickKind.Select, clicks.Update(false, At, Level, false, false, null).Kind);
    }

    [Fact]
    public void PressesOverAPluginWindowOrTheGizmoAreIgnored()
    {
        Assert.Equal(ClickKind.None, Click(new ClickSelection(), null, overUi: true).Kind);
        Assert.Equal(ClickKind.None, Click(new ClickSelection(), 1, overGizmo: true).Kind);
    }

    [Fact]
    public void HoldingMarkerIsTrueOnlyWhileAPressOnAMarkerIsHeld()
    {
        var clicks = new ClickSelection();
        Assert.False(clicks.HoldingMarker);
        clicks.Update(true, At, Level, false, false, 0);
        Assert.True(clicks.HoldingMarker);
        clicks.Update(false, At, Level, false, false, null);
        Assert.False(clicks.HoldingMarker);
    }

    [Fact]
    public void ResetDropsAPressInProgress()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, Level, false, false, 0);
        clicks.Reset();
        Assert.Equal(ClickKind.None, clicks.Update(false, At, Level, false, false, null).Kind);
    }

    [Fact]
    public void NothingHappensWhileTheButtonStaysUp() =>
        Assert.Equal(ClickKind.None, new ClickSelection().Update(false, At, Level, false, false, 3).Kind);

    [Fact]
    public void TurningTheCameraWithTheCursorStillIsADragNotAClick()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, Level, false, false, null);
        clicks.Update(true, At, (ClickSelection.MaxTurn * 2f, 0f), false, false, null);
        Assert.Equal(
            ClickKind.None,
            clicks.Update(false, At, (ClickSelection.MaxTurn * 2f, 0f), false, false, null).Kind
        );
    }

    [Fact]
    public void ATurnUnderTheLimitIsStillAClick()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, Level, false, false, null);
        clicks.Update(true, At, (0f, ClickSelection.MaxTurn / 2f), false, false, null);
        Assert.Equal(ClickKind.Deselect, clicks.Update(false, At, Level, false, false, null).Kind);
    }

    [Fact]
    public void AYawWrappingPastAHalfTurnIsMeasuredTheShortWay()
    {
        var clicks = new ClickSelection();
        clicks.Update(true, At, (MathF.PI - 0.0001f, 0f), false, false, null);
        clicks.Update(true, At, (-MathF.PI + 0.0001f, 0f), false, false, null);
        Assert.Equal(ClickKind.Deselect, clicks.Update(false, At, Level, false, false, null).Kind);
    }
}
