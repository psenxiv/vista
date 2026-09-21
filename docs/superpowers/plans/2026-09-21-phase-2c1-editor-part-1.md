# Phase 2c-1 Editor, Part 1: Core and Probes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the editor's pure logic in Core, test-first, and the three in-game probes the rest of the editor depends on.

**Architecture:** Core gains the track edits (insert, delete, move, replace), selection and undo in `SessionState`, playback seek, and the geometry the overlay and gizmo need: screen projection, marker hit-testing and pose/matrix conversion. The plugin gains three temporary `/ccam probe` commands that answer the spec's three questions in game. Part 2 (overlay, gizmo, windows, keys, scrub) is written after the probes report.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5 (`Dalamud.NET.Sdk/15.0.0`), `Dalamud.Bindings.ImGui`, `Dalamud.Bindings.ImGuizmo`, FFXIVClientStructs.

**Spec:** `docs/superpowers/specs/2026-09-21-editor-design.md` (editor), within `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md` (main). Citations are `editor:N-M` for the editor spec.

## Global Constraints

- `CinematicCam.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`.
- `tests/CinematicCam.Tests` references Core only.
- Namespaces match folders. **Never create a `CinematicCam.Plugin.Camera` namespace**, because it shadows FFXIVClientStructs' `Camera`.
- Build the plugin with `./build.sh`, never bare `dotnet build`. Tests: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`.
- Never write `Camera.Distance` or `InterpDistance`. The only new game-field write in this plan is probe 2's `DirH`/`DirV`, and only on the `/ccam probe aim` command.
- Doc comments are one line, stating what a thing is or does. Inline comments are rare.
- Commits: one line, conventional prefix, lowercase, no trailing period, **no body, no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Work and commit on `main`. Do not push.
- BDTHPlugin and Cammy have no licence: read them as reference only, never copy code.
- Do not add features, options or behaviour this plan does not name. If something needs a decision, stop and report it; do not settle it yourself.

---

### Task 1: Track edits in Core

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackEditing.cs`
- Modify: `src/CinematicCam.Core/Tracks/TrackEvaluator.cs` (make `MinTimingLength` public)
- Test: `tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs`

**Interfaces:**
- Produces, on `TrackEditing`:
  - `public static Track InsertAfter(Track track, int index, ControlPoint point)`
  - `public static Track Delete(Track track, int index)`
  - `public static Track Move(Track track, int from, int to)`
  - `public static Track Replace(Track track, int index, ControlPoint point)`
- `TrackEvaluator.MinTimingLength` becomes `public const float MinTimingLength = 0.1f;`.
- Bad indices throw `ArgumentOutOfRangeException` (via the existing `ValidatePointIndex`). A track with a timing key between points, or a point with no keys or more than two, throws `ArgumentException("timing keys between points are not supported yet")` from `InsertAfter`, `Delete` and `Move`. `Replace` never touches timing, so it doesn't check.

Timing rules (editor:133-151):
- **InsertAfter** splits the leg it lands in, in proportion to the new path's arc length on each side of the new point. Each side counts as at least `MinTimingLength`. The total is unchanged, the new point has no hold, and the selected point keeps its hold. Inserting after the last point is `Append`.
- **Delete:** a middle point's incoming leg, hold and outgoing leg merge (total unchanged). The first point's hold and outgoing leg go, and later keys shift earlier so the new first key keeps the old first key's time. The last point's incoming leg and hold go. The only point: the track becomes empty, keeping its aim and playback.
- **Move:** holds travel with their point, and leg times stay in their slots.
- **Replace:** timing unchanged.

- [ ] **Step 1: Make `MinTimingLength` public**

In `TrackEvaluator.cs`, change `private const float MinTimingLength = 0.1f;` to `public const float MinTimingLength = 0.1f;`. Keep its doc comment.

- [ ] **Step 2: Write the failing tests**

Add to `TrackEditingTests` (it already has `Point(x, y, z)` and `Build3PointTrack()`, which has points at x = 0, 10, 20 and keys at 0, 5, 10 s):

```csharp
    private static float Total(Track track) => track.Timing[^1].Time;

    [Fact]
    public void InsertAfterSplitsTheLegByPathLengthAndKeepsTheTotal()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.InsertAfter(track, 0, Point(2f, 0f, 0f));

        var table = new ArcLengthTable(result.Points.Select(p => p.Position).ToArray());
        var before = table.SegmentLength(0);
        var after = table.SegmentLength(1);

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(5f * before / (before + after), TrackEditing.LegSeconds(result, 1), 3);
        Assert.Equal(5f, TrackEditing.LegSeconds(result, 1) + TrackEditing.LegSeconds(result, 2), 4);
        Assert.Equal(Total(track), Total(result), 4);
        Assert.Equal(5f, TrackEditing.LegSeconds(result, 3), 4);
    }

    [Fact]
    public void InsertAfterKeepsTheSelectedPointsHoldAndGivesTheNewPointNone()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 0, 2f);
        var result = TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f));

        Assert.Equal(2f, TrackEditing.HoldSeconds(result, 0), 4);
        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 1));
        Assert.Equal(Total(track), Total(result), 4);
    }

    [Fact]
    public void InsertAfterTheLastPointAppends()
    {
        var track = Build3PointTrack();
        var result = TrackEditing.InsertAfter(track, 2, Point(30f, 0f, 0f));

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(TrackEditing.DefaultLegSeconds, TrackEditing.LegSeconds(result, 3));
    }

    [Fact]
    public void InsertBetweenCoincidentPointsSplitsEvenly()
    {
        var track = TrackEditing.Empty();
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        var result = TrackEditing.InsertAfter(track, 0, Point(0f, 0f, 0f));

        Assert.Equal(2.5f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(2.5f, TrackEditing.LegSeconds(result, 2), 4);
    }

    [Fact]
    public void DeletingAMiddlePointMergesItsLegsAndHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var result = TrackEditing.Delete(track, 1);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(12f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(Total(track), Total(result), 4);
    }

    [Fact]
    public void DeletingTheFirstPointDropsItsHoldAndLeg()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 0, 1f);
        var result = TrackEditing.Delete(track, 0);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(0f, result.Timing[0].Time);
        Assert.Equal(0f, result.Timing[0].Position);
        Assert.Equal(5f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(5f, Total(result), 4);
    }

    [Fact]
    public void DeletingTheLastPointDropsItsLegAndHold()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 2, 2f);
        var result = TrackEditing.Delete(track, 2);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(5f, Total(result), 4);
    }

    [Fact]
    public void DeletingTheOnlyPointEmptiesTheTrackButKeepsItsModes()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.PathTangent), Point(0f, 0f, 0f));
        track = TrackEditing.SetPlayback(track, PlaybackMode.Loop);
        var result = TrackEditing.Delete(track, 0);

        Assert.Empty(result.Points);
        Assert.Empty(result.Timing);
        Assert.Equal(AimMode.PathTangent, result.Aim);
        Assert.Equal(PlaybackMode.Loop, result.Playback);
    }

    [Fact]
    public void MovingAPointCarriesItsHoldAndLeavesLegsInTheirSlots()
    {
        var track = Build3PointTrack();
        track = TrackEditing.SetLeg(track, 1, 3f);
        track = TrackEditing.SetLeg(track, 2, 7f);
        track = TrackEditing.SetHold(track, 2, 1f);
        var moved = track.Points[2];

        var result = TrackEditing.Move(track, 2, 0);

        Assert.Same(moved, result.Points[0]);
        Assert.Same(track.Points[0], result.Points[1]);
        Assert.Same(track.Points[1], result.Points[2]);
        Assert.Equal(1f, TrackEditing.HoldSeconds(result, 0), 4);
        Assert.Equal(0f, TrackEditing.HoldSeconds(result, 2));
        Assert.Equal(3f, TrackEditing.LegSeconds(result, 1), 4);
        Assert.Equal(7f, TrackEditing.LegSeconds(result, 2), 4);
        Assert.Equal(Total(track), Total(result), 4);
    }

    [Fact]
    public void MovingAPointToWhereItIsChangesNothing()
    {
        var track = Build3PointTrack();
        Assert.Same(track, TrackEditing.Move(track, 1, 1));
    }

    [Fact]
    public void ReplaceKeepsEveryTimingKey()
    {
        var track = TrackEditing.SetHold(Build3PointTrack(), 1, 2f);
        var point = Point(99f, 1f, 2f);
        var result = TrackEditing.Replace(track, 1, point);

        Assert.Same(point, result.Points[1]);
        Assert.Same(track.Timing, result.Timing);
    }

    [Fact]
    public void EditsRefuseTimingKeysBetweenPoints()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[]
        {
            new TimingKey(0f, 0f, TangentMode.Auto, 0f, 0f),
            new TimingKey(2f, 0.5f, TangentMode.Auto, 0f, 0f),
            new TimingKey(5f, 1f, TangentMode.Auto, 0f, 0f),
        };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        const string message = "timing keys between points are not supported yet";
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.InsertAfter(track, 0, Point(5f, 0f, 0f))).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Delete(track, 0)).Message);
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => TrackEditing.Move(track, 0, 1)).Message);
    }

    [Fact]
    public void EditsRejectOutOfRangeIndices()
    {
        var track = Build3PointTrack();
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.InsertAfter(track, 3, Point(0f, 0f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Delete(track, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Move(track, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.Replace(track, 3, Point(0f, 0f, 0f)));
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "FullyQualifiedName~TrackEditingTests"`
Expected: build error. `InsertAfter`, `Delete`, `Move` and `Replace` don't exist.

