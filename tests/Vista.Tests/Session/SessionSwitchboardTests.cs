using Vista.Core.Scenes;
using Vista.Core.Session;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionSwitchboardTests
{
    // Live as OnAirScene leaves it: slot 0 on Program at 3 s, slot 1 Next, slots 1 and 2 resuming at 2 s and 4 s.
    private static readonly OnAir OnAirLive = new(0, 1, 3.0, Resume((1, 2.0), (2, 4.0)));

    public static TheoryData<string, Func<SessionState, string?>> Commands =>
        new()
        {
            { "assign", s => s.AssignSlot(3, s.Scene.Tracks[1].Id) },
            { "rename", s => s.RenameSlot(0, "Wide") },
            { "clear", s => s.ClearSlot(2) },
            { "toggle", s => s.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true) },
        };

    // The slots and toggles, as one comparable value.
    private static (string Slots, bool, bool, bool) SlotsAndToggles(SessionState state)
    {
        var board = state.Scene.Switchboard;
        return (string.Join("|", board.Slots), board.DirectCut, board.KeepRolling, board.AutoNext);
    }

    // A session with OnAirScene open, in Edit.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.LoadScene(OnAirScene());
        state.Edit();
        return state;
    }

    // Editing OnAirScene with Track 1 given points at x = 0 and 10 and put in Playlist 1, so Live has a shot to play.
    private static SessionState EditingPlayable()
    {
        var scene = OnAirScene();
        scene = SceneEditing.Replace(scene, WithTwoPoints(scene.Tracks[0]));
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        var state = new SessionState();
        state.LoadScene(scene);
        state.Edit();
        return state;
    }

    // A toggle changing the scene pins that the scene comparison behind undo steps sees the switchboard.
    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandIsOneUndoStepInEdit(string _, Func<SessionState, string?> command)
    {
        var state = Editing();
        var before = SlotsAndToggles(state);

        Assert.Null(command(state));

        Assert.NotEqual(before, SlotsAndToggles(state));
        Assert.True(state.CanUndo);
        Assert.True(state.Undo());
        Assert.Equal(before, SlotsAndToggles(state));
        Assert.False(state.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandWorksLiveWithoutAnUndoStep(string _, Func<SessionState, string?> command)
    {
        // One undo step (Auto Next on) before going live; the live command adds none, so one Undo empties the history.
        var state = EditingPlayable();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        GoLive(state);
        Assert.Equal(CameraMode.Live, state.Mode);
        var before = SlotsAndToggles(state);

        Assert.Null(command(state));

        var after = SlotsAndToggles(state);
        Assert.NotEqual(before, after);
        state.Edit();
        Assert.Equal(after, SlotsAndToggles(state));
        Assert.False(state.CanRedo);
        Assert.True(state.Undo());
        Assert.False(state.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandIsRefusedOffAndInView(string _, Func<SessionState, string?> command)
    {
        var state = new SessionState();
        state.LoadScene(OnAirScene());
        var scene = state.Scene;

        Assert.Equal("The scene can only change while editing.", command(state));
        state.Release(CameraMode.View);
        Assert.Equal("The scene can only change while editing.", command(state));
        Assert.Same(scene, state.Scene);
    }

    [Fact]
    public void UndoKeepsLivesPosition()
    {
        var state = Editing();
        state.AssignSlot(3, state.Scene.Tracks[1].Id);

        state.Undo();

        Assert.Null(state.Scene.Switchboard.Slots[3]);
        SameAir(OnAirLive, state.Scene.Switchboard.Live);
    }

    [Fact]
    public void UndoDoesntPutBackWhatAChangeTookOffAir()
    {
        // Clearing slot 0 takes Program off it; undo brings the slot back but keeps Live as it now is.
        var state = Editing();
        var slot = state.Scene.Switchboard.Slots[0];
        state.ClearSlot(0);

        state.Undo();

        Assert.Equal(slot, state.Scene.Switchboard.Slots[0]);
        SameAir(new OnAir(null, 1, 0.0, Resume((1, 2.0), (2, 4.0))), state.Scene.Switchboard.Live);
    }

    [Fact]
    public void UndoingATrackDeletionBringsItsSlotsBack()
    {
        // Slot 0 is on Track 1.
        var state = Editing();
        var slot = state.Scene.Switchboard.Slots[0];
        state.DeleteTracks([state.Scene.Tracks[0].Id]);

        state.Undo();
        Assert.Equal(slot, state.Scene.Switchboard.Slots[0]);
    }

    [Fact]
    public void UndoingAPlaylistDeletionBringsItsSlotsBack()
    {
        // Slot 1 is on Playlist 1.
        var state = Editing();
        var slot = state.Scene.Switchboard.Slots[1];
        state.DeletePlaylist(state.Scene.Playlists[0].Id);

        state.Undo();
        Assert.Equal(slot, state.Scene.Switchboard.Slots[1]);
    }

    [Fact]
    public void ARefusedCommandIsNoUndoStep()
    {
        var state = Editing();

        Assert.Equal("That slot is empty.", state.RenameSlot(5, "Wide"));
        Assert.False(state.CanUndo);
    }
}
