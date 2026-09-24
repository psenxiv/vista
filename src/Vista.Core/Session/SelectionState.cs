using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Session;

/// <summary>What's selected while editing: points, tracks, playlist entries, a timing key or leg, or an anchor, and the rules for clicking them.</summary>
public sealed class SelectionState
{
    private const string SceneAnchorUnplaced = "The scene anchor is placed with the scene's first point.";
    private const string TrackAnchorUnplaced = "A track's anchor is placed with its first point.";
    private const string FollowAnchorHidden = "A Follow Target track's anchor is hidden";
    private const string LookAtUnused = "The Look At point is used only while the track aims at it.";

    private readonly SessionState session;
    private SelectedItems selection = SelectedItems.None;
    private Guid? lastTrack;
    private Guid? lastEntry;

    internal SelectionState(SessionState session) => this.session = session;

    /// <summary>The selected point's index when exactly one is selected, or null.</summary>
    public int? Point => selection.Points.Count == 1 ? selection.Points[0] : null;

    /// <summary>The edited track's selected points, in order.</summary>
    public IReadOnlyList<int> Points => selection.Points;

    /// <summary>The edited track and any other selected tracks, in Hierarchy order.</summary>
    public IReadOnlyList<Guid> Tracks => session.Scene.Tracks.Select(t => t.Id).Where(id => id == session.EditedTrackId || selection.Tracks.Contains(id)).ToArray();

    /// <summary>The selected playlist entries, in playlist order.</summary>
    public IReadOnlyList<Guid> Entries => session.Scene.Playlist.Select(e => e.Id).Where(selection.Entries.Contains).ToArray();

    /// <summary>The selected timing key's index, or null. Never set together with <see cref="Leg"/>.</summary>
    public int? Key { get; internal set; }

    /// <summary>The selected leg, or null. Never set together with <see cref="Key"/>.</summary>
    public int? Leg { get; private set; }

    /// <summary>The selected anchor, or null; never set together with a selected point.</summary>
    public AnchorKind? Anchor { get; private set; }

    /// <summary>The selected scene or track anchor in the world, or null.</summary>
    public Anchor? AnchorInWorld => Anchor switch
    {
        AnchorKind.Scene => session.Scene.Anchor,
        AnchorKind.Track => SceneGeometry.WorldAnchor(session.Scene, session.StoredTrack),
        _ => null,
    };

    /// <summary>The selected Look At point in the world, or null.</summary>
    public Vector3? LookAtInWorld => Anchor == AnchorKind.LookAt ? session.Track.LookAt : null;

    /// <summary>The selection as an undo step records it; setting it applies no other rule.</summary>
    internal SelectedItems Value { get => selection; set => selection = value; }

    /// <summary>The last point clicked, the start of a Shift range, or null.</summary>
    internal int? LastPoint { get; set; }

    /// <summary>Selects only a point while editing, as a plain click does; null or an index out of range clears every selection.</summary>
    public void Select(int? index)
    {
        if (session.Mode != CameraMode.Editing) return;
        var point = index is { } i && i >= 0 && i < session.StoredTrack.Points.Count ? i : (int?)null;
        SelectAnchorless(new SelectedItems(point is { } p ? [p] : [], [], []));
        LastPoint = point;
    }

    /// <summary>Applies a click to point <paramref name="index"/>: plain selects only it; Ctrl and Shift add to the point selection, unless two or more of another kind are selected.</summary>
    public void ClickPoint(int index, RowClick click)
    {
        var count = session.StoredTrack.Points.Count;
        if (session.Mode != CameraMode.Editing || index < 0 || index >= count) return;
        if (click == RowClick.Plain)
        {
            Select(index);
            return;
        }

        if (Tracks.Count >= 2 || Entries.Count >= 2) return;
        var (points, last) = RowPicking.Click(Enumerable.Range(0, count).ToArray(), selection.Points, LastPoint, index, click);
        SelectAnchorless(new SelectedItems(points, [], []));
        if (points.Count > 0) LastPoint = last;
    }