- [ ] **Step 4: Implement**

Add to `TrackEditing`, after `SetPlayback`:

```csharp
    /// <summary>Inserts a point after point <paramref name="index"/>, splitting that leg by path length; after the last point it appends.</summary>
    public static Track InsertAfter(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "insert");
        RequireKeyPerPoint(track);
        if (index == track.Points.Count - 1) return Append(track, point);

        var points = new List<ControlPoint>(track.Points);
        points.Insert(index + 1, point);

        var table = new ArcLengthTable(points.Select(p => p.Position).ToArray());
        var before = MathF.Max(table.SegmentLength(index), TrackEvaluator.MinTimingLength);
        var after = MathF.Max(table.SegmentLength(index + 1), TrackEvaluator.MinTimingLength);

        var keys = track.Timing;
        var start = keys[LastKeyIndex(keys, index)].Time;
        var leg = keys[FirstKeyIndex(keys, index + 1)].Time - start;
        var inserted = new TimingKey(start + (leg * before / (before + after)), index + 1, TangentMode.Auto, 0f, 0f);

        var timing = new List<TimingKey>(keys.Count + 1);
        timing.AddRange(keys.Where(k => k.Position <= index));
        timing.Add(inserted);
        timing.AddRange(keys.Where(k => k.Position > index).Select(k => k with { Position = k.Position + 1 }));

        return track with { Points = points, Timing = timing };
    }

    /// <summary>Removes point <paramref name="index"/>: a middle point's legs and hold merge, an end point's leg and hold go.</summary>
    public static Track Delete(Track track, int index)
    {
        ValidatePointIndex(track, index, "delete");
        RequireKeyPerPoint(track);
        if (track.Points.Count == 1)
            return track with { Points = Array.Empty<ControlPoint>(), Timing = Array.Empty<TimingKey>() };

        var keys = track.Timing;
        var shift = index == 0 ? keys[FirstKeyIndex(keys, 1)].Time - keys[0].Time : 0f;

        var points = new List<ControlPoint>(track.Points);
        points.RemoveAt(index);

        var timing = keys
            .Where(k => k.Position != index)
            .Select(k => k with { Time = k.Time - shift, Position = k.Position > index ? k.Position - 1 : k.Position })
            .ToList();

        return track with { Points = points, Timing = timing };
    }

    /// <summary>Moves point <paramref name="from"/> to position <paramref name="to"/>; holds travel with their point, leg times stay in their slots.</summary>
    public static Track Move(Track track, int from, int to)
    {
        ValidatePointIndex(track, from, "move");
        ValidatePointIndex(track, to, "move");
        RequireKeyPerPoint(track);
        if (from == to) return track;

        var n = track.Points.Count;
        var keys = track.Timing;
        var legs = new float[n];
        for (var i = 1; i < n; i++) legs[i] = LegSeconds(track, i);

        var order = Enumerable.Range(0, n).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);

        var timing = new List<TimingKey>(keys.Count);
        var time = keys[0].Time;
        for (var slot = 0; slot < n; slot++)
        {
            var firstIndex = FirstKeyIndex(keys, order[slot]);
            var lastIndex = LastKeyIndex(keys, order[slot]);
            if (slot > 0) time += legs[slot];

            timing.Add(keys[firstIndex] with { Time = time, Position = slot });
            if (lastIndex == firstIndex) continue;

            time += keys[lastIndex].Time - keys[firstIndex].Time;
            timing.Add(keys[lastIndex] with { Time = time, Position = slot });
        }

        return track with { Points = order.Select(i => track.Points[i]).ToList(), Timing = timing };
    }

    /// <summary>Replaces point <paramref name="index"/>, keeping every timing key.</summary>
    public static Track Replace(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "replace");
        var points = new List<ControlPoint>(track.Points) { [index] = point };
        return track with { Points = points };
    }
```

Add beside the other private helpers:

```csharp
    private static void RequireKeyPerPoint(Track track)
    {
        var counts = new int[track.Points.Count];
        foreach (var key in track.Timing)
        {
            var position = (int)key.Position;
            if (key.Position != position || position < 0 || position >= counts.Length)
                throw new ArgumentException("timing keys between points are not supported yet");
            counts[position]++;
        }

        if (counts.Any(c => c is < 1 or > 2))
            throw new ArgumentException("timing keys between points are not supported yet");
    }
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Core/Tracks tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs
git commit -m "feat(core) insert, delete, move and replace control points"
```

---

### Task 2: Selection, edit operations and undo in `SessionState`

**Files:**
- Create: `src/CinematicCam.Core/Session/EditHistory.cs`
- Modify: `src/CinematicCam.Core/Session/SessionState.cs`
- Test: `tests/CinematicCam.Tests/Session/EditHistoryTests.cs`
- Test: `tests/CinematicCam.Tests/Session/SessionEditingTests.cs`

**Interfaces:**
- Consumes: Task 1's `TrackEditing.InsertAfter/Delete/Move/Replace`.
- Produces (namespace `CinematicCam.Core.Session`):

```csharp
public readonly record struct EditSnapshot(Track Track, int? Selected);

public sealed class EditHistory
{
    public const int Capacity = 100;
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public void Record(EditSnapshot before);
    public EditSnapshot? Undo(EditSnapshot current);
    public EditSnapshot? Redo(EditSnapshot current);
}
```

New members on `SessionState`:

```csharp
public int? Selected { get; }
public bool CanUndo { get; }            // editing and history has a step
public bool CanRedo { get; }
public void Select(int? index);         // editing only; out-of-range clears
public string? AddToEnd(ControlPoint point);
public string? AddAfterSelected(ControlPoint point);
public string? OverwriteSelected(ControlPoint point);
public string? ReplacePoint(int index, ControlPoint point);
public string? DeleteSelected();
public string? MovePoint(int from, int to);
public bool Undo();
public bool Redo();
```

