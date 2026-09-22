# Phase 3.b Scenes and Tracks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A scene holds several named tracks, one edited at a time, with a Hierarchy compartment in the main window and every shown track drawn in the world.

**Architecture:** Five tasks in three waves:
- **Wave 1, in parallel:** Task 1 adds `Track.Id`/`Name`, the `Scene` record and pure `SceneEditing`. Task 2 adds hit-testing across tracks (`TrackMarker`, `TrackMarkerHitTest`).
- **Wave 2:** Task 3 moves `SessionState` onto the scene (edited track, scene-wide undo, scene edits) and adds the `CameraSession` wrappers.
- **Wave 3, in parallel:** Task 4 draws every shown track and switches tracks on marker clicks. Task 5 builds the Hierarchy compartment in the main window.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`.

**Spec:** `docs/superpowers/specs/2026-09-22-scenes-and-tracks-design.md`. Requirements background: `BRAINSPLAT.md`.

## Global Constraints

- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only. Never create a `Vista.Plugin.Camera` namespace.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only, with no `until … sleep` loops.
- Doc comments are one line.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`. Check `git status` after committing. Do not push.
- Refusals are returned as a `string?` reason (null means done) and logged by the UI, as today. Core signals invalid edits by throwing `ArgumentException`, which the session turns into a refusal.
- Everything that changes the scene or the edited track is disabled while live, paused included.
- In a row loop that can edit the scene (delete, reorder), read every row from a snapshot taken before the loop.
- In a self-sizing window, never align to the window's own width. Measure icon buttons with `IconButton.Width`.
- No backwards compatibility: the plugin is unreleased and nothing is saved yet.

## Technical rulings (the cost if wrong is in brackets)