    /// <summary>Applies a click to track <paramref name="id"/>: plain edits it and selects only it; Ctrl and Shift add to the track selection, unless two or more of another kind are selected. Returns why it was refused, or null.</summary>
    public string? ClickTrack(Guid id, RowClick click)
    {
        var scene = session.Scene;
        if (session.Mode != CameraMode.Editing) return "Tracks can only be selected while editing.";
        if (SceneEditing.IndexOf(scene, id) < 0) return "There is no such track.";
        if (click == RowClick.Plain)
        {
            if (session.SwitchTrack(id) is { } refusal) return refusal;
            SelectAnchorless(SelectedItems.None);
            lastTrack = id;
            return null;
        }

        var edited = session.EditedTrackId;
        if (id == edited && click == RowClick.Toggle) return null;
        if (selection.Points.Count >= 2 || Entries.Count >= 2) return null;
        var (tracks, last) = RowPicking.Click(scene.Tracks.Select(t => t.Id).ToArray(), Tracks, lastTrack ?? edited, id, click);
        SelectAnchorless(new SelectedItems([], tracks.Where(t => t != edited).ToArray(), []));
        lastTrack = tracks.Count > 1 ? last : null;
        return null;
    }

    /// <summary>Applies a click to playlist entry <paramref name="id"/>: plain selects only it; Ctrl and Shift add to the entry selection, unless two or more of another kind are selected. Returns why it was refused, or null.</summary>
    public string? ClickEntry(Guid id, RowClick click)
    {
        var scene = session.Scene;
        if (session.Mode != CameraMode.Editing) return "Playlist entries can only be selected while editing.";
        if (PlaylistEditing.IndexOf(scene, id) < 0) return "There is no such playlist entry.";
        if (click != RowClick.Plain && (selection.Points.Count >= 2 || Tracks.Count >= 2)) return null;

        var (entries, last) = RowPicking.Click(scene.Playlist.Select(e => e.Id).ToArray(), Entries, lastEntry, id, click);
        SelectAnchorless(new SelectedItems([], [], entries));
        if (entries.Count > 0) lastEntry = last;
        return null;
    }

    /// <summary>Selects a timing key while editing; a point's key also selects its point.</summary>
    public void SelectKey(int? key)
    {
        if (session.Mode != CameraMode.Editing) return;
        var local = session.StoredTrack;
        Leg = null;
        Key = key is { } k && k >= 0 && k < TrackEditing.KeyCount(local) ? k : null;
        if (Key is { } s && TrackEditing.RoleOf(local, s) == KeyRole.Point)
        {
            SelectAnchorless(new SelectedItems([TrackEditing.PointOf(local, s)], [], []));
            Key = s;
        }
    }

    /// <summary>Selects a leg while editing, leaving the point selection alone.</summary>
    public void SelectLeg(int? leg)
    {
        if (session.Mode != CameraMode.Editing) return;
        Key = null;
        Leg = leg is { } l && l >= 1 && l < session.StoredTrack.Points.Count ? l : null;
    }

    /// <summary>Selects the scene anchor, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectSceneAnchor()
    {
        if (session.Mode != CameraMode.Editing) return "Anchors can only be selected while editing.";
        if (UnplacedRefusal(AnchorKind.Scene) is { } unplaced) return unplaced;
        SelectAnchor(AnchorKind.Scene);
        return null;
    }

    /// <summary>Edits track <paramref name="id"/> and selects its anchor, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectTrackAnchor(Guid id)
    {
        if (session.Mode != CameraMode.Editing) return "Anchors can only be selected while editing.";
        if (SceneEditing.IndexOf(session.Scene, id) < 0) return "There is no such track.";
        var track = SceneEditing.Get(session.Scene, id);
        if (track.Aim == AimMode.FollowTarget) return FollowAnchorHidden;
        if (!track.AnchorPlaced) return TrackAnchorUnplaced;
        if (session.SwitchTrack(id) is { } refusal) return refusal;
        SelectAnchor(AnchorKind.Track);
        return null;
    }

    /// <summary>Edits track <paramref name="id"/> and selects its Look At point, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectLookAt(Guid id)
    {
        if (session.Mode != CameraMode.Editing) return "The Look At point can only be selected while editing.";
        if (SceneEditing.IndexOf(session.Scene, id) < 0) return "There is no such track.";
        if (SceneEditing.Get(session.Scene, id) is not { Aim: AimMode.LookAt, LookAtPlaced: true }) return LookAtUnused;
        if (session.SwitchTrack(id) is { } refusal) return refusal;
        SelectAnchor(AnchorKind.LookAt);
        return null;
    }

    /// <summary>Why the <paramref name="kind"/> selection cannot be used: an unplaced or hidden anchor, or a Look At point not in use.</summary>
    internal string? UnplacedRefusal(AnchorKind kind)
    {
        var local = session.StoredTrack;
        return kind switch
        {
            AnchorKind.Scene => session.Scene.AnchorPlaced ? null : SceneAnchorUnplaced,
            AnchorKind.Track => local.Aim == AimMode.FollowTarget ? FollowAnchorHidden : local.AnchorPlaced ? null : TrackAnchorUnplaced,
            _ => local is { Aim: AimMode.LookAt, LookAtPlaced: true } ? null : LookAtUnused,
        };
    }

