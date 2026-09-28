using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Input;
using Vista.Core.Session;
using Xunit;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Input;

public class HotkeyResolverTests
{
    private static readonly HotkeyContext Edit = new(
        CameraMode.Editing,
        OverlayShown: true,
        PointsSelected: false,
        GizmoTarget: false
    );
    private static readonly HotkeyContext Live = new(
        CameraMode.Live,
        OverlayShown: false,
        PointsSelected: false,
        GizmoTarget: false
    );
    private static readonly HotkeyContext View = new(
        CameraMode.View,
        OverlayShown: true,
        PointsSelected: false,
        GizmoTarget: false
    );
    private static readonly HotkeyContext Off = new(
        CameraMode.Off,
        OverlayShown: false,
        PointsSelected: false,
        GizmoTarget: false
    );

    [Fact]
    public void SpacePlaysAndIsHiddenInEditAndLive()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.Play, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Space, Modifiers.None, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.Play, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Space, Modifiers.None, Live)
        );
    }

    [Fact]
    public void SpaceIsTheGamesInOffAndView()
    {
        Assert.Null(HotkeyResolver.Resolve(Key.Space, Modifiers.None, Off));
        Assert.Null(HotkeyResolver.Resolve(Key.Space, Modifiers.None, View));
    }

    [Fact]
    public void ShiftNeverStopsAPress()
    {
        Assert.Equal(HotkeyTable.Play, HotkeyResolver.Resolve(Key.Space, Modifiers.Shift, Edit)?.Entry);
        Assert.Equal(
            HotkeyTable.Restart,
            HotkeyResolver.Resolve(Key.Space, Modifiers.Ctrl | Modifiers.Shift, Edit)?.Entry
        );
        Assert.Equal(HotkeyTable.AddToEnd, HotkeyResolver.Resolve(Key.Backtick, Modifiers.Shift, Edit)?.Entry);
        Assert.Equal(
            HotkeyTable.LevelRoll,
            HotkeyResolver.Resolve(Key.R, Modifiers.Alt | Modifiers.Shift, Edit)?.Entry
        );
        Assert.Equal(HotkeyTable.Undo, HotkeyResolver.Resolve(Key.Z, Modifiers.Ctrl | Modifiers.Shift, Edit)?.Entry);
    }

    [Fact]
    public void AnExtraCtrlOrAltMatchesNothing()
    {
        // Alt + Space, Ctrl + R, Ctrl + Alt + Backtick and Ctrl + Alt + Z are in no table row.
        Assert.Null(HotkeyResolver.Resolve(Key.Space, Modifiers.Alt, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.R, Modifiers.Ctrl, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.Backtick, Modifiers.Ctrl | Modifiers.Alt, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.Z, Modifiers.Ctrl | Modifiers.Alt, Edit));
    }

    [Fact]
    public void AMissingCtrlMatchesNothingOrTheBareKey()
    {
        // Z alone and Y alone are in no row; Space alone is Play, not Restart.
        Assert.Null(HotkeyResolver.Resolve(Key.Z, Modifiers.None, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.Y, Modifiers.None, Edit));
        Assert.Equal(HotkeyTable.Play, HotkeyResolver.Resolve(Key.Space, Modifiers.None, Edit)?.Entry);
    }

    [Fact]
    public void RIsHiddenInEditButOnlyTogglesWithAGizmoTarget()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.GizmoToggle, Acts: false, Hidden: true),
            HotkeyResolver.Resolve(Key.R, Modifiers.None, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.GizmoToggle, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.R, Modifiers.None, Edit with { GizmoTarget = true })
        );
        Assert.Null(HotkeyResolver.Resolve(Key.R, Modifiers.None, Live with { GizmoTarget = true }));
    }

    [Fact]
    public void DeleteAndBackspaceAreOursOnlyWithPointsSelected()
    {
        var selected = Edit with { PointsSelected = true };
        Assert.Null(HotkeyResolver.Resolve(Key.Delete, Modifiers.None, Edit));
        Assert.Equal(
            new HotkeyPress(HotkeyTable.DeleteSelectedPoints, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Delete, Modifiers.None, selected)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.DeleteSelectedPoints, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Backspace, Modifiers.None, selected)
        );
    }

    [Fact]
    public void DeleteNeverFiresOutsideEditOrWithAnExtraModifier()
    {
        Assert.Null(HotkeyResolver.Resolve(Key.Delete, Modifiers.None, Live with { PointsSelected = true }));
        Assert.Null(HotkeyResolver.Resolve(Key.Delete, Modifiers.None, View with { PointsSelected = true }));
        Assert.Null(HotkeyResolver.Resolve(Key.Delete, Modifiers.None, Off with { PointsSelected = true }));
        Assert.Null(HotkeyResolver.Resolve(Key.Delete, Modifiers.Ctrl, Edit with { PointsSelected = true }));
    }

    [Fact]
    public void GTogglesColourWhileTheOverlayShowsAndIsHiddenOnlyInEdit()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.ColourByTurnSpeed, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.G, Modifiers.None, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.ColourByTurnSpeed, Acts: true, Hidden: false),
            HotkeyResolver.Resolve(Key.G, Modifiers.None, View)
        );
        // Edit while a preview plays without the ghost hides the overlay.
        Assert.Null(HotkeyResolver.Resolve(Key.G, Modifiers.None, Edit with { OverlayShown = false }));
        Assert.Null(HotkeyResolver.Resolve(Key.G, Modifiers.Ctrl, View));
    }

    [Fact]
    public void EscapeRestoresTheUiOnlyInLiveAndIsNeverHiddenHere()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.RestoreGameUi, Acts: true, Hidden: false),
            HotkeyResolver.Resolve(Key.Escape, Modifiers.None, Live)
        );
        Assert.Null(HotkeyResolver.Resolve(Key.Escape, Modifiers.None, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.Escape, Modifiers.Ctrl, Live));
    }

    [Fact]
    public void BacktickAddsInEditOnlyAndIsAlwaysHidden()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.AddToEnd, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Backtick, Modifiers.None, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.AddAfterSelected, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Backtick, Modifiers.Alt, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.OverwriteSelected, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Backtick, Modifiers.Ctrl, Edit)
        );
        Assert.Null(HotkeyResolver.Resolve(Key.Backtick, Modifiers.None, Live));
        Assert.Null(HotkeyResolver.Resolve(Key.Backtick, Modifiers.None, View));
        Assert.Null(HotkeyResolver.Resolve(Key.Backtick, Modifiers.None, Off));
    }

    [Fact]
    public void CtrlSpaceRestartsInEditAndLiveOnly()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.Restart, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Space, Modifiers.Ctrl, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.Restart, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Space, Modifiers.Ctrl, Live)
        );
        Assert.Null(HotkeyResolver.Resolve(Key.Space, Modifiers.Ctrl, Off));
        Assert.Null(HotkeyResolver.Resolve(Key.Space, Modifiers.Ctrl, View));
    }

    [Fact]
    public void AltRLevelsRollInEditOnly()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.LevelRoll, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.R, Modifiers.Alt, Edit)
        );
        Assert.Null(HotkeyResolver.Resolve(Key.R, Modifiers.Alt, Live));
        Assert.Null(HotkeyResolver.Resolve(Key.R, Modifiers.Alt, View));
        Assert.Null(HotkeyResolver.Resolve(Key.R, Modifiers.Alt, Off));
    }

    [Fact]
    public void CtrlZUndoesAndCtrlYRedoesInEditOnly()
    {
        Assert.Equal(
            new HotkeyPress(HotkeyTable.Undo, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Z, Modifiers.Ctrl, Edit)
        );
        Assert.Equal(
            new HotkeyPress(HotkeyTable.Redo, Acts: true, Hidden: true),
            HotkeyResolver.Resolve(Key.Y, Modifiers.Ctrl, Edit)
        );
        Assert.Null(HotkeyResolver.Resolve(Key.Z, Modifiers.Ctrl, Live));
        Assert.Null(HotkeyResolver.Resolve(Key.Y, Modifiers.Ctrl, Live));
    }

    [Fact]
    public void FlightKeysAreNeverPresses()
    {
        Assert.Null(HotkeyResolver.Resolve(Key.W, Modifiers.None, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.Q, Modifiers.Ctrl, Edit));
        Assert.Null(HotkeyResolver.Resolve(Key.Shift, Modifiers.Shift, Edit));
    }

    [Fact]
    public void QAndEFlyWithoutCtrlAndRollWithIt()
    {
        static bool QAndW(Key k) => k is Key.Q or Key.W;
        // W forward (+1), Q down (−1 up); no roll.
        Assert.Equal(new FlightKeys(new Vector3(1f, -1f, 0f), 0f, false), HotkeyResolver.Flight(QAndW, Modifiers.None));
        // With Ctrl, Q rolls left (−1) and no longer flies down; W still flies forward.
        Assert.Equal(new FlightKeys(new Vector3(1f, 0f, 0f), -1f, false), HotkeyResolver.Flight(QAndW, Modifiers.Ctrl));
    }

    [Fact]
    public void FlightIgnoresAltAndShiftAndShiftKeyFliesFaster()
    {
        static bool DEAndShift(Key k) => k is Key.D or Key.E or Key.Shift;
        // D right (+1), E up (+1); Alt changes nothing; Shift held flies faster.
        Assert.Equal(
            new FlightKeys(new Vector3(0f, 1f, 1f), 0f, true),
            HotkeyResolver.Flight(DEAndShift, Modifiers.Alt | Modifiers.Shift)
        );
    }

    [Fact]
    public void OppositeFlightKeysCancel()
    {
        static bool All(Key k) => k is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E;
        Assert.Equal(new FlightKeys(Vector3.Zero, 0f, false), HotkeyResolver.Flight(All, Modifiers.None));
        Assert.Equal(new FlightKeys(Vector3.Zero, 0f, false), HotkeyResolver.Flight(All, Modifiers.Ctrl));
    }

    [Fact]
    public void OfMarksTheGizmoTargetForOnePointOrAnAnchorButNotTwoPoints()
    {
        var state = EditingOverGround();
        state.Selection.Select(1);
        Assert.Equal(new HotkeyContext(CameraMode.Editing, true, true, true), HotkeyContext.Of(state));

        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);
        Assert.Equal(new HotkeyContext(CameraMode.Editing, true, true, false), HotkeyContext.Of(state));

        state.Selection.SelectSceneAnchor();
        Assert.Equal(new HotkeyContext(CameraMode.Editing, true, false, true), HotkeyContext.Of(state));

        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        Assert.Equal(new HotkeyContext(CameraMode.Editing, true, false, true), HotkeyContext.Of(state));
    }

    [Fact]
    public void OfReflectsTheOverlayInViewAndOff()
    {
        var state = EditingOverGround();
        state.Release(CameraMode.View);
        Assert.Equal(new HotkeyContext(CameraMode.View, true, false, false), HotkeyContext.Of(state));

        state.Release();
        Assert.Equal(new HotkeyContext(CameraMode.Off, false, false, false), HotkeyContext.Of(state));
    }
}