- **`Track` gets `Id` and `Name` as its first two positional members**, and `TrackEditing.Empty` makes a fresh `Guid` each call. Only `Empty` constructs tracks. [If wrong: reorder the record's members.]
- **Clear track keeps the Id and Name and resets everything else,** as `Empty()` does today, including aim. `TrackEditing.Clear` does it. [If wrong: carry more fields across in `Clear`.]
- **A track change that returns a track with a different Id is refused** ("A change cannot replace the track."), checked before the undo step is recorded. [If wrong: none; this only guards against misuse.]
- **Redo returns to the track that was being edited when Undo was pressed,** because the redo snapshot is taken then. Undo returns to the track the change was made in, as the spec says. [If wrong: store the changed track's Id in redo snapshots too.]
- **Clicking the edited track's own row also flies the camera to its first point.** [If wrong: skip the jump when the Id is already edited.]
- **The Hierarchy is shown by default.** [If wrong: flip `showHierarchy`'s initial value.]
- **The window class keeps its name `TrackEditorWindow`;** its title is already "Vista". [If wrong: rename the class.]
- **Marker clicks index into the frame's flat marker list** (`ClickSelection` stays int-based). The list is rebuilt each frame in the same order, and nothing changes the scene between a press and its release. [If wrong: carry a `(Guid, int)` through `ClickSelection`.]

---

### Task 1: Track identity, the Scene and scene edits

**Files:**
- Modify: `src/Vista.Core/Tracks/Track.cs`, `src/Vista.Core/Tracks/TrackEditing.cs` (`Empty`; add `Clear`)
- Create: `src/Vista.Core/Scenes/Scene.cs`, `src/Vista.Core/Scenes/SceneEditing.cs`
- Test: `tests/Vista.Tests/Scenes/SceneEditingTests.cs`, `tests/Vista.Tests/Tracks/TrackEditingTests.cs`

**Interfaces:**
- Produces:
  - `record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop)`
  - `TrackEditing.Empty(AimMode aim = AimMode.AimKeys, string name = "Track 1")`: a new Id each call.
  - `TrackEditing.Clear(Track track)`: `Empty()` with the track's Id and Name.
  - `namespace Vista.Core.Scenes`: `record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden)`.
  - `static class SceneEditing`, all pure, throwing `ArgumentException` on a refusal:
    - `Scene New()`
    - `int IndexOf(Scene scene, Guid id)` (−1 if absent)
    - `Track Get(Scene scene, Guid id)`
    - `Scene Replace(Scene scene, Track track)` (by `track.Id`)
    - `(Scene Scene, Guid Added) Add(Scene scene)`
    - `Scene Rename(Scene scene, Guid id, string name)`
    - `(Scene Scene, Guid Copy) Duplicate(Scene scene, Guid id)`
    - `(Scene Scene, Guid Next) Delete(Scene scene, Guid id)`
    - `Scene Move(Scene scene, int from, int to)`
    - `Scene SetHidden(Scene scene, Guid id, bool hidden)`

- [ ] **Step 1: Write the failing tests**

Add to `TrackEditingTests.cs`:

```csharp
    [Fact]
    public void EmptyGivesEachTrackANewIdAndTheNameAsked()
    {
        var first = TrackEditing.Empty();
        var second = TrackEditing.Empty(name: "Crane");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Track 1", first.Name);
        Assert.Equal("Crane", second.Name);
    }

    [Fact]
    public void ClearEmptiesTheTrackButKeepsItsIdAndName()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.PathTangent, "Crane"), Point(1f, 2f, 3f));
        var cleared = TrackEditing.Clear(track);

        Assert.Equal(track.Id, cleared.Id);
        Assert.Equal("Crane", cleared.Name);
        Assert.Empty(cleared.Points);
        Assert.Empty(cleared.Timing);
        Assert.Equal(AimMode.AimKeys, cleared.Aim);
    }
```

Create `tests/Vista.Tests/Scenes/SceneEditingTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Scenes;

public class SceneEditingTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Three tracks: Track 1, Track 2, Track 3.
    private static Scene Three()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Add(scene).Scene;
        return SceneEditing.Add(scene).Scene;
    }

    [Fact]
    public void ANewSceneHoldsOneEmptyTrackNamedTrack1()
    {
        var scene = SceneEditing.New();

        var track = Assert.Single(scene.Tracks);
        Assert.Equal("Track 1", track.Name);
        Assert.Empty(track.Points);
        Assert.Empty(scene.Hidden);
    }

    [Fact]
    public void AddPutsAnEmptyTrackAtTheEndNamedByTheCount()
    {
        var (scene, added) = SceneEditing.Add(SceneEditing.New());

        Assert.Equal(2, scene.Tracks.Count);
        Assert.Equal(added, scene.Tracks[1].Id);
        Assert.Equal("Track 2", scene.Tracks[1].Name);
    }

    [Fact]
    public void AddAfterADeleteCanRepeatAName()
    {
        var scene = Three();
        scene = SceneEditing.Delete(scene, scene.Tracks[0].Id).Scene;
        scene = SceneEditing.Add(scene).Scene;

        Assert.Equal(new[] { "Track 2", "Track 3", "Track 3" }, scene.Tracks.Select(t => t.Name));
    }

    [Fact]
    public void GetAndIndexOfFindTracksById()
    {
        var scene = Three();
        var id = scene.Tracks[2].Id;

        Assert.Equal(2, SceneEditing.IndexOf(scene, id));
        Assert.Same(scene.Tracks[2], SceneEditing.Get(scene, id));
        Assert.Equal(-1, SceneEditing.IndexOf(scene, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => SceneEditing.Get(scene, Guid.NewGuid()));
    }

    [Fact]
    public void ReplaceSwapsTheTrackWithTheSameId()
    {
        var scene = Three();
        var changed = TrackEditing.Append(scene.Tracks[1], Point(5f));
        var result = SceneEditing.Replace(scene, changed);

        Assert.Same(changed, result.Tracks[1]);
        Assert.Same(scene.Tracks[0], result.Tracks[0]);
        Assert.Same(result, SceneEditing.Replace(result, changed));
        Assert.Throws<ArgumentException>(() => SceneEditing.Replace(scene, TrackEditing.Empty()));
    }

    [Fact]
    public void RenameChangesTheNameAndRefusesAnEmptyOne()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;
        var renamed = SceneEditing.Rename(scene, id, "Crane");

        Assert.Equal("Crane", renamed.Tracks[1].Name);
        Assert.Same(renamed, SceneEditing.Rename(renamed, id, "Crane"));
        Assert.Throws<ArgumentException>(() => SceneEditing.Rename(scene, id, ""));
        Assert.Throws<ArgumentException>(() => SceneEditing.Rename(scene, id, "   "));
    }

    [Fact]
    public void DuplicateInsertsACopyAfterTheOriginalWithANewIdAndCopyName()
    {
        var scene = Three();
        scene = SceneEditing.Replace(scene, TrackEditing.Append(scene.Tracks[0], Point(4f)));
        var original = scene.Tracks[0];
        var (result, copy) = SceneEditing.Duplicate(scene, original.Id);

        Assert.Equal(4, result.Tracks.Count);
        Assert.Equal(copy, result.Tracks[1].Id);
        Assert.NotEqual(original.Id, copy);
        Assert.Equal("Track 1 copy", result.Tracks[1].Name);
        Assert.Equal(original.Points, result.Tracks[1].Points);
        Assert.Same(scene.Tracks[1], result.Tracks[2]);
    }

    [Fact]
    public void DeleteRemovesTheTrackAndNamesTheOneTakingItsPlace()
    {
        var scene = Three();
        var (result, next) = SceneEditing.Delete(scene, scene.Tracks[1].Id);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(scene.Tracks[2].Id, next);
    }

    [Fact]
    public void DeletingTheLastInTheListNamesTheNewLast()
    {
        var scene = Three();
        var (_, next) = SceneEditing.Delete(scene, scene.Tracks[2].Id);
        Assert.Equal(scene.Tracks[1].Id, next);
    }

    [Fact]
    public void DeleteRefusesTheOnlyTrackAndForgetsAHiddenOne()
    {
        var only = SceneEditing.New();
        Assert.Throws<ArgumentException>(() => SceneEditing.Delete(only, only.Tracks[0].Id));

        var scene = Three();
        var id = scene.Tracks[1].Id;
        scene = SceneEditing.SetHidden(scene, id, true);
        Assert.DoesNotContain(id, SceneEditing.Delete(scene, id).Scene.Hidden);
    }

    [Fact]
    public void MoveReordersTheTracks()
    {
        var scene = Three();
        var moved = SceneEditing.Move(scene, 0, 2);

        Assert.Equal(new[] { scene.Tracks[1].Id, scene.Tracks[2].Id, scene.Tracks[0].Id }, moved.Tracks.Select(t => t.Id));
        Assert.Same(scene, SceneEditing.Move(scene, 1, 1));
        Assert.Throws<ArgumentException>(() => SceneEditing.Move(scene, 0, 3));
    }

    [Fact]
    public void SetHiddenAddsAndRemovesFromTheHiddenSet()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;
        var hidden = SceneEditing.SetHidden(scene, id, true);

        Assert.Contains(id, hidden.Hidden);
        Assert.Same(hidden, SceneEditing.SetHidden(hidden, id, true));
        Assert.DoesNotContain(id, SceneEditing.SetHidden(hidden, id, false).Hidden);
        Assert.Empty(scene.Hidden);
        Assert.Throws<ArgumentException>(() => SceneEditing.SetHidden(scene, Guid.NewGuid(), true));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: the build fails because `Scene`, `SceneEditing`, `Track.Id`, `Track.Name` and `TrackEditing.Clear` don't exist.

- [ ] **Step 3: Add Id and Name to Track**

`src/Vista.Core/Tracks/Track.cs`:

```csharp
namespace Vista.Core.Tracks;

/// <summary>A camera move: its identity and name, a path through control points, their timing, the track's speed, how it aims and how it plays.</summary>
public sealed record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop);
```

In `TrackEditing.cs`, replace `Empty` and add `Clear` after it:

```csharp
    /// <summary>A track with a new Id and no points at the default speed, playing forward once.</summary>
    public static Track Empty(AimMode aim = AimMode.AimKeys, string name = "Track 1")
        => new(Guid.NewGuid(), name, [], [], DefaultSpeed, aim, PlaybackDirection.Forward, false);

    /// <summary>An empty track that keeps <paramref name="track"/>'s Id and Name.</summary>
    public static Track Clear(Track track) => Empty(name: track.Name) with { Id = track.Id };
```

- [ ] **Step 4: Add the Scene and scene edits**

`src/Vista.Core/Scenes/Scene.cs`:

```csharp
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, and which of them are hidden.</summary>
public sealed record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden);
```

`src/Vista.Core/Scenes/SceneEditing.cs`:

```csharp
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's tracks: add, rename, duplicate, delete, move and hide.</summary>
public static class SceneEditing
{
    /// <summary>A scene holding one empty track, "Track 1".</summary>
    public static Scene New() => new([TrackEditing.Empty()], new HashSet<Guid>());

    /// <summary>The index of track <paramref name="id"/>, or −1.</summary>
    public static int IndexOf(Scene scene, Guid id)
    {
        for (var i = 0; i < scene.Tracks.Count; i++)
            if (scene.Tracks[i].Id == id) return i;
        return -1;
    }

    /// <summary>Track <paramref name="id"/>.</summary>
    public static Track Get(Scene scene, Guid id) => scene.Tracks[Require(scene, id)];

    /// <summary>Puts <paramref name="track"/> in place of the track with its Id.</summary>
    public static Scene Replace(Scene scene, Track track)
    {
        var index = Require(scene, track.Id);
        if (ReferenceEquals(scene.Tracks[index], track)) return scene;
        var tracks = scene.Tracks.ToArray();
        tracks[index] = track;
        return scene with { Tracks = tracks };
    }

    /// <summary>Adds an empty track at the end, named "Track N" for the new count.</summary>
    public static (Scene Scene, Guid Added) Add(Scene scene)
    {
        var track = TrackEditing.Empty(name: $"Track {scene.Tracks.Count + 1}");
        return (scene with { Tracks = [.. scene.Tracks, track] }, track.Id);
    }

    /// <summary>Renames track <paramref name="id"/>; an empty name is refused.</summary>
    public static Scene Rename(Scene scene, Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A track needs a name.");
        var track = Get(scene, id);
        return track.Name == name ? scene : Replace(scene, track with { Name = name });
    }

    /// <summary>Inserts a copy of track <paramref name="id"/> after it, with a new Id and "copy" after its name.</summary>
    public static (Scene Scene, Guid Copy) Duplicate(Scene scene, Guid id)
    {
        var index = Require(scene, id);
        var original = scene.Tracks[index];
        var copy = original with { Id = Guid.NewGuid(), Name = $"{original.Name} copy" };
        var tracks = scene.Tracks.ToList();
        tracks.Insert(index + 1, copy);
        return (scene with { Tracks = tracks }, copy.Id);
    }

    /// <summary>Deletes track <paramref name="id"/>, refusing the only track; names the track now at its place, or the new last.</summary>
    public static (Scene Scene, Guid Next) Delete(Scene scene, Guid id)
    {
        var index = Require(scene, id);
        if (scene.Tracks.Count == 1) throw new ArgumentException("A scene keeps at least one track.");
        var tracks = scene.Tracks.ToList();
        tracks.RemoveAt(index);
        var hidden = new HashSet<Guid>(scene.Hidden);
        hidden.Remove(id);
        return (new Scene(tracks, hidden), tracks[Math.Min(index, tracks.Count - 1)].Id);
    }

    /// <summary>Moves the track at <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Scene Move(Scene scene, int from, int to)
    {
        if (from < 0 || from >= scene.Tracks.Count || to < 0 || to >= scene.Tracks.Count)
            throw new ArgumentException("There is no such track to move.");
        if (from == to) return scene;
        var tracks = scene.Tracks.ToList();
        var track = tracks[from];
        tracks.RemoveAt(from);
        tracks.Insert(to, track);
        return scene with { Tracks = tracks };
    }

    /// <summary>Hides or shows track <paramref name="id"/>.</summary>
    public static Scene SetHidden(Scene scene, Guid id, bool hidden)
    {
        Require(scene, id);
        if (scene.Hidden.Contains(id) == hidden) return scene;
        var set = new HashSet<Guid>(scene.Hidden);
        if (hidden) set.Add(id);
        else set.Remove(id);
        return scene with { Hidden = set };
    }

    private static int Require(Scene scene, Guid id)
        => IndexOf(scene, id) is var index and >= 0 ? index : throw new ArgumentException("There is no such track.");
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings. (Existing tests call `TrackEditing.Empty()` or `Empty(AimMode.X)`, which still compile.)

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Core/Tracks/Track.cs src/Vista.Core/Tracks/TrackEditing.cs src/Vista.Core/Scenes/Scene.cs src/Vista.Core/Scenes/SceneEditing.cs tests/Vista.Tests/Scenes/SceneEditingTests.cs tests/Vista.Tests/Tracks/TrackEditingTests.cs
git commit -m "feat(scenes) add named tracks and scene edits"
```

---

### Task 2: Hit-testing markers across tracks

**Files:**
- Create: `src/Vista.Core/Editing/TrackMarkerHitTest.cs`
- Test: `tests/Vista.Tests/Editing/TrackMarkerHitTestTests.cs`

**Interfaces:**
- Produces (namespace `Vista.Core.Editing`):
  - `readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen)`: one point's marker; `Screen` is null off screen.
  - `static int? TrackMarkerHitTest.Nearest(IReadOnlyList<TrackMarker> markers, Guid edited, Vector2 cursor, float radius)`: the index into `markers` of the hit, or null. The edited track's nearest marker within the radius wins; otherwise the nearest other marker, and on a tie the later one in the list (drawn on top).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class TrackMarkerHitTestTests
{
    private static readonly Guid Edited = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Third = Guid.NewGuid();

    [Fact]
    public void TheEditedTracksMarkerWinsEvenWhenAnotherIsNearer()
    {
        var markers = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Edited, 3, new Vector2(108f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(101f, 100f), 10f));
    }

    [Fact]
    public void AnotherTracksMarkerIsHitWhenNoEditedMarkerIsInRange()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, 0, new Vector2(300f, 300f)),
            new TrackMarker(Other, 2, new Vector2(100f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(102f, 100f), 10f));
    }

    [Fact]
    public void AmongOtherTracksTheNearestWinsAndATieGoesToTheLaterMarker()
    {
        var nearer = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Third, 0, new Vector2(106f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(nearer, Edited, new Vector2(105f, 100f), 10f));

        var tie = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Third, 0, new Vector2(100f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(tie, Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void OffScreenAndOutOfRangeMarkersAreNotHit()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, 0, null),
            new TrackMarker(Other, 0, new Vector2(200f, 200f)),
        };
        Assert.Null(TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Null(TrackMarkerHitTest.Nearest([], Edited, Vector2.Zero, 10f));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter TrackMarkerHitTestTests`
Expected: the build fails because `TrackMarker` and `TrackMarkerHitTest` don't exist.

- [ ] **Step 3: Write the implementation**

`src/Vista.Core/Editing/TrackMarkerHitTest.cs`:

```csharp
using System.Numerics;

namespace Vista.Core.Editing;

/// <summary>One point's marker on screen; <see cref="Screen"/> is null when off screen.</summary>
public readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen);

/// <summary>Finds which marker a click landed on when several tracks are drawn.</summary>
public static class TrackMarkerHitTest
{
    /// <summary>The index of the hit marker, or null: the edited track's nearest within <paramref name="radius"/>, else the nearest other, later winning a tie.</summary>
    public static int? Nearest(IReadOnlyList<TrackMarker> markers, Guid edited, Vector2 cursor, float radius)
        => NearestOf(markers, cursor, radius, m => m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Track != edited, laterWinsTie: true);

    private static int? NearestOf(IReadOnlyList<TrackMarker> markers, Vector2 cursor, float radius, Func<TrackMarker, bool> include, bool laterWinsTie)
    {
        int? best = null;
        var bestDistance = radius * radius;
        for (var i = 0; i < markers.Count; i++)
        {
            if (!include(markers[i]) || markers[i].Screen is not { } at) continue;
            var distance = Vector2.DistanceSquared(at, cursor);
            if (distance > bestDistance || (distance == bestDistance && best is not null && !laterWinsTie)) continue;
            best = i;
            bestDistance = distance;
        }

        return best;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Core/Editing/TrackMarkerHitTest.cs tests/Vista.Tests/Editing/TrackMarkerHitTestTests.cs
git commit -m "feat(editing) hit-test markers across tracks"
```

---

### Task 3: The session edits a scene

Starts once Task 1 is on `main`.

**Files:**
- Modify: `src/Vista.Core/Session/EditHistory.cs` (`EditSnapshot`), `src/Vista.Core/Session/SessionState.cs`, `src/Vista.Plugin/Session/CameraSession.cs`, `src/Vista.Plugin/Ui/TrackEditorWindow.cs` (Clear track only)
- Test: `tests/Vista.Tests/Session/SessionSceneTests.cs` (new), `tests/Vista.Tests/Session/EditHistoryTests.cs`, `tests/Vista.Tests/Session/SessionEditingTests.cs`

**Interfaces:**
- Consumes: `Scene`, `SceneEditing.*`, `TrackEditing.Clear`, `Track.Id` (Task 1).
- Produces:
  - `readonly record struct EditSnapshot(Scene Scene, Guid Edited, int? Selected)`
  - On `SessionState`: `Scene Scene { get; }`, `Guid EditedTrackId { get; }`, `Track Track { get; }` (the edited track), and these, each returning a refusal reason or null:
    `AddTrack()`, `RenameTrack(Guid id, string name)`, `DuplicateTrack(Guid id)`, `DeleteTrack(Guid id)`, `MoveTrack(int from, int to)`, `SetTrackHidden(Guid id, bool hidden)`, `SwitchTrack(Guid id)`.
  - On `CameraSession`: `Scene Scene`, `Guid EditedTrackId`, the same six edits passing through, plus
    `string? OpenTrack(Guid id)` (switch, then fly the editor camera to the track's first point) and
    `string? SelectPoint(Guid track, int index)` (switch, then select the point, leaving the camera alone).

- [ ] **Step 1: Update the existing tests for the new snapshot and Clear**

In `EditHistoryTests.cs`, change the helper to:

```csharp
    private static EditSnapshot Snap(int? selected) => new(SceneEditing.New(), Guid.Empty, selected);
```

and add `using Vista.Core.Scenes;`.

In `SessionEditingTests.cs`, the two calls `state.ChangeTrack(_ => TrackEditing.Empty());` become `state.ChangeTrack(TrackEditing.Clear);` (a change may no longer swap the track's Id).

- [ ] **Step 2: Write the failing session tests**

Create `tests/Vista.Tests/Session/SessionSceneTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionSceneTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing; Track 1 has points at x = 0, 10, 20 (two 5 s legs).
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    private static Guid First(SessionState state) => state.Scene.Tracks[0].Id;

    [Fact]
    public void StartsEditingTheOnlyTrack()
    {
        var state = new SessionState();
        var track = Assert.Single(state.Scene.Tracks);
        Assert.Equal(track.Id, state.EditedTrackId);
        Assert.Same(track, state.Track);
        Assert.Equal("Track 1", state.Track.Name);
    }

    [Fact]
    public void TrackEditsChangeOnlyTheEditedTrack()
    {
        var state = Editing();
        Assert.Null(state.AddTrack());
        state.AddToEnd(Point(50f));

        Assert.Equal(3, state.Scene.Tracks[0].Points.Count);
        Assert.Single(state.Scene.Tracks[1].Points);
        Assert.Same(state.Scene.Tracks[1], state.Track);
    }

    [Fact]
    public void AddTrackSwitchesToItAndClearsTheSelectionAndScrubHead()
    {
        var state = Editing();
        state.Select(1);
        state.ScrubTo(3.0);

        state.AddTrack();

        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Null(state.Selected);
        Assert.Null(state.SelectedKey);
        Assert.Equal(0.0, state.ScrubHead);
    }

    [Fact]
    public void SwitchingIsNotAnUndoStepAndClearsTheSelectionAndScrubHead()
    {
        var state = Editing();
        state.AddTrack();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(First(state));
        state.Select(2);
        state.ScrubTo(4.0);

        Assert.Null(state.SwitchTrack(second));

        Assert.Equal(second, state.EditedTrackId);
        Assert.Null(state.Selected);
        Assert.Equal(0.0, state.ScrubHead);
        Assert.True(state.Undo());
        Assert.Equal(2, state.Scene.Tracks.Count);
    }

    [Fact]
    public void UndoingAChangeInAnotherTrackSwitchesBackToIt()
    {
        var state = Editing();
        var first = First(state);
        state.AddTrack();
        state.SwitchTrack(first);
        state.AddToEnd(Point(30f));
        state.SwitchTrack(state.Scene.Tracks[1].Id);

        Assert.True(state.Undo());

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void UndoAndRedoCoverSceneEdits()
    {
        var state = Editing();
        var first = First(state);
        state.RenameTrack(first, "Crane");
        Assert.Equal("Crane", state.Track.Name);

        Assert.True(state.Undo());
        Assert.Equal("Track 1", state.Track.Name);
        Assert.True(state.Redo());
        Assert.Equal("Crane", state.Track.Name);
    }

    [Fact]
    public void DuplicateSwitchesToTheCopy()
    {
        var state = Editing();
        Assert.Null(state.DuplicateTrack(First(state)));

        Assert.Equal(2, state.Scene.Tracks.Count);
        Assert.Equal(state.Scene.Tracks[1].Id, state.EditedTrackId);
        Assert.Equal("Track 1 copy", state.Track.Name);
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void DeletingTheEditedTrackSwitchesToTheOneTakingItsPlace()
    {
        var state = Editing();
        state.AddTrack();
        state.AddTrack();
        var third = state.Scene.Tracks[2].Id;
        state.SwitchTrack(state.Scene.Tracks[1].Id);

        Assert.Null(state.DeleteTrack(state.EditedTrackId));

        Assert.Equal(third, state.EditedTrackId);
    }

    [Fact]
    public void DeletingAnotherTrackKeepsTheEditedTrackAndSelection()
    {
        var state = Editing();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;
        state.SwitchTrack(First(state));
        state.Select(1);

        Assert.Null(state.DeleteTrack(second));

        Assert.Equal(First(state), state.EditedTrackId);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void TheLastTrackCannotBeDeleted()
    {
        var state = Editing();
        Assert.NotNull(state.DeleteTrack(First(state)));
        Assert.Single(state.Scene.Tracks);
    }

    [Fact]
    public void AnEmptyNameIsRefused()
    {
        var state = Editing();
        Assert.NotNull(state.RenameTrack(First(state), " "));
        Assert.Equal("Track 1", state.Track.Name);
    }

    [Fact]
    public void MoveReordersTracksAsAnUndoStep()
    {
        var state = Editing();
        state.AddTrack();
        var second = state.Scene.Tracks[1].Id;

        Assert.Null(state.MoveTrack(1, 0));
        Assert.Equal(second, state.Scene.Tracks[0].Id);
        Assert.True(state.Undo());
        Assert.Equal(second, state.Scene.Tracks[1].Id);
    }

    [Fact]
    public void TheEditedTrackCannotBeHiddenButOthersCan()
    {
        var state = Editing();
        state.AddTrack();
        var second = state.EditedTrackId;
        var first = First(state);

        Assert.NotNull(state.SetTrackHidden(second, true));
        Assert.Null(state.SetTrackHidden(first, true));
        Assert.Contains(first, state.Scene.Hidden);
    }

    [Fact]
    public void SwitchingToAHiddenTrackShowsItAsAnUndoStep()
    {
        var state = Editing();
        var first = First(state);
        state.AddTrack();
        state.SetTrackHidden(first, true);

        Assert.Null(state.SwitchTrack(first));

        Assert.Equal(first, state.EditedTrackId);
        Assert.DoesNotContain(first, state.Scene.Hidden);
        Assert.True(state.Undo());
        Assert.Contains(first, state.Scene.Hidden);
    }

    [Fact]
    public void ClearKeepsTheTracksIdAndName()
    {
        var state = Editing();
        var id = state.EditedTrackId;
        state.RenameTrack(id, "Crane");

        Assert.Null(state.ChangeTrack(TrackEditing.Clear));

        Assert.Equal(id, state.EditedTrackId);
        Assert.Equal("Crane", state.Track.Name);
        Assert.Empty(state.Track.Points);
    }

    [Fact]
    public void AChangeThatSwapsTheTrackIsRefused()
    {
        var state = Editing();
        var before = state.Scene;

        Assert.NotNull(state.ChangeTrack(_ => TrackEditing.Empty()));

        Assert.Same(before, state.Scene);
    }

    [Fact]
    public void SceneEditsAndSwitchingAreRefusedUnlessEditing()
    {
        var state = Editing();
        state.AddTrack();
        var first = First(state);
        state.SwitchTrack(first);
        state.Play();
        Assert.Equal(CameraMode.Live, state.Mode);

        Assert.NotNull(state.AddTrack());
        Assert.NotNull(state.SwitchTrack(first));
        Assert.NotNull(state.RenameTrack(first, "Crane"));
        Assert.Equal(2, state.Scene.Tracks.Count);
    }

    [Fact]
    public void LivePlaysTheEditedTrack()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));

        state.Play();
        state.Director.Tick(10f);

        Assert.Equal(2.0, state.Director.ShotTime, 3);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: the build fails because `Scene`, `EditedTrackId`, `AddTrack` and the other scene members don't exist on `SessionState`, and `EditSnapshot` has the old shape.

- [ ] **Step 4: Change the snapshot**

In `src/Vista.Core/Session/EditHistory.cs`, replace `EditSnapshot` with:

```csharp
/// <summary>The scene, the edited track and its selected point, as one undo step restores them.</summary>
public readonly record struct EditSnapshot(Scene Scene, Guid Edited, int? Selected);
```

and add `using Vista.Core.Scenes;`.

- [ ] **Step 5: Move `SessionState` onto the scene**

In `src/Vista.Core/Session/SessionState.cs` (add `using Vista.Core.Scenes;`):

1. Replace `public Track Track { get; private set; } = TrackEditing.Empty();` with:

```csharp
    /// <summary>The tracks being edited, their order and which are hidden.</summary>
    public Scene Scene { get; private set; } = SceneEditing.New();

    /// <summary>The Id of the track the editor works on.</summary>
    public Guid EditedTrackId { get; private set; }

    /// <summary>The edited track: Edit builds it and Play plays it. Changed only through the edit methods and undo.</summary>
    public Track Track
    {
        get => SceneEditing.Get(Scene, EditedTrackId);
        private set => Scene = SceneEditing.Replace(Scene, value);
    }

    public SessionState() => EditedTrackId = Scene.Tracks[0].Id;
```

2. `Current` becomes:

```csharp
    private EditSnapshot Current => new(Scene, EditedTrackId, Selected);
```

3. In `Commit`, right after `if (ReferenceEquals(result, Track)) return null;`, add:

```csharp
            if (result.Id != EditedTrackId) return "A change cannot replace the track.";
```

4. Replace `EndLiveEdit` with:

```csharp
    /// <summary>Ends a live edit, recording it as one undo step if the track changed.</summary>
    public void EndLiveEdit()
    {
        if (liveEditStart is not { } start) return;
        liveEditStart = null;
        var startTrack = SceneEditing.Get(start.Scene, start.Edited);
        if (ReferenceEquals(startTrack, Track)) return;

        // Previews rebuild the lists, so compare values: a drag back to the start is no step.
        if (startTrack.Points.SequenceEqual(Track.Points) && startTrack.Timing.SequenceEqual(Track.Timing) && startTrack.Speed == Track.Speed) Track = startTrack;
        else history.Record(start);
    }
```

5. In `PreviewFromStart`, read the starting track once and use it in place of `start.Track` (three places):

```csharp
        if (liveEditStart is not { } start) return "No live edit is in progress.";
        var startTrack = SceneEditing.Get(start.Scene, start.Edited);
        if (!ReferenceEquals(evaluatedStart, startTrack))
        {
            liveStartEvaluator = new TrackEvaluator(startTrack);
            evaluatedStart = startTrack;
        }

        try
        {
            var result = change(startTrack, liveStartEvaluator!);
```

   and in `PreviewHandle`, `start.Timing[...]` already reads the lambda's `start` parameter, which is now `startTrack`; no change there.

6. Replace `Restore` with:

```csharp
    private bool Restore(EditSnapshot? snapshot)
    {
        if (snapshot is not { } s) return false;
        var pointsBefore = Track.Points;
        if (s.Edited != EditedTrackId) ClearForSwitch();
        Scene = s.Scene;
        EditedTrackId = s.Edited;
        Selected = s.Selected;
        RefreshTimingSelection(pointsBefore);
        return true;
    }
```

7. Add the scene edits, after `UnifyHandles`:

```csharp
    /// <summary>Adds an empty track at the end and edits it. Returns why it was refused, or null.</summary>
    public string? AddTrack() => CommitScene(scene => SceneEditing.Add(scene));

    /// <summary>Renames track <paramref name="id"/>. Returns why it was refused, or null.</summary>
    public string? RenameTrack(Guid id, string name) => CommitScene(scene => (SceneEditing.Rename(scene, id, name), EditedTrackId));

    /// <summary>Copies track <paramref name="id"/> after itself and edits the copy. Returns why it was refused, or null.</summary>
    public string? DuplicateTrack(Guid id) => CommitScene(scene => SceneEditing.Duplicate(scene, id));

    /// <summary>Deletes track <paramref name="id"/>; deleting the edited track edits the one taking its place. Returns why it was refused, or null.</summary>
    public string? DeleteTrack(Guid id)
        => CommitScene(scene =>
        {
            var (result, next) = SceneEditing.Delete(scene, id);
            return (result, id == EditedTrackId ? next : EditedTrackId);
        });

    /// <summary>Moves a track in the Hierarchy order. Returns why it was refused, or null.</summary>
    public string? MoveTrack(int from, int to) => CommitScene(scene => (SceneEditing.Move(scene, from, to), EditedTrackId));

    /// <summary>Hides or shows track <paramref name="id"/>; the edited track is always shown. Returns why it was refused, or null.</summary>
    public string? SetTrackHidden(Guid id, bool hidden)
    {
        if (hidden && id == EditedTrackId) return "The track being edited is always shown.";
        return CommitScene(scene => (SceneEditing.SetHidden(scene, id, hidden), EditedTrackId));
    }

    /// <summary>Edits track <paramref name="id"/>, showing it first if hidden. Not an undo step itself. Returns why it was refused, or null.</summary>
    public string? SwitchTrack(Guid id)
    {
        if (Mode != CameraMode.Editing) return "Tracks can only be switched while editing.";
        if (SceneEditing.IndexOf(Scene, id) < 0) return "There is no such track.";
        if (Scene.Hidden.Contains(id) && SetTrackHidden(id, false) is { } refusal) return refusal;
        if (id == EditedTrackId) return null;

        EndLiveEdit();
        ClearForSwitch();
        EditedTrackId = id;
        return null;
    }
```

8. Add these private helpers next to `Commit`:

```csharp
    /// <summary>Applies a scene change and the edited track it leaves, as one undo step. Returns why it was refused, or null.</summary>
    private string? CommitScene(Func<Scene, (Scene Scene, Guid Edited)> change)
    {
        if (Mode != CameraMode.Editing) return "The scene can only change while editing.";
        EndLiveEdit();

        try
        {
            var (result, edited) = change(Scene);
            if (ReferenceEquals(result, Scene) && edited == EditedTrackId) return null;

            history.Record(Current);
            if (edited != EditedTrackId) ClearForSwitch();
            Scene = result;
            EditedTrackId = edited;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Clears the point, key and leg selection and puts the scrub head at 0, as switching tracks does.</summary>
    private void ClearForSwitch()
    {
        Selected = null;
        SelectedKey = null;
        SelectedLeg = null;
        scrubTime = 0.0;
    }
```

9. Any remaining direct assignment `Track = ...` in the file still works through the setter. Check nothing assigns `Track` to a track with another Id: `grep -n "Track = " src/Vista.Core/Session/SessionState.cs`.

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings. If an existing test fails only because tracks now carry an Id and Name (or because a change may no longer swap the track), fix that test minimally, keep its intent, and list it in the report.

- [ ] **Step 7: Pass the scene through `CameraSession`, and fix Clear track**

In `src/Vista.Plugin/Session/CameraSession.cs` (add `using Vista.Core.Scenes;`), change the `Track` summary to `/// <summary>The edited track: Edit builds it and Play plays it. Changed only through the edit methods and undo.</summary>` and add after it:

```csharp
    /// <summary>The tracks being edited, their order and which are hidden.</summary>
    public Scene Scene => state.Scene;

    /// <summary>The Id of the track the editor works on.</summary>
    public Guid EditedTrackId => state.EditedTrackId;

    /// <summary>Adds an empty track and edits it. Returns why it was refused, or null.</summary>
    public string? AddTrack() => state.AddTrack();

    /// <summary>Renames a track. Returns why it was refused, or null.</summary>
    public string? RenameTrack(Guid id, string name) => state.RenameTrack(id, name);

    /// <summary>Copies a track after itself and edits the copy. Returns why it was refused, or null.</summary>
    public string? DuplicateTrack(Guid id) => state.DuplicateTrack(id);

    /// <summary>Deletes a track. Returns why it was refused, or null.</summary>
    public string? DeleteTrack(Guid id) => state.DeleteTrack(id);

    /// <summary>Moves a track in the Hierarchy order. Returns why it was refused, or null.</summary>
    public string? MoveTrack(int from, int to) => state.MoveTrack(from, to);

    /// <summary>Hides or shows a track. Returns why it was refused, or null.</summary>
    public string? SetTrackHidden(Guid id, bool hidden) => state.SetTrackHidden(id, hidden);

    /// <summary>Edits a track and flies the editor camera to its first point. Returns why it was refused, or null.</summary>
    public string? OpenTrack(Guid id)
    {
        var refusal = state.SwitchTrack(id);
        if (refusal is null) JumpToPoint(0);
        return refusal;
    }

    /// <summary>Edits a track and selects one of its points, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SelectPoint(Guid track, int index)
    {
        var refusal = state.SwitchTrack(track);
        if (refusal is null) state.Select(index);
        return refusal;
    }
```

In `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, the Clear track button's `session.ChangeTrack(_ => TrackEditing.Empty())` becomes `session.ChangeTrack(TrackEditing.Clear)`.

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Vista.Core/Session/EditHistory.cs src/Vista.Core/Session/SessionState.cs src/Vista.Plugin/Session/CameraSession.cs src/Vista.Plugin/Ui/TrackEditorWindow.cs tests/Vista.Tests/Session/SessionSceneTests.cs tests/Vista.Tests/Session/EditHistoryTests.cs tests/Vista.Tests/Session/SessionEditingTests.cs
git commit -m "feat(session) edit one track of a scene with undo across the scene"
```

---

### Task 4: Draw every shown track and switch on marker clicks

Starts once Tasks 2 and 3 are on `main`. Runs in parallel with Task 5.

**Files:**
- Modify: `src/Vista.Plugin/Editor/EditorColours.cs`, `src/Vista.Plugin/Editor/Overlay.cs`, `src/Vista.Plugin/Editor/EditorLayer.cs`

**Interfaces:**
- Consumes: `CameraSession.Scene`, `EditedTrackId`, `Select(int?)`, `SelectPoint(Guid, int)`, `Track`, `Selected` (Task 3); `TrackMarker`, `TrackMarkerHitTest.Nearest` (Task 2).
- Produces: `Overlay.Draw(EditorView view, Track track, int? selected, bool edited)` and `Overlay.Prune(IReadOnlySet<Guid> ids)`.

No Core changes, so no new tests; the plugin build and the checklist cover it.

- [ ] **Step 1: Add the grey colours**

In `EditorColours.cs`, add:

```csharp
    public const uint OtherPath = 0x78A0A0A0;
    public const uint OtherGlyph = 0x78A0A0A0;
    public const uint OtherUpLine = 0x78A0A0A0;
    public const uint OtherMarker = 0xA0303030;
    public const uint OtherMarkerRing = 0x90B0B0B0;
    public const uint OtherMarkerText = 0xB0C8C8C8;
```

- [ ] **Step 2: Make the overlay draw any track, cached per track**

Replace `src/Vista.Plugin/Editor/Overlay.cs` with:

```csharp
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Editor;

/// <summary>Draws a track's path, and a wireframe camera per point with its number on the point; tracks not being edited draw in grey.</summary>
internal sealed class Overlay
{
    public const float MarkerRadius = 20f;
    private const float LabelScale = 2f;
    private const float PathSpacing = 0.25f;
    private const float PathThickness = 3f;
    private const float GlyphDepth = 1f;
    private const float GlyphThickness = 1.5f;
    private const float SelectedGlyphThickness = 2.5f;

    private readonly Dictionary<Guid, TrackCache> caches = new();

    /// <summary>Draws <paramref name="track"/>, in grey unless <paramref name="edited"/>, and returns each number's absolute screen position, null when off screen.</summary>
    public IReadOnlyList<Vector2?> Draw(EditorView view, Track track, int? selected, bool edited)
    {
        if (!caches.TryGetValue(track.Id, out var cache)) caches[track.Id] = cache = new TrackCache();
        var palette = edited ? Palette.Edited : Palette.Other;
        var list = ImGui.GetBackgroundDrawList();
        DrawPath(list, view, track, cache, palette);

        var aspect = view.Size.Y > 0f ? view.Size.X / view.Size.Y : 1f;
        var labels = new Vector2?[track.Points.Count];
        for (var i = 0; i < track.Points.Count; i++)
        {
            var (forward, roll, fov) = Pose(track, cache, i);
            var up = CameraOrientation.UpFor(Vector3.Zero, forward, roll);
            var glyph = CameraGlyph.Build(track.Points[i].Position, forward, up, fov, aspect, GlyphDepth);
            DrawGlyph(list, view, glyph, i == selected, palette);
            labels[i] = view.ToScreen(track.Points[i].Position);
        }

        DrawLabels(list, labels, selected, palette);
        return labels;
    }

    /// <summary>Forgets cached paths of tracks not in <paramref name="ids"/>.</summary>
    public void Prune(IReadOnlySet<Guid> ids)
    {
        foreach (var id in caches.Keys.Where(id => !ids.Contains(id)).ToList()) caches.Remove(id);
    }

    private static void DrawPath(ImDrawListPtr list, EditorView view, Track track, TrackCache cache, Palette palette)
    {
        if (!ReferenceEquals(cache.SampledPoints, track.Points))
        {
            cache.Samples = TrackPath.Sample(track.Points.Select(p => p.Position).ToArray(), PathSpacing);
            cache.SampledPoints = track.Points;
        }

        for (var i = 1; i < cache.Samples.Count; i++)
        {
            if (ScreenProjection.ProjectSegment(cache.Samples[i - 1], cache.Samples[i], view.ViewProjection, view.Size, view.Near) is { } s)
                list.AddLine(view.Origin + s.Start, view.Origin + s.End, palette.Path, PathThickness);
        }
    }

    /// <summary>Point <paramref name="index"/>'s aim, roll and FoV: recorded, or along the path in Direction-of-travel mode.</summary>
    private static (Vector3 Forward, float Roll, float Fov) Pose(Track track, TrackCache cache, int index)
    {
        var point = track.Points[index];
        if (track.Aim == AimMode.AimKeys)
            return (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);

        if (!ReferenceEquals(cache.EvaluatedTrack, track))
        {
            cache.Evaluator = new TrackEvaluator(track);
            cache.EvaluatedTrack = track;
        }

        return cache.Evaluator!.Evaluate(cache.Evaluator.PointSeconds(index)) is { } frame
            ? (frame.LookAt - frame.Position, frame.Roll, point.Fov)
            : (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);
    }

    private static void DrawGlyph(ImDrawListPtr list, EditorView view, CameraGlyph glyph, bool selected, Palette palette)
    {
        var colour = selected ? EditorColours.Selected : palette.Glyph;
        var thickness = selected ? SelectedGlyphThickness : GlyphThickness;
        var corners = glyph.Corners;
        for (var i = 0; i < corners.Length; i++)
        {
            DrawEdge(list, view, glyph.Apex, corners[i], colour, thickness);
            DrawEdge(list, view, corners[i], corners[(i + 1) % corners.Length], colour, thickness);
        }

        if (view.ToScreenBeyondNear(glyph.TabLeft) is { } left && view.ToScreenBeyondNear(glyph.TabTip) is { } tip
            && view.ToScreenBeyondNear(glyph.TabRight) is { } right)
            list.AddTriangleFilled(left, tip, right, palette.UpLine);
    }

    private static void DrawEdge(ImDrawListPtr list, EditorView view, Vector3 from, Vector3 to, uint colour, float thickness)
    {
        if (ScreenProjection.ProjectSegment(from, to, view.ViewProjection, view.Size, view.Near) is { } s)
            list.AddLine(view.Origin + s.Start, view.Origin + s.End, colour, thickness);
    }

    private static void DrawLabels(ImDrawListPtr list, Vector2?[] labels, int? selected, Palette palette)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] is not { } at) continue;

            var ring = i == selected ? EditorColours.Selected : palette.MarkerRing;
            list.AddCircleFilled(at, MarkerRadius, palette.Marker);
            list.AddCircle(at, MarkerRadius, ring, 0, i == selected ? 3f : 1.5f);

            var label = (i + 1).ToString();
            var size = ImGui.CalcTextSize(label) * LabelScale;
            list.AddText(ImGui.GetFont(), ImGui.GetFontSize() * LabelScale, at - (size / 2f), palette.MarkerText, label);
        }
    }

    /// <summary>A track's sampled path and evaluator, rebuilt when the track changes.</summary>
    private sealed class TrackCache
    {
        public IReadOnlyList<ControlPoint>? SampledPoints;
        public IReadOnlyList<Vector3> Samples = [];
        public Track? EvaluatedTrack;
        public TrackEvaluator? Evaluator;
    }

    /// <summary>The colours one track draws in.</summary>
    private readonly record struct Palette(uint Path, uint Glyph, uint UpLine, uint Marker, uint MarkerRing, uint MarkerText)
    {
        public static readonly Palette Edited = new(EditorColours.Path, EditorColours.AimLine, EditorColours.UpLine, EditorColours.Marker, EditorColours.MarkerRing, EditorColours.MarkerText);
        public static readonly Palette Other = new(EditorColours.OtherPath, EditorColours.OtherGlyph, EditorColours.OtherUpLine, EditorColours.OtherMarker, EditorColours.OtherMarkerRing, EditorColours.OtherMarkerText);
    }
}
```

- [ ] **Step 3: Draw the scene and switch tracks on clicks**

In `src/Vista.Plugin/Editor/EditorLayer.cs`, replace the block from `var track = gizmo.Preview ...` through `var hovered = MarkerHitTest.Nearest(markers, io.MousePos, HitRadius);` with:

```csharp
        var scene = session.Scene;
        var edited = session.EditedTrackId;
        var markers = new List<TrackMarker>();

        // Other tracks first, so the edited track draws on top.
        foreach (var other in scene.Tracks)
        {
            if (other.Id == edited || scene.Hidden.Contains(other.Id)) continue;
            AddMarkers(markers, other.Id, overlay.Draw(view, other, null, edited: false));
        }

        var track = gizmo.Preview is { } preview && preview.Index < session.Track.Points.Count
            ? TrackEditing.Replace(session.Track, preview.Index, preview.Point)
            : session.Track;
        AddMarkers(markers, edited, overlay.Draw(view, track, session.Selected, edited: true));
        overlay.Prune(scene.Tracks.Select(t => t.Id).ToHashSet());

        var io = ImGui.GetIO();
        var hovered = TrackMarkerHitTest.Nearest(markers, edited, io.MousePos, HitRadius);
```

Change the click line to pass the markers:

```csharp
            Apply(clicks.Update(PhysicalKeys.IsDown(VirtualKey.LBUTTON), io.MousePos, look, overUi, gizmo.Hot, hovered), markers);
```

Replace `Apply` and add `AddMarkers`:

```csharp
    /// <summary>Selects a clicked point, switching to its track first when it isn't the edited one; a click on empty space clears the selection.</summary>
    private void Apply(ClickOutcome outcome, IReadOnlyList<TrackMarker> markers)
    {
        switch (outcome.Kind)
        {
            case ClickKind.Select when outcome.Index < markers.Count:
                var hit = markers[outcome.Index];
                if (hit.Track == session.EditedTrackId) session.Select(hit.Point);
                else Report(session.SelectPoint(hit.Track, hit.Point));
                break;
            case ClickKind.Deselect:
                session.Select(null);
                break;
        }
    }

    private static void AddMarkers(List<TrackMarker> markers, Guid track, IReadOnlyList<Vector2?> screens)
    {
        for (var i = 0; i < screens.Count; i++) markers.Add(new TrackMarker(track, i, screens[i]));
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[editor] {Refusal}", refusal);
    }
```

Remove the now-unused `var io = ImGui.GetIO();` further down if it duplicates the one above. Update the class summary to `/// <summary>Everything drawn over the game in editing mode: every shown track, marker clicks and the gizmo.</summary>`.

- [ ] **Step 4: Build**

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors.

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Plugin/Editor/EditorColours.cs src/Vista.Plugin/Editor/Overlay.cs src/Vista.Plugin/Editor/EditorLayer.cs
git commit -m "feat(editor) draw every shown track and switch tracks on marker clicks"
```

---

### Task 5: The Hierarchy compartment

Starts once Task 3 is on `main`. Runs in parallel with Task 4.

**Files:**
- Create: `src/Vista.Plugin/Ui/HierarchyPanel.cs`
- Modify: `src/Vista.Plugin/Ui/TrackEditorWindow.cs`

**Interfaces:**
- Consumes: `CameraSession.Scene`, `EditedTrackId`, `AddTrack`, `RenameTrack`, `DuplicateTrack`, `DeleteTrack`, `MoveTrack`, `SetTrackHidden`, `OpenTrack` (Task 3).
- Produces: `HierarchyPanel(CameraSession session)`, `HierarchyPanel.Width` (`const float`, 200) and `HierarchyPanel.Draw(bool editing)`.

No Core changes, so no new tests; the plugin build and the checklist cover it.

- [ ] **Step 1: Write the panel**

`src/Vista.Plugin/Ui/HierarchyPanel.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui;

/// <summary>The scene's tracks: pick one to edit, show or hide, rename, duplicate, delete and reorder them.</summary>
internal sealed unsafe class HierarchyPanel
{
    /// <summary>The compartment's width.</summary>
    public const float Width = 200f;

    private const string TrackPayload = "VISTA_TRACK";

    private readonly CameraSession session;
    private Guid? renaming;
    private string renameText = string.Empty;
    private bool focusRename;

    public HierarchyPanel(CameraSession session) => this.session = session;

    /// <summary>The "Scene" header, one row per track, and + Track; disabled unless editing.</summary>
    public void Draw(bool editing)
    {
        ImGui.TextUnformatted("Scene");
        ImGui.Separator();

        // Rows can delete or reorder tracks, so every row reads this snapshot.
        var scene = session.Scene;
        var edited = session.EditedTrackId;
        ImGui.BeginDisabled(!editing);
        var footer = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("tracks", new Vector2(0f, -footer)))
        {
            for (var i = 0; i < scene.Tracks.Count; i++) DrawRow(scene, scene.Tracks[i], i, edited, editing);
        }

        ImGui.EndChild();
        if (ImGui.Button("+ Track")) Report(session.AddTrack());
        ImGui.EndDisabled();
    }

    /// <summary>The eye toggle and the name: click edits the track, double-click renames, right-click opens the menu, drag reorders.</summary>
    private void DrawRow(Scene scene, Track track, int index, Guid edited, bool editing)
    {
        using var id = ImRaii.PushId(track.Id.ToString());
        var isEdited = track.Id == edited;
        var hidden = scene.Hidden.Contains(track.Id);

        ImGui.BeginDisabled(isEdited);
        if (IconButton.Draw("eye", hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, hidden ? "Show" : "Hide"))
            Report(session.SetTrackHidden(track.Id, !hidden));
        ImGui.EndDisabled();
        ImGui.SameLine();

        if (renaming == track.Id)
        {
            DrawRename(track);
            return;
        }

        if (ImGui.Selectable(track.Name, isEdited, ImGuiSelectableFlags.None, new Vector2(0f, ImGui.GetFrameHeight())))
            Report(session.OpenTrack(track.Id));
        if (editing && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) StartRename(track);

        if (editing && ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(TrackPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));
            ImGui.TextUnformatted(track.Name);
            ImGui.EndDragDropSource();
        }

        if (editing && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(TrackPayload);
            if (!payload.IsNull && *(int*)payload.Handle->Data is var from && from != index) Report(session.MoveTrack(from, index));
            ImGui.EndDragDropTarget();
        }

        if (editing && ImGui.BeginPopupContextItem("track-menu"))
        {
            var ticked = false;
            if (ImGui.MenuItem("Rename", string.Empty, ref ticked)) StartRename(track);
            if (ImGui.MenuItem("Duplicate", string.Empty, ref ticked)) Report(session.DuplicateTrack(track.Id));
            if (ImGui.MenuItem("Delete", string.Empty, ref ticked, scene.Tracks.Count > 1)) Report(session.DeleteTrack(track.Id));
            ImGui.EndPopup();
        }
    }

    /// <summary>The name as a text field; Enter or clicking away renames, Escape cancels.</summary>
    private void DrawRename(Track track)
    {
        if (focusRename)
        {
            ImGui.SetKeyboardFocusHere();
            focusRename = false;
        }

        ImGui.SetNextItemWidth(-1f);
        var entered = ImGui.InputText("##rename", ref renameText, 64, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            renaming = null;
            return;
        }

        if (!entered && !ImGui.IsItemDeactivated()) return;
        Report(session.RenameTrack(track.Id, renameText));
        renaming = null;
    }

    private void StartRename(Track track)
    {
        renaming = track.Id;
        renameText = track.Name;
        focusRename = true;
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
```

If a binding overload differs from what this code calls (for example `MenuItem` or `InputText`), use the matching overload already used elsewhere in `src/Vista.Plugin/Ui/` and note it in the report.

- [ ] **Step 2: Put the panel in the main window**

In `src/Vista.Plugin/Ui/TrackEditorWindow.cs`:

1. Add fields and construct the panel:

```csharp
    private readonly HierarchyPanel hierarchy;
    private bool showHierarchy = true;
    private float pendingWidth;
```

   and in the constructor, `hierarchy = new HierarchyPanel(session);`.

2. `PreDraw` becomes:

```csharp
    /// <summary>Widens the minimum size to fit the track row and any open compartment, so Clear track stays on screen.</summary>
    public override void PreDraw() => SetMinimumWidth(MathF.Max(MinWidth, TrackRowWidth()) + CompartmentsWidth());
```

   and add:

```csharp
    /// <summary>The width the open compartments beside the track editor take, with their gap.</summary>
    private float CompartmentsWidth() => showHierarchy ? HierarchyPanel.Width + Spacing.X : 0f;
```

3. `Draw` becomes:

```csharp
    public override void Draw()
    {
        if (session.Mode != lastMode)
        {
            fields.Clear();
            lastMode = session.Mode;
        }

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Spacing);
        var editing = session.Mode == CameraMode.Editing;
        DrawTopRow(editing);

        if (showHierarchy)
        {
            if (ImGui.BeginChild("hierarchy", new Vector2(HierarchyPanel.Width, 0f), true)) hierarchy.Draw(editing);
            ImGui.EndChild();
            ImGui.SameLine();
        }

        if (ImGui.BeginChild("track-editor", Vector2.Zero))
        {
            ImGui.BeginDisabled(!editing);
            DrawTrackRow();
            ImGui.EndDisabled();
            ImGui.Separator();

            DrawPoints(editing);
            ImGui.Separator();
            DrawScrubRow(editing);
        }

        ImGui.EndChild();

        // Showing or hiding a compartment grows or shrinks the window by its width, so the track editor keeps its size.
        if (pendingWidth != 0f)
        {
            ImGui.SetWindowSize(ImGui.GetWindowSize() + new Vector2(pendingWidth, 0f));
            pendingWidth = 0f;
        }
    }
```

4. At the start of `DrawTopRow`, before `DrawModeCombo();`, add the toggle:

```csharp
        var colour = showHierarchy ? (uint?)null : ImGui.GetColorU32(ImGuiCol.Text, 0.4f);
        if (IconButton.Draw("hierarchy", FontAwesomeIcon.Sitemap, showHierarchy ? "Hide hierarchy" : "Show hierarchy", colour))
        {
            showHierarchy = !showHierarchy;
            pendingWidth += showHierarchy ? HierarchyPanel.Width + Spacing.X : -(HierarchyPanel.Width + Spacing.X);
        }

        ImGui.SameLine();
```

5. Change the class summary to `/// <summary>The main Vista window: modes, the Hierarchy, track settings, the point list and the scrub bar.</summary>`.

The points child inside the track editor keeps its `-footer` height, which is now relative to the track-editor child. Everything that aligned to the content region still does, since the child reports its own region.

- [ ] **Step 3: Build**

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors.

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes.

- [ ] **Step 4: Commit**

```bash
git add src/Vista.Plugin/Ui/HierarchyPanel.cs src/Vista.Plugin/Ui/TrackEditorWindow.cs
git commit -m "feat(ui) add the hierarchy compartment to the vista window"
```

---

### After the tasks: the checklist

The controller writes `CHECKLIST.md` in the repo root (untracked) after the final review, with a falsifiable pass condition and a Notes line per check, keys written as words. It covers: the Hierarchy toggle and window width; add, rename (double-click and menu, Enter, Escape, empty name), duplicate, delete (and the last track), drag reorder; clicking a row flies to the first point; hide and show, the edited track's eye disabled, a hidden track's row showing it; other tracks drawn grey with numbers and the edited track on top; clicking another track's marker switches and selects without moving the camera; undo across tracks switching back; Clear track keeping the name; Live playing the edited track and the Hierarchy disabled while live.