Each `string?` return is null on success, or the refusal message. Rules:
- Not editing: every edit returns `"The track can only change while editing."`, and Undo/Redo/Select do nothing.
- `AddAfterSelected`, `OverwriteSelected` and `DeleteSelected` with nothing selected return `"Select a point first."`.
- **Selection** (editor:102-111):
  - `AddToEnd`, `OverwriteSelected` and `ReplacePoint` keep it.
  - `AddAfterSelected` selects the new point.
  - `DeleteSelected` clears it.
  - `MovePoint` keeps it on the same point.
  - `ChangeTrack` keeps it if it's still in range, otherwise clears it.
  - `Undo` and `Redo` restore it as it was at that step.
- **History:** every applied change records the state before it (track and selection) as one undo step, and clears redo. A refused change records nothing. A change that returns the same `Track` instance records nothing. Only the last 100 steps are kept.

- [ ] **Step 1: Write the failing tests**

`tests/CinematicCam.Tests/Session/EditHistoryTests.cs`:

```csharp
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Session;

public class EditHistoryTests
{
    private static EditSnapshot Snap(int? selected) => new(TrackEditing.Empty(), selected);

    [Fact]
    public void UndoReturnsTheRecordedStateAndRedoReturnsTheCurrentOne()
    {
        var history = new EditHistory();
        history.Record(Snap(1));

        Assert.Equal(1, history.Undo(Snap(2))!.Value.Selected);
        Assert.Equal(2, history.Redo(Snap(1))!.Value.Selected);
    }

    [Fact]
    public void RecordingClearsRedo()
    {
        var history = new EditHistory();
        history.Record(Snap(1));
        history.Undo(Snap(2));
        history.Record(Snap(3));

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo(Snap(4)));
    }

    [Fact]
    public void OnlyTheLastHundredStepsAreKept()
    {
        var history = new EditHistory();
        for (var i = 0; i < EditHistory.Capacity + 5; i++) history.Record(Snap(i));

        var undone = 0;
        while (history.Undo(Snap(null)) is not null) undone++;

        Assert.Equal(EditHistory.Capacity, undone);
    }

    [Fact]
    public void NothingToUndoOrRedoReturnsNull()
    {
        var history = new EditHistory();
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo(Snap(null)));
        Assert.Null(history.Redo(Snap(null)));
    }
}
```

`tests/CinematicCam.Tests/Session/SessionEditingTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Session;

public class SessionEditingTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing, three points at x = 0, 10, 20.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    [Fact]
    public void AddToEndKeepsTheSelection()
    {
        var state = Editing();
        state.Select(1);
        Assert.Null(state.AddToEnd(Point(30f)));
        Assert.Equal(1, state.Selected);

        state.Select(null);
        state.AddToEnd(Point(40f));
        Assert.Null(state.Selected);
    }

    [Fact]
    public void AddAfterSelectedSelectsTheNewPoint()
    {
        var state = Editing();
        state.Select(0);
        var point = Point(5f);

        Assert.Null(state.AddAfterSelected(point));
        Assert.Equal(1, state.Selected);
        Assert.Same(point, state.Track.Points[1]);

        state.AddAfterSelected(Point(7f));
        Assert.Equal(2, state.Selected);
        Assert.Equal(7f, state.Track.Points[2].Position.X);
    }

    [Fact]
    public void SelectedOnlyEditsNeedASelection()
    {
        var state = Editing();
        const string refused = "Select a point first.";
        Assert.Equal(refused, state.AddAfterSelected(Point(5f)));
        Assert.Equal(refused, state.OverwriteSelected(Point(5f)));
        Assert.Equal(refused, state.DeleteSelected());
    }

    [Fact]
    public void OverwriteSelectedKeepsSelectionAndTiming()
    {
        var state = Editing();
        state.Select(1);
        var timing = state.Track.Timing;

        Assert.Null(state.OverwriteSelected(Point(12f)));
        Assert.Equal(1, state.Selected);
        Assert.Equal(12f, state.Track.Points[1].Position.X);
        Assert.Same(timing, state.Track.Timing);
    }

    [Fact]
    public void ReplacePointKeepsTheSelection()
    {
        var state = Editing();
        state.Select(2);
        Assert.Null(state.ReplacePoint(0, Point(-1f)));
        Assert.Equal(2, state.Selected);
        Assert.Equal(-1f, state.Track.Points[0].Position.X);
    }

    [Fact]
    public void DeleteSelectedClearsTheSelection()
    {
        var state = Editing();
        state.Select(1);
        Assert.Null(state.DeleteSelected());
        Assert.Null(state.Selected);
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Theory]
    [InlineData(1, 1, 3, 3)] // the selected point itself moves
    [InlineData(1, 0, 2, 0)] // a point before it moves past it
    [InlineData(1, 3, 0, 2)] // a point after it moves before it
    [InlineData(1, 2, 3, 1)] // a move entirely after it
    public void MoveKeepsTheSelectionOnTheSamePoint(int selected, int from, int to, int expected)
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.Select(selected);
        var point = state.Track.Points[selected];

        Assert.Null(state.MovePoint(from, to));
        Assert.Equal(expected, state.Selected);
        Assert.Same(point, state.Track.Points[expected]);
    }

    [Fact]
    public void ChangeTrackClearsASelectionThatNoLongerExists()
    {
        var state = Editing();
        state.Select(2);
        state.ChangeTrack(_ => TrackEditing.Empty());
        Assert.Null(state.Selected);
    }

    [Fact]
    public void SelectOutOfRangeClears()
    {
        var state = Editing();
        state.Select(1);
        state.Select(5);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void UndoAndRedoRestoreTrackAndSelection()
    {
        var state = Editing();
        state.Select(0);
        var before = state.Track;
        state.AddAfterSelected(Point(5f));
        var after = state.Track;

        Assert.True(state.Undo());
        Assert.Same(before, state.Track);
        Assert.Equal(0, state.Selected);

        Assert.True(state.Redo());
        Assert.Same(after, state.Track);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void UndoRestoresTheSelectionAfterAnOverwrite()
    {
        var state = Editing();
        state.Select(1);
        state.OverwriteSelected(Point(12f));

        state.Undo();
        Assert.Equal(1, state.Selected);
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void ANewChangeClearsRedo()
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.Undo();
        state.AddToEnd(Point(40f));

        Assert.False(state.CanRedo);
        Assert.False(state.Redo());
    }

    [Fact]
    public void RefusedChangesRecordNothing()
    {
        var state = new SessionState();
        state.Edit();
        Assert.NotNull(state.DeleteSelected());
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void AChangeThatChangesNothingRecordsNothing()
    {
        var state = Editing();
        state.MovePoint(1, 1);

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void UndoRedoAndSelectWorkOnlyWhileEditing()
    {
        var state = Editing();
        state.Select(1);
        state.Play();

        Assert.False(state.CanUndo);
        Assert.False(state.Undo());
        state.Select(0);
        Assert.Equal(1, state.Selected);
        Assert.Equal("The track can only change while editing.", state.AddToEnd(Point(30f)));

        state.Edit();
        Assert.True(state.Undo());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: build error. `EditHistory`, `EditSnapshot` and the new `SessionState` members don't exist.

- [ ] **Step 3: Implement `EditHistory`**

`src/CinematicCam.Core/Session/EditHistory.cs`:

```csharp
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Session;

/// <summary>A track and the point selected with it, as one undo step restores them.</summary>
public readonly record struct EditSnapshot(Track Track, int? Selected);

/// <summary>Undo and redo stacks of edit snapshots, keeping the most recent <see cref="Capacity"/> undo steps.</summary>
public sealed class EditHistory
{
    public const int Capacity = 100;

    private readonly LinkedList<EditSnapshot> undo = new();
    private readonly Stack<EditSnapshot> redo = new();

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Records the state before a change and clears redo.</summary>
    public void Record(EditSnapshot before)
    {
        Push(before);
        redo.Clear();
    }

    /// <summary>The state to return to, keeping <paramref name="current"/> for redo; null when there is nothing to undo.</summary>
    public EditSnapshot? Undo(EditSnapshot current)
    {
        if (undo.Last is not { } last) return null;
        undo.RemoveLast();
        redo.Push(current);
        return last.Value;
    }