    /// <summary>Clears the track and entry selections and any selection of two or more points, as leaving Edit does.</summary>
    internal void DropGroup()
    {
        if (selection.Points.Count > 1 || selection.Tracks.Count > 0 || selection.Entries.Count > 0) Set(SelectedItems.None);
    }

    /// <summary>Selects points <paramref name="points"/> of the edited track, in order.</summary>
    internal void SelectPoints(IReadOnlyList<int> points) => Set(new SelectedItems(points.Distinct().Order().ToArray(), [], []));

    /// <summary>After an edit is committed: selects <paramref name="points"/> of the edited track, forgets a last point past its end, and drops an anchor it no longer has.</summary>
    internal void AfterCommit(IReadOnlyList<int> points, Track edited)
    {
        selection = selection with { Points = points };
        if (points.Count == 0 || LastPoint >= edited.Points.Count) LastPoint = null;
        if (Anchor is { } kind && UnplacedRefusal(kind) is not null) Anchor = null;
    }

    /// <summary>After a timing change: clears a key or leg past the end of the edited track.</summary>
    internal void AfterTiming()
    {
        var local = session.StoredTrack;
        if (Key is { } key && key >= TrackEditing.KeyCount(local)) Key = null;
        if (Leg is { } leg && leg >= local.Points.Count) Leg = null;
    }

    /// <summary>After an undo or redo: puts back the recorded <paramref name="value"/> and brings the timing selection in step.</summary>
    internal void Restore(SelectedItems value, IReadOnlyList<ControlPoint> pointsBefore)
    {
        selection = value;
        LastPoint = null;
        if (selection.Points.Count > 0 || (Anchor is { } kind && UnplacedRefusal(kind) is not null)) Anchor = null;
        RefreshTiming(pointsBefore);
    }

    /// <summary>Clears every selection, key and leg, as switching tracks does.</summary>
    internal void Clear()
    {
        selection = SelectedItems.None;
        LastPoint = null;
        lastTrack = null;
        lastEntry = null;
        Key = null;
        Leg = null;
        Anchor = null;
    }

    /// <summary>After a point edit: a point key follows its point, and any other timing selection clears unless it's a leg and the points are unchanged.</summary>
    internal void RefreshTiming(IReadOnlyList<ControlPoint> pointsBefore)
    {
        if (selection.Points.Count > 1)
        {
            SyncKeyToPoint();
            return;
        }

        var local = session.StoredTrack;
        if (!ReferenceEquals(pointsBefore, local.Points) && !pointsBefore.SequenceEqual(local.Points)) Leg = null;
        if (Key is { } key && (Point is null || key >= TrackEditing.KeyCount(local) || TrackEditing.RoleOf(local, key) != KeyRole.Point)) Key = null;
        if (Point is not null && Leg is null) Key = TrackEditing.PointKey(local, Point.Value);
    }

    /// <summary>Replaces the selection and clears any selected anchor.</summary>
    private void SelectAnchorless(SelectedItems next)
    {
        Anchor = null;
        Set(next);
    }

    /// <summary>Replaces the selection, forgets the last row clicked of each kind it leaves empty, and keeps the timing selection in step with its points.</summary>
    private void Set(SelectedItems next)
    {
        selection = next;
        if (next.Points.Count == 0) LastPoint = null;
        if (next.Tracks.Count == 0) lastTrack = null;
        if (next.Entries.Count == 0) lastEntry = null;
        SyncKeyToPoint();
    }

    private void SelectAnchor(AnchorKind kind)
    {
        session.EndLiveEdit();
        Set(SelectedItems.None);
        Anchor = kind;
    }

    /// <summary>Points the timing selection at the selected point's key, or clears a point key's selection when no point is selected.</summary>
    private void SyncKeyToPoint()
    {
        var local = session.StoredTrack;
        if (selection.Points.Count > 1)
        {
            Key = null;
            Leg = null;
        }
        else if (Point is { } point)
        {
            Key = TrackEditing.PointKey(local, point);
            Leg = null;
        }
        else if (Key is { } key && (key >= TrackEditing.KeyCount(local) || TrackEditing.RoleOf(local, key) == KeyRole.Point))
        {
            Key = null;
        }
    }
}