    /// <summary>The state to go forward to, keeping <paramref name="current"/> for undo; null when there is nothing to redo.</summary>
    public EditSnapshot? Redo(EditSnapshot current)
    {
        if (!redo.TryPop(out var next)) return null;
        Push(current);
        return next;
    }

    private void Push(EditSnapshot snapshot)
    {
        undo.AddLast(snapshot);
        if (undo.Count > Capacity) undo.RemoveFirst();
    }
}
```

- [ ] **Step 4: Implement the `SessionState` members**

Add the field `private readonly EditHistory history = new();` and the members below. Replace the body of the existing `ChangeTrack` with a call to `Apply`, keeping its doc comment and signature.

```csharp
    /// <summary>The selected point's index, or null.</summary>
    public int? Selected { get; private set; }

    /// <summary>True while editing with a step to undo.</summary>
    public bool CanUndo => Mode == CameraMode.Editing && history.CanUndo;

    /// <summary>True while editing with a step to redo.</summary>
    public bool CanRedo => Mode == CameraMode.Editing && history.CanRedo;

    /// <summary>Selects a point while editing; null or an index out of range clears the selection.</summary>
    public void Select(int? index)
    {
        if (Mode != CameraMode.Editing) return;
        Selected = index is { } i && i >= 0 && i < Track.Points.Count ? i : null;
    }

    public string? ChangeTrack(Func<Track, Track> change)
        => Apply(change, result => Selected is { } s && s < result.Points.Count ? s : null);

    /// <summary>Appends a point; the selection is unchanged.</summary>
    public string? AddToEnd(ControlPoint point)
        => Apply(t => TrackEditing.Append(t, point), _ => Selected);

    /// <summary>Inserts a point after the selected one and selects it.</summary>
    public string? AddAfterSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        var s = Selected!.Value;
        return Apply(t => TrackEditing.InsertAfter(t, s, point), _ => s + 1);
    }

    /// <summary>Replaces the selected point, keeping its timing and the selection.</summary>
    public string? OverwriteSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        return ReplacePoint(Selected!.Value, point);
    }

    /// <summary>Replaces point <paramref name="index"/>, keeping its timing and the selection.</summary>
    public string? ReplacePoint(int index, ControlPoint point)
        => Apply(t => TrackEditing.Replace(t, index, point), _ => Selected);

    /// <summary>Deletes the selected point and clears the selection.</summary>
    public string? DeleteSelected()
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        var s = Selected!.Value;
        return Apply(t => TrackEditing.Delete(t, s), _ => null);
    }

    /// <summary>Moves a point in the order; the selection stays on the same point.</summary>
    public string? MovePoint(int from, int to)
    {
        var selected = Selected;
        return Apply(t => TrackEditing.Move(t, from, to), _ => selected is { } s ? Follow(s, from, to) : null);
    }

    /// <summary>Restores the track and selection before the last change. Returns false if nothing was undone.</summary>
    public bool Undo() => Restore(Mode == CameraMode.Editing ? history.Undo(Current) : null);

    /// <summary>Re-applies the last undone change. Returns false if nothing was redone.</summary>
    public bool Redo() => Restore(Mode == CameraMode.Editing ? history.Redo(Current) : null);

    private EditSnapshot Current => new(Track, Selected);

    private string? Apply(Func<Track, Track> change, Func<Track, int?> selectAfter)
    {
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";

        try
        {
            var result = change(Track);
            if (ReferenceEquals(result, Track)) return null;

            _ = new TrackEvaluator(result);
            history.Record(Current);
            var selected = selectAfter(result);
            Track = result;
            Selected = selected;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private bool Restore(EditSnapshot? snapshot)
    {
        if (snapshot is not { } s) return false;
        Track = s.Track;
        Selected = s.Selected;
        return true;
    }

    private string? SelectionRefusal()
        => Mode != CameraMode.Editing ? "The track can only change while editing."
         : Selected is null ? "Select a point first." : null;

    private static int Follow(int selected, int from, int to)
    {
        if (selected == from) return to;
        if (from < selected && selected <= to) return selected - 1;
        if (to <= selected && selected < from) return selected + 1;
        return selected;
    }
```


- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed. The existing `SessionStateTests` still pass: `ChangeTrack` keeps its messages and refusal behaviour.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Core/Session tests/CinematicCam.Tests/Session
git commit -m "feat(core) add selection, point edits and undo to the session"
```

---

### Task 3: Seek and frame-at-time

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackPlayback.cs`
- Modify: `src/CinematicCam.Core/Tracks/Director.cs`
- Modify: `src/CinematicCam.Core/Session/SessionState.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackPlaybackTests.cs`
- Test: `tests/CinematicCam.Tests/Tracks/DirectorTests.cs`
- Test: `tests/CinematicCam.Tests/Session/SessionEditingTests.cs`

**Interfaces:**
- Produces:
  - `TrackPlayback.Seek(double time)`: `Once` clamps to [0, duration] and sets `IsFinished` to `Elapsed >= duration`. `Loop` wraps into [0, duration).
  - `Director.Seek(double time)`: seeks the current track shot while live. It does nothing when not live or for other shots, and doesn't change pause.
  - `SessionState.FrameAt(double time)` → `CameraState?`: the track's frame at that time, or null with no points. The evaluator is cached per `Track` instance.

Behaviour (editor:163-181): scrubbing while editing shows the frame at a time; dragging while live seeks, and seeking a finished `Once` shot back un-finishes it.

- [ ] **Step 1: Write the failing tests**

Add to `TrackPlaybackTests` (`StraightTrack(mode)` is 10 s long):

```csharp
    [Fact]
    public void SeekOnceClampsAndFinishesAtTheEnd()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Once));
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.Elapsed);
        Assert.False(playback.IsFinished);

        playback.Seek(99.0);
        Assert.Equal(10.0, playback.Elapsed, 5);
        Assert.True(playback.IsFinished);

        playback.Seek(-3.0);
        Assert.Equal(0.0, playback.Elapsed);
    }

    [Fact]
    public void SeekingAFinishedShotBackUnfinishesIt()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Once));
        playback.Advance(20f);
        Assert.True(playback.IsFinished);

        playback.Seek(3.0);
        Assert.False(playback.IsFinished);
        playback.Advance(1f);
        Assert.Equal(4.0, playback.Elapsed, 5);
    }

    [Fact]
    public void SeekLoopWraps()
    {
        var playback = new TrackPlayback(StraightTrack(PlaybackMode.Loop));
        playback.Seek(23.0);
        Assert.Equal(3.0, playback.Elapsed, 5);

        playback.Seek(-1.0);
        Assert.Equal(9.0, playback.Elapsed, 5);
    }
```

Add to `DirectorTests` (`StraightTrack()` is 10 s long, `Snap()` is a snap point):

```csharp
    [Fact]
    public void SeekMovesALiveTrackAndKeepsItsPause()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        director.Pause();

        director.Seek(6.0);
        Assert.Equal(6.0, director.Elapsed, 5);
        Assert.True(director.IsPaused);
    }

    [Fact]
    public void SeekDoesNothingOfflineOrForSnapShots()
    {
        var director = new Director();
        director.Seek(3.0);
        Assert.Equal(0.0, director.Elapsed);

        director.GoLive(new SnapShot(Snap()));
        director.Seek(3.0);
        Assert.Equal(0.0, director.Elapsed);
    }
```

Add to `SessionEditingTests`:

```csharp
    [Fact]
    public void FrameAtMatchesTheEvaluatorAndIsNullForAnEmptyTrack()
    {
        Assert.Null(new SessionState().FrameAt(1.0));

        var state = Editing();
        var expected = new TrackEvaluator(state.Track).Evaluate(2.5);
        Assert.Equal(expected, state.FrameAt(2.5));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: build error. `Seek` and `FrameAt` don't exist.

- [ ] **Step 3: Implement**

`TrackPlayback`, after `Restart`:

```csharp
    /// <summary>Jumps to <paramref name="time"/>: <c>Once</c> clamps to the track and finishes at its end, <c>Loop</c> wraps.</summary>
    public void Seek(double time)
    {
        var duration = _evaluator.Duration;
        if (_track.Playback == PlaybackMode.Loop)
        {
            Elapsed = duration > 0.0 ? ((time % duration) + duration) % duration : 0.0;
            return;
        }

        Elapsed = Math.Clamp(time, 0.0, duration);
        IsFinished = Elapsed >= duration;
    }
```

`Director`, after `GoOffline`:

```csharp
    /// <summary>Jumps the live track shot to <paramref name="time"/>, keeping pause. No effect otherwise.</summary>
    public void Seek(double time)
    {
        if (IsLive) _playback?.Seek(time);
    }
```

`SessionState`: add the fields `private Track? evaluatedTrack;` and `private TrackEvaluator? evaluator;`, and:

```csharp
    /// <summary>The track's frame at <paramref name="time"/> seconds, or null with no points.</summary>
    public CameraState? FrameAt(double time)
    {
        if (Track.Points.Count == 0) return null;
        if (!ReferenceEquals(evaluatedTrack, Track))
        {
            evaluator = new TrackEvaluator(Track);
            evaluatedTrack = Track;
        }

        return evaluator!.Evaluate(time);
    }
```

Add `using CinematicCam.Core.Camera;` to `SessionState.cs`.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core tests/CinematicCam.Tests
git commit -m "feat(core) seek playback and evaluate a frame at any time"
```

---

### Task 4: Editor geometry: projection, hit-testing, pose matrices

**Files:**
- Create: `src/CinematicCam.Core/Camera/ScreenProjection.cs`
- Create: `src/CinematicCam.Core/Editing/MarkerHitTest.cs`
- Create: `src/CinematicCam.Core/Editing/PoseMatrix.cs`
- Test: `tests/CinematicCam.Tests/Camera/ScreenProjectionTests.cs`
- Test: `tests/CinematicCam.Tests/Editing/MarkerHitTestTests.cs`
- Test: `tests/CinematicCam.Tests/Editing/PoseMatrixTests.cs`

**Interfaces:**
- Produces:
  - `ScreenProjection.Project(Vector3 world, Matrix4x4 viewProjection, Vector2 viewport)` → `Vector2?`: pixel position with the origin top-left, or null when the point is behind the camera (clip W ≤ 0). This is the row-vector convention the game uses (`Vector4.Transform(world, view * projection)`).
  - `MarkerHitTest.Nearest(IReadOnlyList<Vector2?> markers, Vector2 cursor, float radius)` → `int?`: the index of the nearest non-null marker within `radius` pixels.
  - `PoseMatrix.From(Vector3 position, float yaw, float pitch, float roll)` → `Matrix4x4`. Rows: X = right, Y = up (with roll), Z = backward (−forward), and the 4th row is the translation. `PoseMatrix.ToPose(Matrix4x4)` → `(Vector3 Position, float Yaw, float Pitch, float Roll)`. It is the exact inverse, with pitch clamped to `TrackAim.PitchLimit`. Rows may be scaled; they are normalised.

The yaw/pitch convention is `FreeCamMotion`'s. Roll is `CameraOrientation.UpFor`'s: positive rolls right, about the view direction.

- [ ] **Step 1: Write the failing tests**

`tests/CinematicCam.Tests/Camera/ScreenProjectionTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Camera;
using Xunit;

namespace CinematicCam.Tests.Camera;

public class ScreenProjectionTests
{
    private static readonly Vector2 Viewport = new(1920f, 1080f);

    // Camera at z = 10 looking at the origin.
    private static Matrix4x4 ViewProjection()
        => Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 10f), Vector3.Zero, Vector3.UnitY)
         * Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 0.1f, 1000f);

    [Fact]
    public void APointStraightAheadLandsInTheCentre()
    {
        var screen = ScreenProjection.Project(Vector3.Zero, ViewProjection(), Viewport);
        Assert.NotNull(screen);
        Assert.Equal(960f, screen!.Value.X, 2);
        Assert.Equal(540f, screen.Value.Y, 2);
    }

    [Fact]
    public void RightIsRightAndUpIsUpOnScreen()
    {
        var right = ScreenProjection.Project(Vector3.UnitX, ViewProjection(), Viewport)!.Value;
        var up = ScreenProjection.Project(Vector3.UnitY, ViewProjection(), Viewport)!.Value;
        Assert.True(right.X > 960f);
        Assert.True(up.Y < 540f);
    }

    [Fact]
    public void APointBehindTheCameraIsNull()
        => Assert.Null(ScreenProjection.Project(new Vector3(0f, 0f, 20f), ViewProjection(), Viewport));
}
```

`tests/CinematicCam.Tests/Editing/MarkerHitTestTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class MarkerHitTestTests
{
    [Fact]
    public void TheNearestMarkerWithinTheRadiusWins()
    {
        var markers = new Vector2?[] { new(100f, 100f), new(106f, 100f), new(300f, 300f) };
        Assert.Equal(1, MarkerHitTest.Nearest(markers, new Vector2(104f, 100f), 10f));
    }

    [Fact]
    public void NothingWithinTheRadiusIsNull()
    {
        var markers = new Vector2?[] { new(100f, 100f) };
        Assert.Null(MarkerHitTest.Nearest(markers, new Vector2(120f, 100f), 10f));
    }

    [Fact]
    public void OffScreenMarkersAreSkipped()
    {
        var markers = new Vector2?[] { null, new(100f, 100f) };
        Assert.Equal(1, MarkerHitTest.Nearest(markers, new Vector2(100f, 100f), 10f));
        Assert.Null(MarkerHitTest.Nearest(Array.Empty<Vector2?>(), Vector2.Zero, 10f));
    }
}
```

`tests/CinematicCam.Tests/Editing/PoseMatrixTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class PoseMatrixTests
{
    [Fact]
    public void ALevelUnrolledPoseFacingNorthIsTheIdentityRotation()
    {
        var m = PoseMatrix.From(new Vector3(1f, 2f, 3f), 0f, 0f, 0f);
        Assert.Equal(1f, m.M11, 5);
        Assert.Equal(1f, m.M22, 5);
        Assert.Equal(1f, m.M33, 5);
        Assert.Equal(0f, m.M12, 5);
        Assert.Equal(0f, m.M23, 5);
        Assert.Equal(new Vector3(1f, 2f, 3f), new Vector3(m.M41, m.M42, m.M43));
    }

    [Theory]
    [InlineData(0.7f, 0.3f, 0.4f)]
    [InlineData(-2.5f, -0.9f, -1.2f)]
    [InlineData(3.0f, 1.2f, 2.9f)]
    [InlineData(0f, 0f, 0f)]
    public void ToPoseInvertsFrom(float yaw, float pitch, float roll)
    {
        var position = new Vector3(-5f, 7f, 11f);
        var (p, y, pi, r) = PoseMatrix.ToPose(PoseMatrix.From(position, yaw, pitch, roll));

        Assert.Equal(position, p);
        Assert.Equal(yaw, y, 4);
        Assert.Equal(pitch, pi, 4);
        Assert.Equal(roll, r, 4);
    }

    [Fact]
    public void ToPoseIgnoresScaleAndClampsPitch()
    {
        var scaled = Matrix4x4.CreateScale(3f) * PoseMatrix.From(Vector3.Zero, 0.5f, 0.2f, 0.1f);
        var (_, yaw, pitch, roll) = PoseMatrix.ToPose(scaled);
        Assert.Equal(0.5f, yaw, 4);
        Assert.Equal(0.2f, pitch, 4);
        Assert.Equal(0.1f, roll, 4);

        var straightUp = PoseMatrix.From(Vector3.Zero, 0f, 1.5707963f, 0f);
        Assert.True(PoseMatrix.ToPose(straightUp).Pitch <= TrackAim.PitchLimit);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: build error. The three types don't exist.

- [ ] **Step 3: Implement**

`src/CinematicCam.Core/Camera/ScreenProjection.cs`:

```csharp
using System.Numerics;

namespace CinematicCam.Core.Camera;

/// <summary>Projects world points to screen pixels with a row-vector view-projection matrix, as the game does.</summary>
public static class ScreenProjection
{
    /// <summary>Pixel position of <paramref name="world"/> with the origin top-left, or null when it is behind the camera.</summary>
    public static Vector2? Project(Vector3 world, Matrix4x4 viewProjection, Vector2 viewport)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= float.Epsilon) return null;

        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        return new Vector2((x + 1f) * viewport.X * 0.5f, (1f - y) * viewport.Y * 0.5f);
    }
}
```

`src/CinematicCam.Core/Editing/MarkerHitTest.cs`:

```csharp
using System.Numerics;

namespace CinematicCam.Core.Editing;

/// <summary>Finds which projected marker a click landed on.</summary>
public static class MarkerHitTest
{
    /// <summary>Index of the marker nearest <paramref name="cursor"/> within <paramref name="radius"/> pixels, or null. Null markers are off screen.</summary>
    public static int? Nearest(IReadOnlyList<Vector2?> markers, Vector2 cursor, float radius)
    {
        int? best = null;
        var bestDistance = radius * radius;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i] is not { } marker) continue;
            var distance = Vector2.DistanceSquared(marker, cursor);
            if (distance > bestDistance) continue;
            best = i;
            bestDistance = distance;
        }

        return best;
    }
}
```

`src/CinematicCam.Core/Editing/PoseMatrix.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>Converts a camera pose to and from the matrix a gizmo edits: rows right, up, backward, then translation.</summary>
public static class PoseMatrix
{
    public static Matrix4x4 From(Vector3 position, float yaw, float pitch, float roll)
    {
        var forward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch));
        var up = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward, roll));
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        var back = -forward;

        return new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            back.X, back.Y, back.Z, 0f,
            position.X, position.Y, position.Z, 1f);
    }

    public static (Vector3 Position, float Yaw, float Pitch, float Roll) ToPose(Matrix4x4 matrix)
    {
        var position = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        var forward = -Vector3.Normalize(new Vector3(matrix.M31, matrix.M32, matrix.M33));
        var up = Vector3.Normalize(new Vector3(matrix.M21, matrix.M22, matrix.M23));

        var (yaw, pitch) = TrackAim.FromDirection(forward);
        var level = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward));
        var roll = MathF.Atan2(Vector3.Dot(Vector3.Cross(level, up), forward), Vector3.Dot(level, up));

        return (position, yaw, Math.Clamp(pitch, -TrackAim.PitchLimit, TrackAim.PitchLimit), roll);
    }
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed. If `ToPoseInvertsFrom` fails only on roll's sign, the implementation disagrees with `CameraOrientation.UpFor`. Report it; don't flip signs to force a pass.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core tests/CinematicCam.Tests
git commit -m "feat(core) add screen projection, marker hit-testing and pose matrices"
```

---

### Task 5: Probe 1: projection and gizmo convention

**Files:**
- Create: `src/CinematicCam.Plugin/Probes/GizmoProbe.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs` (services, `probe` verb, drawing)
- Modify: `src/CinematicCam.Plugin/Session/CameraSession.cs` (expose the last frame)

**Interfaces:**
- Consumes: `ScreenProjection.Project` (Task 4).
- Produces: `/ccam probe gizmo [game|ours]` toggles the probe. `CameraSession.LastFrame` (`CameraState?`) exposes the frame last written (the existing `lastFrame` field). New plugin services: `Plugin.GameGui` (`IGameGui`) and `Plugin.Objects` (`IObjectTable`).

**What it answers (editor:126-131, 215-217):**
1. Which view matrix projects world points onto the right pixels while we own the camera: the game's `SceneCamera.ViewMatrix`, or one we build from the frame we wrote?
2. With BDTH's projection fix-up, does ImGuizmo drag along the axis it draws?

The probe draws three circles at the local player's feet, a place you can see:
- **red, large:** projected with the game's `ViewMatrix`;
- **green, medium:** projected with our own view, `Matrix4x4.CreateLookAt(frame.Position, frame.LookAt, UpFor(...))`;
- **blue, small:** `IGameGui.WorldToScreen`.

It draws a translate gizmo, in world axes, 2 yalms above the feet, using the game's view or ours according to the argument. Once a second it logs the camera position recovered from the game's `ViewMatrix` against the position we wrote. After each gizmo drag it logs the translation change per axis.

- [ ] **Step 1: Expose the last frame and add services**

In `CameraSession`, add:

```csharp
    /// <summary>The frame last written to the camera, or null when the game has it.</summary>
    public CameraState? LastFrame => lastFrame;
```

In `Plugin`, add the services:

```csharp
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
```

- [ ] **Step 2: Write the probe**

`src/CinematicCam.Plugin/Probes/GizmoProbe.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Plugin.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface.Utility;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 1: which view matrix projects correctly, and whether the gizmo drags along the axis it draws.</summary>
internal sealed unsafe class GizmoProbe
{
    private const uint Red = 0xFF0000FF;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFFFF0000;

    private bool enabled;
    private bool useOurView;
    private Matrix4x4 gizmo;
    private bool placed;
    private bool wasUsing;
    private Vector3 dragStart;
    private long lastLog;

    /// <summary>Toggles the probe; "ours" drives the gizmo with our own view matrix.</summary>
    public void Toggle(string argument)
    {
        useOurView = argument == "ours";
        enabled = !enabled || argument.Length > 0;
        placed = false;
        Plugin.Log.Information("[probe] gizmo {State}, view {View}", enabled ? "on" : "off", useOurView ? "ours" : "game");
    }

    /// <summary>Draws the markers and gizmo. Call from UiBuilder.Draw.</summary>
    public void Draw(CameraState? frame)
    {
        if (!enabled || Plugin.Objects.LocalPlayer is not { } player) return;
        if (!CameraAccess.TryGetWorldCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        var render = scene->RenderCamera;
        if (render == null) return;

        var gameView = scene->ViewMatrix;
        var projection = render->ProjectionMatrix;
        var ourView = frame is { } f
            ? Matrix4x4.CreateLookAt(f.Position, f.LookAt, CameraOrientation.UpFor(f.Position, f.LookAt, f.Roll))
            : gameView;

        var viewport = ImGuiHelpers.MainViewport;
        var feet = player.Position;
        var list = ImGui.GetBackgroundDrawList();
        Mark(list, ScreenProjection.Project(feet, gameView * projection, viewport.Size), viewport.Pos, Red, 16f);
        Mark(list, ScreenProjection.Project(feet, ourView * projection, viewport.Size), viewport.Pos, Green, 11f);
        if (Plugin.GameGui.WorldToScreen(feet, out var gui)) list.AddCircle(gui, 6f, Blue, 0, 2f);

        LogCameraPositions(gameView, frame);
        DrawGizmo(useOurView ? ourView : gameView, projection, render->NearPlane, render->FarPlane, feet);
    }

    private static void Mark(ImDrawListPtr list, Vector2? screen, Vector2 origin, uint colour, float radius)
    {
        if (screen is { } s) list.AddCircle(origin + s, radius, colour, 0, 2f);
    }

    private void LogCameraPositions(Matrix4x4 gameView, CameraState? frame)
    {
        var now = Environment.TickCount64;
        if (now - lastLog < 1000) return;
        lastLog = now;

        var fromGame = Matrix4x4.Invert(gameView, out var inverse) ? inverse.Translation : Vector3.Zero;
        Plugin.Log.Information("[probe] camera from game view {Game}, written {Ours}, gap {Gap:0.000}",
            fromGame, frame?.Position, frame is { } f ? Vector3.Distance(fromGame, f.Position) : -1f);
    }

    private void DrawGizmo(Matrix4x4 view, Matrix4x4 projection, float near, float far, Vector3 feet)
    {
        if (!placed)
        {
            gizmo = Matrix4x4.CreateTranslation(feet + new Vector3(0f, 2f, 0f));
            placed = true;
        }

        // BDTHPlugin's fix-up (reference only): re-express the game's reversed-Z, infinite-far projection for ImGuizmo.
        var clip = far / (far - near);
        projection.M43 = -(clip * near);
        projection.M33 = -((far + near) / (far - near));
        view.M44 = 1f;

        var viewport = ImGuiHelpers.MainViewport;
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoSavedSettings;

        if (ImGui.Begin("##ccam-probe-gizmo", flags))
        {
            ImGuizmo.BeginFrame();
            ImGuizmo.SetDrawlist();
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);

            var v = &view.M11;
            var p = &projection.M11;
            fixed (float* m = &gizmo.M11)
                ImGuizmo.Manipulate(v, p, ImGuizmoOperation.Translate, ImGuizmoMode.World, m, null, null, null, null);

            LogDrag();
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void LogDrag()
    {
        var usingNow = ImGuizmo.IsUsing();
        if (usingNow && !wasUsing) dragStart = gizmo.Translation;
        if (!usingNow && wasUsing)
        {
            var delta = gizmo.Translation - dragStart;
            Plugin.Log.Information("[probe] gizmo drag moved x {X:0.000} y {Y:0.000} z {Z:0.000}", delta.X, delta.Y, delta.Z);
        }

        wasUsing = usingNow;
    }
}
```

If the compiler rejects an ImGuizmo or ImGui call, look up the binding source under `~/code/Dalamud/imgui/` and use the matching overload. Report every substitution you made.

- [ ] **Step 3: Wire it in**

In `Plugin`, add the field `private readonly GizmoProbe gizmoProbe = new();` and the verb, and draw it:

```csharp
            case "probe":
                OnProbe(args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray());
                break;
```

```csharp
    /// <summary>Temporary in-game probes for phase 2c-1; removed once they have answered.</summary>
    private void OnProbe(string[] words)
    {
        switch (words.FirstOrDefault())
        {
            case "gizmo":
                gizmoProbe.Toggle(words.ElementAtOrDefault(1) ?? "");
                break;
            default:
                Log.Information("[probe] usage: /ccam probe gizmo [game|ours]");
                break;
        }
    }
```

In `OnDraw`, before `windows.Draw();`:

```csharp
        gizmoProbe.Draw(Session.LastFrame);
```

Change the help message to `"/ccam opens the test window | release | probe gizmo|aim|input"`. Tasks 6 and 7 add `aim` and `input` to the same switch.

- [ ] **Step 4: Build and test**

Run: `./build.sh` → 0 errors. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin
git commit -m "feat(probe) draw projection markers and a test gizmo"
```

**In-game check (the user's).** Stand still somewhere open. Press **Edit**, then run `/ccam probe gizmo`.
1. Fly around the character, near and far, at different heights. For each colour, note whether its circle stays on the character's feet: red is the game's view, green is ours, blue is Dalamud's.
2. The log prints `[probe] camera from game view … written … gap …` every second. Note whether the gap is near 0 or large.
3. Drag the gizmo's red arrow a short way. **Pass:** the handle moves along the red arrow as drawn, and the log's `gizmo drag moved` shows only `x` non-zero. Repeat for green (`y`) and blue (`z`).
4. If step 3 fails, run `/ccam probe gizmo ours` and repeat it.
5. The camera may also turn while you drag. Ignore that here; probe 3 covers it.

Report which circles held and the gizmo results.

---

### Task 6: Probe 2: are `DirH` and `DirV` safe to write?

**Files:**
- Modify: `src/CinematicCam.Plugin/Game/CameraAccess.cs`
- Create: `src/CinematicCam.Plugin/Probes/AimProbe.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Produces: `CameraAccess.WriteAngles(float yaw, float pitch)` sets `DirH`/`DirV` in radians. In this plan only the probe calls it. `/ccam probe aim <yawDegrees> <pitchDegrees>` writes the angles once, in editing mode only, and logs the values before, straight after, and on the next two framework updates.

Source findings (ClientStructs at b53cdf38, `FFXIV/Client/Game/Camera.cs`): `DirH` (0x140) and `DirV` (0x144) are the live orientation fields. The saved camera defaults are separate `ConfigOption` entries, handled through `SaveConfigOptions`/`LoadConfigOptions` (vfuncs 24/25), and no known function ties the save path to `DirH`/`DirV`. That makes them *likely* runtime state. The probe is the proof.

- [ ] **Step 1: Add the write**

In `CameraAccess`, after `ReadAngles`:

```csharp
    /// <summary>Sets the world camera's yaw and pitch in radians, as DirH and DirV. Probe 2 decides whether this is safe to use.</summary>
    public static void WriteAngles(float yaw, float pitch)
    {
        if (!TryGetWorldCamera(out var camera)) return;
        camera->DirH = yaw;
        camera->DirV = pitch;
    }
```

- [ ] **Step 2: Write the probe**

`src/CinematicCam.Plugin/Probes/AimProbe.cs`:

```csharp
using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 2: writes DirH and DirV once and logs whether the game keeps them.</summary>
internal sealed class AimProbe
{
    private int framesToLog;

    /// <summary>Writes the angles in degrees while editing, then logs them for two frames.</summary>
    public void Run(CameraMode mode, string[] words)
    {
        if (mode != CameraMode.Editing) { Plugin.Log.Error("[probe] aim needs editing mode."); return; }
        if (words.Length < 2 || !float.TryParse(words[0], out var yawDeg) || !float.TryParse(words[1], out var pitchDeg))
        {
            Plugin.Log.Error("[probe] usage: /ccam probe aim <yawDegrees> <pitchDegrees>");
            return;
        }

        Plugin.Log.Information("[probe] aim before {Angles}", CameraAccess.ReadAngles());
        CameraAccess.WriteAngles(yawDeg * MathF.PI / 180f, pitchDeg * MathF.PI / 180f);
        Plugin.Log.Information("[probe] aim after write {Angles}", CameraAccess.ReadAngles());
        framesToLog = 2;
    }

    /// <summary>Logs the angles on the frames after a write. Call from Framework.Update.</summary>
    public void Update()
    {
        if (framesToLog == 0) return;
        framesToLog--;
        Plugin.Log.Information("[probe] aim next frame {Angles}", CameraAccess.ReadAngles());
    }
}
```

- [ ] **Step 3: Wire it in**

In `Plugin`: add `private readonly AimProbe aimProbe = new();`, add `aimProbe.Update();` at the end of `OnFrameworkUpdate`'s first block (after the `Camera.Faulted` check), and add to `OnProbe`:

```csharp
            case "aim":
                aimProbe.Run(Session.Mode, words.Skip(1).ToArray());
                break;
```

Update the usage line to `"[probe] usage: /ccam probe gizmo [game|ours] | aim <yaw> <pitch> | input"`.

- [ ] **Step 4: Build and test**

Run: `./build.sh` → 0 errors. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin
git commit -m "feat(probe) write camera yaw and pitch on command"
```

**In-game check (the user's and the controller's together).**
1. Log in on a **test character**. Tell the controller, and it copies `~/Library/Application Support/XIV on Mac/ffxivConfig/` into the scratchpad as the "before" snapshot.
2. Press **Edit**, then run `/ccam probe aim 123 -20`. **Pass:** the view swings to face a new direction and stays there. The log's `aim after write` and `aim next frame` lines both show about 2.147 and −0.349.
3. Mouse-look still turns the view afterwards.
4. **Release**. Teleport to another zone. Open Character Configuration → Control Settings, change nothing, and press OK. Log out to the title screen, then log back in.
5. Tell the controller. It diffs `ffxivConfig/` against the snapshot, and reviews every changed line with you.
   - **Pass:** no camera-related setting changed (distance, angles, zoom, or anything under the camera settings).
   - **Also pass:** after logging in, the camera behaves normally.

---

### Task 7: Probe 3: hiding our keys and clicks from the game

**Files:**
- Create: `src/CinematicCam.Plugin/Probes/InputProbe.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`

**Interfaces:**
- Produces: `/ccam probe input` toggles the probe. It is active in editing mode only.

**What it answers (editor:204-207, 221-223):**
1. Reading C, R, backtick, Z and Y from ImGui's key state works while the game has focus, and Alt (Option under Wine) and Ctrl read correctly.
2. Clearing those keys in the game's key buffer, with `Plugin.KeyState[vk] = false` every framework update while they're held, stops the game acting on them. Z and Y are only cleared while Ctrl is held.
3. How the game's buffer behaves after a clear while a key is physically held. The probe logs the raw value before clearing for the first 40 frames of a C hold.
4. Letting a click-through overlay take the mouse while the cursor is over a spot stops the game's own click there. Dalamud swallows mouse clicks while ImGui wants the mouse.

Clearing is skipped while typing in chat (`RaptureAtkModule.IsTextInputActive()`), so chat keeps working.

- [ ] **Step 1: Write the probe**

`src/CinematicCam.Plugin/Probes/InputProbe.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 3: reads our keys from ImGui, hides them from the game, and captures clicks over a test spot.</summary>
internal sealed unsafe class InputProbe
{
    private const float SpotRadius = 40f;

    private static readonly (ImGuiKey Key, VirtualKey Vk, bool NeedsCtrl)[] Keys =
    [
        (ImGuiKey.C, VirtualKey.C, false),
        (ImGuiKey.R, VirtualKey.R, false),
        (ImGuiKey.GraveAccent, VirtualKey.OEM_3, false),
        (ImGuiKey.Z, VirtualKey.Z, true),
        (ImGuiKey.Y, VirtualKey.Y, true),
    ];

    private readonly bool[] held = new bool[Keys.Length];
    private bool enabled;
    private bool ctrl;
    private int rawFramesLogged;
    private bool overSpot;

    public void Toggle()
    {
        enabled = !enabled;
        Plugin.Log.Information("[probe] input {State}", enabled ? "on" : "off");
    }

    /// <summary>Reads keys from ImGui and draws the click spot. Call from UiBuilder.Draw.</summary>
    public void Draw(CameraMode mode)
    {
        if (!enabled || mode != CameraMode.Editing) { Array.Clear(held); return; }

        var io = ImGui.GetIO();
        ctrl = io.KeyCtrl;
        for (var i = 0; i < Keys.Length; i++)
        {
            var down = ImGui.IsKeyDown(Keys[i].Key);
            if (down && !held[i])
                Plugin.Log.Information("[probe] key {Key} down, alt {Alt}, ctrl {Ctrl}, shift {Shift}", Keys[i].Key, io.KeyAlt, io.KeyCtrl, io.KeyShift);
            held[i] = down;
        }

        DrawSpot(io);
    }

    /// <summary>Clears our held keys from the game's buffer. Call from Framework.Update.</summary>
    public void Update(CameraMode mode)
    {
        if (!enabled || mode != CameraMode.Editing || IsTyping()) return;

        for (var i = 0; i < Keys.Length; i++)
        {
            if (!held[i] || (Keys[i].NeedsCtrl && !ctrl)) continue;

            if (Keys[i].Vk == VirtualKey.C && rawFramesLogged < 40)
            {
                Plugin.Log.Information("[probe] C raw before clear {Raw}", Plugin.KeyState[VirtualKey.C]);
                rawFramesLogged++;
            }

            Plugin.KeyState[Keys[i].Vk] = false;
        }

        if (!held[0]) rawFramesLogged = 0;
    }

    private void DrawSpot(ImGuiIOPtr io)
    {
        var viewport = ImGuiHelpers.MainViewport;
        var centre = viewport.Pos + (viewport.Size / 2f);
        overSpot = Vector2.Distance(io.MousePos, centre) <= SpotRadius;

        var flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove;
        if (!overSpot) flags |= ImGuiWindowFlags.NoInputs;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        if (ImGui.Begin("##ccam-probe-input", flags))
        {
            ImGui.GetWindowDrawList().AddCircle(centre, SpotRadius, overSpot ? 0xFF00FFFF : 0xFFFFFFFF, 0, 2f);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                Plugin.Log.Information("[probe] click, over spot {Over}, imgui wants mouse {Want}", overSpot, io.WantCaptureMouse);
        }

        ImGui.End();
    }

    private static bool IsTyping()
    {
        var module = RaptureAtkModule.Instance();
        return module != null && module->IsTextInputActive();
    }
}
```

If the compiler rejects an ImGui call, find the matching overload in `~/code/Dalamud/imgui/Dalamud.Bindings.ImGui` and report the substitution.

- [ ] **Step 2: Wire it in**

In `Plugin`: add `private readonly InputProbe inputProbe = new();`. Call `inputProbe.Update(Session.Mode);` in `OnFrameworkUpdate` right after `aimProbe.Update();`, and `inputProbe.Draw(Session.Mode);` in `OnDraw` before `windows.Draw();`. Add to `OnProbe`:

```csharp
            case "input":
                inputProbe.Toggle();
                break;
```

- [ ] **Step 3: Build and test**

Run: `./build.sh` → 0 errors. Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj` → 0 failed.

- [ ] **Step 4: Commit**

```bash
git add src/CinematicCam.Plugin
git commit -m "feat(probe) hide editor keys and clicks from the game"
```

**In-game check (the user's).** Press **Edit**, then run `/ccam probe input`. A white circle appears at the centre of the screen.
1. Press **C**. **Pass:** the Character window does not open, and the log shows `key C down`.
2. Hold **C** for two seconds. The log shows up to 40 `C raw before clear` lines. Report the pattern (all `True`, or `True` then `False`).
3. Press **R**, **backtick**, **Ctrl+Z** and **Ctrl+Y**. **Pass:** none of them does its usual game action, and each is logged with the right `ctrl` value.
4. Press **Alt + backtick**. **Pass:** logged with `alt True`.
5. Open chat and type `cr` and a backtick. **Pass:** the characters appear in chat, and the Character window does not open.
6. Fly so a targetable NPC or object sits inside the circle, then left-click it inside the circle. **Pass:** it is **not** targeted, and the log shows `over spot True, imgui wants mouse True`. Then click it outside the circle. **Pass:** it **is** targeted.
7. Run `/ccam probe input` again to switch the probe off. C opens the Character window again.

---

## After Part 1

The controller records each probe's result in the editor spec: which view matrix the overlay uses, whether aim can be written, and how keys and clicks are hidden. Part 2's plan is then written against those results. Part 2 covers fly-down on C, the key bindings, the overlay and selection, the gizmo, both windows, scrub and jumps, undo, and removing the probes.

Carried into Part 2 from Part 1's final review:
- `SessionState` needs a `Duration` for the scrub bar.
- `CameraSession` passes through `Selected`, the edit methods, Undo/Redo and `FrameAt`.
- `CapturePoint` goes through `AddToEnd`.
- The path polyline is clipped at the near plane: `ScreenProjection` only rejects W ≤ 0, so points just in front of the camera project far off screen.
- The gizmo commits only when the pose changed. `Replace` already ignores an unchanged point.
- When those files are next touched: add tests for `RequireKeyPerPoint` rejecting 0-key and >2-key points, and move `SessionState`'s private fields to the top.
