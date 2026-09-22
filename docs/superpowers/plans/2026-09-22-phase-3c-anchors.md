# Phase 3.c Anchors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The scene and each track get an anchor (position and yaw). Points are stored relative to their track's anchor, and track anchors relative to the scene's. Anchors are placed on a first point, can be clicked, moved and turned (Alt moves the anchor alone), edited in the Point window, flown to, and the scene can be brought to the camera.

**Architecture:** Six tasks in four waves:
- **Wave 1, in parallel:** Task 1 adds the `Anchor` math. Task 2 teaches the hit test about anchor markers.
- **Wave 2:** Task 3 adds anchors to `Track` and `Scene`, plus the pure `SceneGeometry` (world view, placement, moves, Bring scene, the fly-to view).
- **Wave 3:** Task 4 splits `SessionState` into local storage and a world view, places anchors on first points, and adds anchor selection, moves and live anchor edits. `CameraSession` gets the wrappers, the foot height, flying to an anchor and Bring scene to me.
- **Wave 4, in parallel:** Task 5 draws anchors, clicks them and adds the anchor gizmo. Task 6 adds the Point window's anchor mode and the Hierarchy's anchor buttons.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`, `Dalamud.Bindings.ImGuizmo`.

**Spec:** `docs/superpowers/specs/2026-09-22-anchors-design.md`. Builds on `2026-09-22-scenes-and-tracks-design.md`.

## Global Constraints

- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only. Never create a `Vista.Plugin.Camera` namespace.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only, with no `until … sleep` loops.
- Doc comments are one line.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`. Check `git status` after committing. Do not push.
- Refusals are returned as a `string?` reason (null means done) and logged by the UI. Core signals invalid edits by throwing `ArgumentException`.
- Everything that changes the scene is refused unless editing (Live, paused included).
- Never write a game field that the game saves; this phase writes nothing new to the game. The character's position is only read.
- In a self-sizing window, never align to the window's own width. Measure icon buttons with `IconButton.Width`.

## Technical rulings (the cost if wrong is in brackets)

- **Yaw turns a horizontal offset `(x, z)` to `(x·cos a + z·sin a, z·cos a − x·sin a)`.** This matches the game's look direction `(−sin yaw, ·, −cos yaw)` (`FreeCamMotion.Direction`), so turning a point's position and adding to its yaw agree. Task 1 tests this against `FreeCamMotion.LookAtFrom`. [If wrong: flip the sign of `sin a`.]
- **`Anchor` lives in `Vista.Core.Tracks`**, since both `Track` and `Scene` hold one. [If wrong: move the file.]
- **`Track` and `Scene` gain `Anchor Anchor = default` and `bool AnchorPlaced = false` as trailing positional members.** `default(Anchor)` is the origin with yaw 0. [If wrong: none; a placement detail.]
- **World views are cached per track** in `SessionState.WorldOf`, keyed by the local track instance and the scene anchor. Callers (the evaluator, the overlay's per-track caches) compare by reference, so a view must stay the same instance until something changes. With both anchors at the origin, the world view is the local track itself. [If wrong: caches rebuild every frame; a performance fix only.]
- **An anchor that hasn't been placed doesn't draw, can't be clicked, and its Hierarchy button is disabled.** Moving it (Bring scene to me, or a move) marks it placed. [If wrong: draw unplaced anchors at the origin.]
- **The foot height comes from `IObjectTable.LocalPlayer.Position.Y`,** injected into `SessionState` as a `Func<float?>`. Null falls back to the point's own height, or for Bring scene to me, the scene anchor's current height. [If wrong: read it elsewhere.]
- **Alt is read with `PhysicalKeys.IsDown(VirtualKey.MENU)`** each frame during an anchor drag, because Dalamud's handling of keys in ImGui isn't reliable for this. [If wrong: use `ImGui.GetIO().KeyAlt`.]
- **An anchor drag is a live edit.** It previews the whole scene each frame from the drag's start (so carried tracks move while dragging) and ends as one undo step. A live edit that ends where it began records no step: it compares every track by value and the scene anchor. [If wrong: a no-op drag records one harmless step.]
- **The anchor gizmo is its own class, `AnchorGizmo`,** sharing the Move/Rotate mode with `PointGizmo`. `PointGizmo` abandons a drag when the track instance changes, which an anchor preview does every frame. [If wrong: merge the two classes.]
- **Click priority:** the edited track's points, then the edited track's anchor, then other tracks' points, then other tracks' anchors, then the scene anchor. [If wrong: reorder the passes in `TrackMarkerHitTest`.]
- **Flying to a track:** the camera goes 5 yalms behind the anchor along its yaw and 3 up, looking at the anchor, and keeps the current field of view. [If wrong: change `SceneGeometry.ViewBack`/`ViewUp`.]

---

### Task 1: Anchor math

**Files:**
- Create: `src/Vista.Core/Tracks/Anchor.cs`
- Test: `tests/Vista.Tests/Tracks/AnchorTests.cs`

**Interfaces:**
- Produces: `readonly record struct Anchor(Vector3 Position, float Yaw)` in `Vista.Core.Tracks`, with:
  - `static readonly Anchor Origin`
  - `static Vector3 Turn(Vector3 offset, float yaw)`
  - `Vector3 ToWorld(Vector3 local)` / `Vector3 ToLocal(Vector3 world)`
  - `ControlPoint ToWorld(ControlPoint local)` / `ControlPoint ToLocal(ControlPoint world)`
  - `Anchor ToWorld(Anchor child)` / `Anchor ToLocal(Anchor worldChild)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AnchorTests
{
    private const float Tolerance = 1e-4f;

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void TheOriginChangesNothing()
    {
        var point = new ControlPoint(new Vector3(3f, 4f, 5f), 0.5f, 0.2f, 1f, 0.1f);
        Assert.Equal(point, Anchor.Origin.ToWorld(point));
        Assert.Equal(point, Anchor.Origin.ToLocal(point));
    }

    [Fact]
    public void AQuarterTurnTurnsTheForwardOffsetTheWayTheLookTurns()
    {
        var anchor = new Anchor(Vector3.Zero, MathF.PI / 2f);
        Near(new Vector3(-1f, 0f, 0f), anchor.ToWorld(new Vector3(0f, 0f, -1f)));
    }

    [Theory]
    [InlineData(0.3f, 0.1f, 0.7f)]
    [InlineData(-2.5f, -0.4f, 1.9f)]
    [InlineData(3.0f, 0.6f, -3.0f)]
    public void TurningAPointAgreesWithAddingToItsYaw(float yaw, float pitch, float turn)
    {
        var look = FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch);
        Near(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw + turn, pitch), Anchor.Turn(look, turn));
    }

    [Fact]
    public void ToLocalUndoesToWorldForPositions()
    {
        var anchor = new Anchor(new Vector3(10f, -2f, 7f), 1.1f);
        var local = new Vector3(3f, 1.5f, -4f);

        Near(local, anchor.ToLocal(anchor.ToWorld(local)));
        Near(new Vector3(10f, -0.5f, 7f) + Anchor.Turn(new Vector3(3f, 0f, -4f), 1.1f), anchor.ToWorld(local));
    }

    [Fact]
    public void APointGainsTheAnchorsYawAndKeepsItsPitchRollAndFov()
    {
        var anchor = new Anchor(new Vector3(1f, 2f, 3f), 0.8f);
        var local = new ControlPoint(new Vector3(0f, 0f, -2f), 0.2f, 0.3f, 1.1f, 0.4f);
        var world = anchor.ToWorld(local);

        Assert.Equal(1.0f, world.Yaw, Tolerance);
        Assert.Equal(0.3f, world.Pitch);
        Assert.Equal(0.4f, world.Roll);
        Assert.Equal(1.1f, world.Fov);
        Near(local.Position, anchor.ToLocal(world).Position);
        Assert.Equal(local.Yaw, anchor.ToLocal(world).Yaw, Tolerance);
    }

    [Fact]
    public void NestedAnchorsComposeAndUndo()
    {
        var scene = new Anchor(new Vector3(100f, 5f, -50f), 0.6f);
        var track = new Anchor(new Vector3(4f, 0f, 2f), -1.3f);
        var point = new Vector3(1f, 2f, 3f);

        var composed = scene.ToWorld(track);
        Near(scene.ToWorld(track.ToWorld(point)), composed.ToWorld(point));
        Assert.Equal(0.6f - 1.3f, composed.Yaw, Tolerance);

        var back = scene.ToLocal(composed);
        Near(track.Position, back.Position);
        Assert.Equal(track.Yaw, back.Yaw, Tolerance);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter AnchorTests`
Expected: the build fails because `Anchor` doesn't exist.

- [ ] **Step 3: Write the implementation**

`src/Vista.Core/Tracks/Anchor.cs`:

```csharp
using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>A position and a yaw that points, tracks or a scene hang off; no pitch, roll or scale.</summary>
public readonly record struct Anchor(Vector3 Position, float Yaw)
{
    /// <summary>The world origin with yaw 0; changes nothing.</summary>
    public static readonly Anchor Origin = new(Vector3.Zero, 0f);

    /// <summary>Turns a horizontal offset by <paramref name="yaw"/> the way yaw turns the camera's look.</summary>
    public static Vector3 Turn(Vector3 offset, float yaw)
    {
        var cos = MathF.Cos(yaw);
        var sin = MathF.Sin(yaw);
        return new Vector3((offset.X * cos) + (offset.Z * sin), offset.Y, (offset.Z * cos) - (offset.X * sin));
    }

    /// <summary>Where a position local to this anchor sits in the world.</summary>
    public Vector3 ToWorld(Vector3 local) => Position + Turn(local, Yaw);

    /// <summary>A world position as seen from this anchor.</summary>
    public Vector3 ToLocal(Vector3 world) => Turn(world - Position, -Yaw);

    /// <summary>A point local to this anchor, placed in the world: moved, turned, and its yaw added to.</summary>
    public ControlPoint ToWorld(ControlPoint local) => local with { Position = ToWorld(local.Position), Yaw = local.Yaw + Yaw };

    /// <summary>A world point as seen from this anchor.</summary>
    public ControlPoint ToLocal(ControlPoint world) => world with { Position = ToLocal(world.Position), Yaw = world.Yaw - Yaw };

    /// <summary>An anchor local to this one, placed in the world.</summary>
    public Anchor ToWorld(Anchor child) => new(ToWorld(child.Position), child.Yaw + Yaw);

    /// <summary>A world anchor as seen from this one.</summary>
    public Anchor ToLocal(Anchor worldChild) => new(ToLocal(worldChild.Position), worldChild.Yaw - Yaw);
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Core/Tracks/Anchor.cs tests/Vista.Tests/Tracks/AnchorTests.cs
git commit -m "feat(tracks) add anchors with local and world conversions"
```

---

### Task 2: Anchor markers in the hit test

**Files:**
- Modify: `src/Vista.Core/Editing/TrackMarkerHitTest.cs`
- Test: `tests/Vista.Tests/Editing/TrackMarkerHitTestTests.cs`

**Interfaces:**
- Produces: `enum MarkerKind { Point, TrackAnchor, SceneAnchor }`; `TrackMarker(Guid Track, int Point, Vector2? Screen, MarkerKind Kind = MarkerKind.Point)`. `Nearest` keeps its signature and applies the priority from the Technical rulings. A scene anchor marker uses `Guid.Empty` and point −1; a track anchor marker uses its track and point −1.

- [ ] **Step 1: Write the failing tests** (add to `TrackMarkerHitTestTests.cs`)

```csharp
    [Fact]
    public void APointBeatsItsOwnTracksAnchor()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, -1, new Vector2(100f, 100f), MarkerKind.TrackAnchor),
            new TrackMarker(Edited, 0, new Vector2(106f, 100f)),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(101f, 100f), 10f));
    }

    [Fact]
    public void TheEditedTracksAnchorBeatsAnotherTracksPoint()
    {
        var markers = new[]
        {
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
            new TrackMarker(Edited, -1, new Vector2(106f, 100f), MarkerKind.TrackAnchor),
        };
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void AnotherTracksPointBeatsAnotherTracksAnchorAndTheSceneAnchorComesLast()
    {
        var markers = new[]
        {
            new TrackMarker(Guid.Empty, -1, new Vector2(100f, 100f), MarkerKind.SceneAnchor),
            new TrackMarker(Other, -1, new Vector2(103f, 100f), MarkerKind.TrackAnchor),
            new TrackMarker(Other, 2, new Vector2(107f, 100f)),
        };
        Assert.Equal(2, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers[..2], Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(0, TrackMarkerHitTest.Nearest(markers[..1], Edited, new Vector2(100f, 100f), 10f));
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter TrackMarkerHitTestTests`
Expected: the build fails because `MarkerKind` doesn't exist.

- [ ] **Step 3: Write the implementation**

Replace `src/Vista.Core/Editing/TrackMarkerHitTest.cs` with:

```csharp
using System.Numerics;

namespace Vista.Core.Editing;

/// <summary>What a marker on screen stands for.</summary>
public enum MarkerKind { Point, TrackAnchor, SceneAnchor }

/// <summary>One marker on screen; <see cref="Screen"/> is null when off screen, and anchors use point −1.</summary>
public readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen, MarkerKind Kind = MarkerKind.Point);

/// <summary>Finds which marker a click landed on when several tracks and their anchors are drawn.</summary>
public static class TrackMarkerHitTest
{
    /// <summary>The index of the hit marker, or null: edited points, the edited anchor, other points, other anchors, then the scene anchor.</summary>
    public static int? Nearest(IReadOnlyList<TrackMarker> markers, Guid edited, Vector2 cursor, float radius)
        => NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.Point && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.TrackAnchor && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.Point && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.TrackAnchor && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.SceneAnchor, laterWinsTie: false);

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
Expected: every test passes (the existing four hit-test tests use points only), 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Core/Editing/TrackMarkerHitTest.cs tests/Vista.Tests/Editing/TrackMarkerHitTestTests.cs
git commit -m "feat(editing) hit-test track and scene anchors"
```

---

### Task 3: Anchors on tracks and scenes, and the scene geometry

Starts once Task 1 is on `main`.

**Files:**
- Modify: `src/Vista.Core/Tracks/Track.cs`, `src/Vista.Core/Tracks/TrackEditing.cs` (`Clear`), `src/Vista.Core/Scenes/Scene.cs`
- Create: `src/Vista.Core/Scenes/SceneGeometry.cs`
- Test: `tests/Vista.Tests/Scenes/SceneGeometryTests.cs`, `tests/Vista.Tests/Tracks/TrackEditingTests.cs`, `tests/Vista.Tests/Scenes/SceneEditingTests.cs`

**Interfaces:**
- Consumes: `Anchor` (Task 1).
- Produces:
  - `record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop, Anchor Anchor = default, bool AnchorPlaced = false)`
  - `record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden, Anchor Anchor = default, bool AnchorPlaced = false)`
  - `TrackEditing.Clear` keeps `Anchor` and `AnchorPlaced` as well as the Id and Name.
  - `static class SceneGeometry` (`Vista.Core.Scenes`):
    - `const float ViewBack = 5f`, `const float ViewUp = 3f`
    - `Anchor WorldAnchor(Scene scene, Track track)`
    - `Track InWorld(Scene scene, Track track)`: the track with world points; returns `track` itself when its world anchor is the origin or it has no points. Only its `Points` are meaningful as world values.
    - `Scene PlaceFor(Scene scene, Guid trackId, Vector3 worldPosition, float footHeight)`
    - `Scene MoveSceneAnchor(Scene scene, Anchor to, bool carry)`
    - `Scene MoveTrackAnchor(Scene scene, Guid trackId, Anchor toWorld, bool carry)`
    - `(Vector3 Position, Vector3 LookAt) ViewOf(Anchor worldAnchor)`

- [ ] **Step 1: Write the failing tests**

Add to `TrackEditingTests.cs`:

```csharp
    [Fact]
    public void ClearKeepsTheAnchor()
    {
        var anchor = new Anchor(new Vector3(1f, 2f, 3f), 0.5f);
        var track = TrackEditing.Append(TrackEditing.Empty() with { Anchor = anchor, AnchorPlaced = true }, Point(1f, 2f, 3f));
        var cleared = TrackEditing.Clear(track);

        Assert.Equal(anchor, cleared.Anchor);
        Assert.True(cleared.AnchorPlaced);
    }
```

Add to `SceneEditingTests.cs`:

```csharp
    [Fact]
    public void DuplicateCopiesTheAnchor()
    {
        var scene = SceneEditing.New();
        var anchor = new Anchor(new Vector3(4f, 0f, 1f), 1.2f);
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { Anchor = anchor, AnchorPlaced = true });
        var (result, _) = SceneEditing.Duplicate(scene, scene.Tracks[0].Id);

        Assert.Equal(anchor, result.Tracks[1].Anchor);
        Assert.True(result.Tracks[1].AnchorPlaced);
    }
```

Create `tests/Vista.Tests/Scenes/SceneGeometryTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Scenes;

public class SceneGeometryTests
{
    private const float Tolerance = 1e-4f;

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    private static ControlPoint Point(float x, float y = 0f, float z = 0f) => new(new Vector3(x, y, z), 0f, 0f, 1f);

    // One track with points at x = 0, 10, 20, local to a track anchor at (5, 0, 0) yaw 0.5, under a scene anchor at (100, 2, 50) yaw 1.
    private static Scene Anchored()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 2f, 50f), 1f), AnchorPlaced = true };
        var track = scene.Tracks[0] with { Anchor = new Anchor(new Vector3(5f, 0f, 0f), 0.5f), AnchorPlaced = true };
        foreach (var x in new[] { 0f, 10f, 20f }) track = TrackEditing.Append(track, Point(x));
        return SceneEditing.Replace(scene, track);
    }

    private static IReadOnlyList<Vector3> WorldPositions(Scene scene)
        => SceneGeometry.InWorld(scene, scene.Tracks[0]).Points.Select(p => p.Position).ToList();

    [Fact]
    public void InWorldPlacesPointsThroughBothAnchors()
    {
        var scene = Anchored();
        var world = SceneGeometry.InWorld(scene, scene.Tracks[0]);
        var anchor = scene.Anchor.ToWorld(scene.Tracks[0].Anchor);

        Near(anchor.ToWorld(new Vector3(10f, 0f, 0f)), world.Points[1].Position);
        Assert.Equal(1.5f, world.Points[1].Yaw, Tolerance);
        Assert.Same(scene.Tracks[0].Timing, world.Timing);
    }

    [Fact]
    public void InWorldIsTheTrackItselfAtTheOrigin()
    {
        var scene = SceneEditing.New();
        var track = TrackEditing.Append(scene.Tracks[0], Point(3f));
        Assert.Same(track, SceneGeometry.InWorld(SceneEditing.Replace(scene, track), track));
    }

    [Fact]
    public void TimingIsTheSameWhereverTheAnchorsAre()
    {
        var scene = Anchored();
        var local = new TrackEvaluator(scene.Tracks[0]);
        var world = new TrackEvaluator(SceneGeometry.InWorld(scene, scene.Tracks[0]));

        Assert.Equal(local.Duration, world.Duration, 4);
        Assert.Equal(local.LegSeconds(2), world.LegSeconds(2), 4);
    }

    [Fact]
    public void PlaceForPutsBothAnchorsUnderTheFirstPointAtFootHeight()
    {
        var scene = SceneEditing.New();
        var placed = SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(7f, 9f, -3f), 2f);

        Assert.True(placed.AnchorPlaced);
        Assert.Equal(new Anchor(new Vector3(7f, 2f, -3f), 0f), placed.Anchor);
        Assert.True(placed.Tracks[0].AnchorPlaced);
        Assert.Equal(Anchor.Origin, placed.Tracks[0].Anchor);
    }

    [Fact]
    public void PlaceForPutsALaterTracksAnchorUnderItsPointRelativeToTheScene()
    {
        var scene = Anchored();
        var (added, id) = SceneEditing.Add(scene);
        var placed = SceneGeometry.PlaceFor(added, id, new Vector3(120f, 8f, 40f), 3f);

        Assert.Equal(scene.Anchor, placed.Anchor);
        Near(new Vector3(120f, 3f, 40f), SceneGeometry.WorldAnchor(placed, SceneEditing.Get(placed, id)).Position);
        Assert.Equal(0f, SceneGeometry.WorldAnchor(placed, SceneEditing.Get(placed, id)).Yaw, Tolerance);
    }

    [Fact]
    public void PlaceForLeavesPlacedAnchorsAlone()
    {
        var scene = Anchored();
        Assert.Same(scene, SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(-9f, 0f, 9f), 0f));
    }

    [Fact]
    public void MovingTheSceneAnchorCarriesEveryTrack()
    {
        var scene = Anchored();
        var to = new Anchor(new Vector3(0f, 0f, 0f), 0f);
        var moved = SceneGeometry.MoveSceneAnchor(scene, to, carry: true);

        Assert.Equal(to, moved.Anchor);
        Assert.Same(scene.Tracks[0], moved.Tracks[0]);
        Near(to.ToWorld(scene.Tracks[0].Anchor).ToWorld(new Vector3(20f, 0f, 0f)), WorldPositions(moved)[2]);
    }

    [Fact]
    public void MovingTheSceneAnchorAloneLeavesEveryPointInTheWorld()
    {
        var scene = Anchored();
        var before = WorldPositions(scene);
        var moved = SceneGeometry.MoveSceneAnchor(scene, new Anchor(new Vector3(-30f, 1f, 8f), -0.7f), carry: false);

        for (var i = 0; i < before.Count; i++) Near(before[i], WorldPositions(moved)[i]);
        Assert.Equal(-0.7f, moved.Anchor.Yaw);
    }

    [Fact]
    public void MovingATrackAnchorCarriesItsPoints()
    {
        var scene = Anchored();
        var to = new Anchor(new Vector3(90f, 2f, 60f), 0.2f);
        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, to, carry: true);

        Near(to.Position, SceneGeometry.WorldAnchor(moved, moved.Tracks[0]).Position);
        Assert.Same(scene.Tracks[0].Points, moved.Tracks[0].Points);
        Near(to.ToWorld(new Vector3(10f, 0f, 0f)), WorldPositions(moved)[1]);
    }

    [Fact]
    public void MovingATrackAnchorAloneLeavesItsPointsInTheWorld()
    {
        var scene = Anchored();
        var before = WorldPositions(scene);
        var worldYawsBefore = SceneGeometry.InWorld(scene, scene.Tracks[0]).Points.Select(p => p.Yaw).ToList();
        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(new Vector3(80f, 0f, 30f), 2f), carry: false);

        var world = SceneGeometry.InWorld(moved, moved.Tracks[0]);
        for (var i = 0; i < before.Count; i++)
        {
            Near(before[i], world.Points[i].Position);
            Assert.Equal(worldYawsBefore[i], world.Points[i].Yaw, Tolerance);
        }
    }

    [Fact]
    public void MovingAnAnchorMarksItPlaced()
    {
        var scene = SceneEditing.New();
        Assert.True(SceneGeometry.MoveSceneAnchor(scene, new Anchor(Vector3.One, 0f), carry: true).AnchorPlaced);
        Assert.True(SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(Vector3.One, 0f), carry: true).Tracks[0].AnchorPlaced);
    }

    [Fact]
    public void TheViewOfAnAnchorIsBehindAndAboveItLookingAtIt()
    {
        var anchor = new Anchor(new Vector3(10f, 0f, 10f), 0f);
        var (position, lookAt) = SceneGeometry.ViewOf(anchor);

        // Yaw 0 looks towards −Z, so behind is +Z.
        Near(new Vector3(10f, SceneGeometry.ViewUp, 10f + SceneGeometry.ViewBack), position);
        Near(anchor.Position, lookAt);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: the build fails because `Track.Anchor`, `Scene.Anchor` and `SceneGeometry` don't exist.

- [ ] **Step 3: Add anchors to Track and Scene**

`Track.cs`:

```csharp
namespace Vista.Core.Tracks;

/// <summary>A camera move: its identity and name, a path through control points local to its anchor, their timing, speed, aim and playback.</summary>
public sealed record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop, Anchor Anchor = default, bool AnchorPlaced = false);
```

In `TrackEditing.cs`, `Clear` becomes:

```csharp
    /// <summary>An empty track that keeps <paramref name="track"/>'s Id, Name and anchor.</summary>
    public static Track Clear(Track track)
        => Empty(name: track.Name) with { Id = track.Id, Anchor = track.Anchor, AnchorPlaced = track.AnchorPlaced };
```

`Scene.cs`:

```csharp
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, which of them are hidden, and the anchor they hang off.</summary>
public sealed record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden, Anchor Anchor = default, bool AnchorPlaced = false);
```

- [ ] **Step 4: Add the scene geometry**

`src/Vista.Core/Scenes/SceneGeometry.cs`:

```csharp
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Where a scene's tracks sit in the world, where anchors start, and how moving them carries what hangs off them.</summary>
public static class SceneGeometry
{
    /// <summary>How far behind an anchor, along its yaw, the camera stops when flying to it.</summary>
    public const float ViewBack = 5f;

    /// <summary>How far above an anchor the camera stops when flying to it.</summary>
    public const float ViewUp = 3f;

    /// <summary>A track's anchor in the world.</summary>
    public static Anchor WorldAnchor(Scene scene, Track track) => scene.Anchor.ToWorld(track.Anchor);

    /// <summary>The track with its points in the world; the track itself when nothing moves it. Only the points are world values.</summary>
    public static Track InWorld(Scene scene, Track track)
    {
        var anchor = WorldAnchor(scene, track);
        if (anchor == Anchor.Origin || track.Points.Count == 0) return track;
        return track with { Points = track.Points.Select(anchor.ToWorld).ToArray() };
    }

    /// <summary>Places the scene's and the track's anchors under a first point at foot height, yaw 0, where not placed yet.</summary>
    public static Scene PlaceFor(Scene scene, Guid trackId, Vector3 worldPosition, float footHeight)
    {
        var ground = new Anchor(worldPosition with { Y = footHeight }, 0f);
        var result = scene.AnchorPlaced ? scene : scene with { Anchor = ground, AnchorPlaced = true };
        var track = SceneEditing.Get(result, trackId);
        if (track.AnchorPlaced) return result;
        return SceneEditing.Replace(result, track with { Anchor = result.Anchor.ToLocal(ground), AnchorPlaced = true });
    }

    /// <summary>Moves the scene anchor to <paramref name="to"/>, carrying every track, or alone so every point stays where it is.</summary>
    public static Scene MoveSceneAnchor(Scene scene, Anchor to, bool carry)
    {
        if (carry) return scene with { Anchor = to, AnchorPlaced = true };
        var tracks = scene.Tracks.Select(t => t with { Anchor = to.ToLocal(scene.Anchor.ToWorld(t.Anchor)) }).ToArray();
        return scene with { Tracks = tracks, Anchor = to, AnchorPlaced = true };
    }

    /// <summary>Moves a track's anchor to <paramref name="toWorld"/>, carrying its points, or alone so they stay where they are.</summary>
    public static Scene MoveTrackAnchor(Scene scene, Guid trackId, Anchor toWorld, bool carry)
    {
        var track = SceneEditing.Get(scene, trackId);
        var moved = track with { Anchor = scene.Anchor.ToLocal(toWorld), AnchorPlaced = true };
        if (!carry)
        {
            var from = WorldAnchor(scene, track);
            moved = moved with { Points = track.Points.Select(p => toWorld.ToLocal(from.ToWorld(p))).ToArray() };
        }

        return SceneEditing.Replace(scene, moved);
    }

    /// <summary>Where the camera goes to look at an anchor: behind it along its yaw and above it.</summary>
    public static (Vector3 Position, Vector3 LookAt) ViewOf(Anchor worldAnchor)
    {
        var forward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, worldAnchor.Yaw, 0f));
        return (worldAnchor.Position - (forward * ViewBack) + new Vector3(0f, ViewUp, 0f), worldAnchor.Position);
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings. Also run `./build.sh`: the plugin must still build (tracks and scenes only gained defaulted members).

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Core/Tracks/Track.cs src/Vista.Core/Tracks/TrackEditing.cs src/Vista.Core/Scenes/Scene.cs src/Vista.Core/Scenes/SceneGeometry.cs tests/Vista.Tests/Scenes/SceneGeometryTests.cs tests/Vista.Tests/Tracks/TrackEditingTests.cs tests/Vista.Tests/Scenes/SceneEditingTests.cs
git commit -m "feat(scenes) anchor tracks and scenes and place them in the world"
```

---

### Task 4: The session works in the world and stores locally

Starts once Tasks 2 and 3 are on `main`.

**Files:**
- Create: `src/Vista.Core/Session/AnchorKind.cs`
- Modify: `src/Vista.Core/Session/SessionState.cs`, `src/Vista.Plugin/Session/CameraSession.cs`, `src/Vista.Plugin/Plugin.cs` (the `IObjectTable` service)
- Test: `tests/Vista.Tests/Session/SessionAnchorTests.cs` (new)

**Interfaces:**
- Consumes: `Anchor` (Task 1), `SceneGeometry.*`, `Track.Anchor`/`AnchorPlaced`, `Scene.Anchor`/`AnchorPlaced` (Task 3).
- Produces:
  - `enum AnchorKind { Scene, Track }` (`Vista.Core.Session`)
  - `SessionState(Func<float?>? footHeight = null)`
  - `SessionState.Track`: the edited track **in the world**. `Scene` keeps local values.
  - `Track SessionState.WorldOf(Track local)`: a scene track in the world, the same instance until it or the scene anchor changes.
  - `AnchorKind? SelectedAnchor`, `Anchor? SelectedAnchorInWorld`
  - `string? SelectSceneAnchor()`, `string? SelectTrackAnchor(Guid id)`, `string? MoveAnchor(Anchor world, bool carry)`, `string? PreviewAnchor(Anchor world, bool carry)` (inside a live edit), `string? BringScene(Vector3 camera)`
  - Every point edit (`AddToEnd`, `AddAfterSelected`, `OverwriteSelected`, `ReplacePoint`, `PreviewPoint`) takes world values.
  - On `CameraSession`: `WorldOf`, `SelectedAnchor`, `SelectedAnchorInWorld`, `SelectSceneAnchor`, `SelectTrackAnchor`, `MoveAnchor`, `PreviewAnchor`, `string? BringSceneToMe()`; `OpenTrack` flies to the anchor.

- [ ] **Step 1: Write the failing tests**

Create `tests/Vista.Tests/Session/SessionAnchorTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionAnchorTests
{
    private const float Tolerance = 1e-4f;

    private static ControlPoint Point(float x, float y = 5f, float z = 0f) => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    // Editing with the character's feet at y = 1; Track 1 has points at x = 10, 20, 30 (y = 5).
    private static SessionState Editing()
    {
        var state = new SessionState(() => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddToEnd(Point(30f));
        return state;
    }

    [Fact]
    public void TheFirstPointPlacesBothAnchorsUnderItAtFootHeight()
    {
        var state = Editing();

        Assert.True(state.Scene.AnchorPlaced);
        Assert.Equal(new Anchor(new Vector3(10f, 1f, 0f), 0f), state.Scene.Anchor);
        Assert.True(state.Scene.Tracks[0].AnchorPlaced);
        Assert.Equal(Anchor.Origin, state.Scene.Tracks[0].Anchor);
        Assert.Equal(new Vector3(0f, 4f, 0f), state.Scene.Tracks[0].Points[0].Position);
        Near(new Vector3(20f, 5f, 0f), state.Track.Points[1].Position);
    }

    [Fact]
    public void WithoutAFootHeightAnchorsSitAtThePointsHeight()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(10f));

        Assert.Equal(new Vector3(10f, 5f, 0f), state.Scene.Anchor.Position);
    }

    [Fact]
    public void ALaterTracksAnchorIsPlacedUnderItsOwnFirstPoint()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(50f, 7f, 5f));

        Assert.Equal(new Vector3(10f, 1f, 0f), state.Scene.Anchor.Position);
        Near(new Vector3(50f, 1f, 5f), SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[1]).Position);
        Near(new Vector3(50f, 7f, 5f), state.Track.Points[0].Position);
    }

    [Fact]
    public void WorldOfKeepsTheSameInstanceUntilSomethingChanges()
    {
        var state = Editing();
        Assert.Same(state.Track, state.Track);
        Assert.Same(state.WorldOf(state.Scene.Tracks[0]), state.Track);
    }

    [Fact]
    public void SelectingAnAnchorClearsThePointAndSelectingAPointClearsTheAnchor()
    {
        var state = Editing();
        state.Select(1);

        Assert.Null(state.SelectSceneAnchor());
        Assert.Equal(AnchorKind.Scene, state.SelectedAnchor);
        Assert.Null(state.Selected);

        state.Select(2);
        Assert.Null(state.SelectedAnchor);

        state.SelectTrackAnchor(state.EditedTrackId);
        state.Select(null);
        Assert.Null(state.SelectedAnchor);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void SelectingAnotherTracksAnchorSwitchesToIt()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));

        Assert.Null(state.SelectTrackAnchor(first));
        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(AnchorKind.Track, state.SelectedAnchor);
    }

    [Fact]
    public void AnUnplacedAnchorCannotBeSelected()
    {
        var state = new SessionState();
        state.Edit();
        Assert.NotNull(state.SelectSceneAnchor());
        Assert.NotNull(state.SelectTrackAnchor(state.EditedTrackId));
        Assert.Null(state.SelectedAnchor);
    }

    [Fact]
    public void MovingTheSceneAnchorCarriesThePointsAsOneUndoStep()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        var before = state.Track.Points[2].Position;

        Assert.Null(state.MoveAnchor(new Anchor(new Vector3(15f, 1f, 3f), 0f), carry: true));

        Near(before + new Vector3(5f, 0f, 3f), state.Track.Points[2].Position);
        Assert.True(state.Undo());
        Near(before, state.Track.Points[2].Position);
    }

    [Fact]
    public void MovingAnAnchorAloneLeavesThePointsInTheWorld()
    {
        var state = Editing();
        state.SelectTrackAnchor(state.EditedTrackId);
        var before = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.MoveAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false));

        for (var i = 0; i < before.Count; i++) Near(before[i], state.Track.Points[i].Position);
        Near(new Vector3(-4f, 0f, 9f), state.SelectedAnchorInWorld!.Value.Position);
    }

    [Fact]
    public void TimingIsUnchangedByTurningTheScene()
    {
        var state = Editing();
        var duration = state.Duration;
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(new Vector3(-200f, 30f, 90f), 2.2f), carry: true);

        Assert.Equal(duration, state.Duration, 4);
    }

    [Fact]
    public void APointReplacedInTheWorldLandsWhereItWasPut()
    {
        var state = Editing();
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(3f, 0f, -2f), 0.9f), carry: true);
        var target = new ControlPoint(new Vector3(12f, 6f, 8f), 0.4f, 0.1f, 1f);

        Assert.Null(state.ReplacePoint(1, target));

        Near(target.Position, state.Track.Points[1].Position);
        Assert.Equal(0.4f, state.Track.Points[1].Yaw, Tolerance);
    }

    [Fact]
    public void AnAnchorDragIsOneUndoStepAndADragBackIsNone()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        var start = state.Scene.Anchor;

        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(12f, 1f, 0f), 0f), carry: true);
        state.PreviewAnchor(new Anchor(new Vector3(14f, 1f, 0f), 0f), carry: true);
        state.EndLiveEdit();
        Assert.Equal(new Vector3(14f, 1f, 0f), state.Scene.Anchor.Position);
        Assert.True(state.Undo());
        Assert.Equal(start, state.Scene.Anchor);

        state.Redo();
        state.Undo();
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(40f, 1f, 0f), 0f), carry: false);
        state.PreviewAnchor(start, carry: false);
        state.EndLiveEdit();

        // No step was recorded: recording one would have cleared the redo left by the Undo above.
        Assert.True(state.CanRedo);
        Assert.Equal(start, state.Scene.Anchor);
    }

    [Fact]
    public void BringSceneMovesTheSceneAnchorToTheCameraAtFootHeightKeepingItsYaw()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(state.Scene.Anchor.Position, 0.6f), carry: true);
        var offset = state.Track.Points[0].Position - state.Scene.Anchor.Position;

        Assert.Null(state.BringScene(new Vector3(-50f, 20f, 70f)));

        Assert.Equal(new Anchor(new Vector3(-50f, 1f, 70f), 0.6f), state.Scene.Anchor);
        Near(new Vector3(-50f, 1f, 70f) + offset, state.Track.Points[0].Position);
        Assert.True(state.Undo());
    }

    [Fact]
    public void ClearKeepsTheAnchorSoTheNextPointIsNotReplaced()
    {
        var state = Editing();
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(0f, 0f, 0f), 0f), carry: false);
        var anchor = state.Scene.Tracks[0].Anchor;

        state.ChangeTrack(TrackEditing.Clear);
        state.AddToEnd(Point(90f));

        Assert.Equal(anchor, state.Scene.Tracks[0].Anchor);
        Near(new Vector3(90f, 5f, 0f), state.Track.Points[0].Position);
    }

    [Fact]
    public void ADuplicateSitsOnTheOriginal()
    {
        var state = Editing();
        state.DuplicateTrack(state.EditedTrackId);

        for (var i = 0; i < 3; i++)
            Near(state.WorldOf(state.Scene.Tracks[0]).Points[i].Position, state.Track.Points[i].Position);
    }

    [Fact]
    public void AnchorEditsAreRefusedUnlessEditing()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        state.Play();

        Assert.NotNull(state.MoveAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        Assert.NotNull(state.BringScene(Vector3.Zero));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter SessionAnchorTests`
Expected: the build fails because the anchor members don't exist on `SessionState`.

- [ ] **Step 3: Add `AnchorKind`**

`src/Vista.Core/Session/AnchorKind.cs`:

```csharp
namespace Vista.Core.Session;

/// <summary>Which anchor is selected: the scene's, or the edited track's.</summary>
public enum AnchorKind { Scene, Track }
```

- [ ] **Step 4: Split local storage from the world view in `SessionState`**

In `src/Vista.Core/Session/SessionState.cs` (add `using System.Numerics;`):

1. Add fields and replace the constructor:

```csharp
    private readonly Func<float?> footHeight;
    private readonly Dictionary<Guid, (Track Local, Anchor Scene, Track World)> worlds = new();

    /// <summary>A session; <paramref name="footHeight"/> reads the character's feet, or null when it can't.</summary>
    public SessionState(Func<float?>? footHeight = null)
    {
        this.footHeight = footHeight ?? (() => null);
        EditedTrackId = Scene.Tracks[0].Id;
    }
```

2. Rename the existing `Track` property (get and private set) to a private `Local`, and replace **every** use of `Track` inside `SessionState` with `Local`, except the two noted below. Then add the public world view:

```csharp
    /// <summary>The edited track as stored, local to its anchor.</summary>
    private Track Local
    {
        get => SceneEditing.Get(Scene, EditedTrackId);
        set => Scene = SceneEditing.Replace(Scene, value);
    }

    /// <summary>The edited track in the world: Edit builds it and Play plays it. Changed only through the edit methods and undo.</summary>
    public Track Track => WorldOf(Local);

    /// <summary>A scene track in the world; the same instance until the track or the scene anchor changes.</summary>
    public Track WorldOf(Track local)
    {
        if (worlds.TryGetValue(local.Id, out var cached) && ReferenceEquals(cached.Local, local) && cached.Scene == Scene.Anchor) return cached.World;
        var world = SceneGeometry.InWorld(Scene, local);
        worlds[local.Id] = (local, Scene.Anchor, world);
        return world;
    }
```

   Keep `Track` (the world view) in exactly these places: `Restart`'s `Director.GoLive(new TrackShot(Track))`, and the `Evaluator` property (`evaluatedTrack`/`new TrackEvaluator(Track)`), so playback, scrubbing and `FrameAt` are in the world. Everywhere else — selection bounds, key counts, commits, live edits, `RefreshTimingSelection`, `SyncKeyToPoint` — uses `Local` (counts and timing are the same either way).

3. Replace `Commit` with a scene-level commit plus a wrapper for track changes, and make `Apply`/`ApplyTiming` go through them:

```csharp
    private string? Apply(Func<Track, Track> change, Func<Track, int?> selectAfter) => ApplyScene(ChangeEdited(change), selectAfter);

    /// <summary>Applies a scene change to the edited track's points, keeping the timing selection in step.</summary>
    private string? ApplyScene(Func<Scene, Scene> change, Func<Track, int?> selectAfter)
    {
        var pointsBefore = Local.Points;
        var refusal = CommitEdit(change, selectAfter);
        if (refusal is null) RefreshTimingSelection(pointsBefore);
        return refusal;
    }

    /// <summary>Applies a timing change, keeping the point selection and any timing selection still in range.</summary>
    private string? ApplyTiming(Func<Track, Track> change)
    {
        var refusal = CommitEdit(ChangeEdited(change), _ => Selected);
        if (refusal is not null) return refusal;
        if (SelectedKey is { } key && key >= TrackEditing.KeyCount(Local)) SelectedKey = null;
        if (SelectedLeg is { } leg && leg >= Local.Points.Count) SelectedLeg = null;
        return null;
    }

    /// <summary>A scene change that applies <paramref name="change"/> to the edited track, refusing one that swaps the track.</summary>
    private Func<Scene, Scene> ChangeEdited(Func<Track, Track> change)
        => scene =>
        {
            var before = SceneEditing.Get(scene, EditedTrackId);
            var result = change(before);
            if (ReferenceEquals(result, before)) return scene;
            if (result.Id != EditedTrackId) throw new ArgumentException("A change cannot replace the track.");
            return SceneEditing.Replace(scene, result);
        };

    /// <summary>Applies a change to the scene as one undo step if the edited track can still be played. Returns why it was refused, or null.</summary>
    private string? CommitEdit(Func<Scene, Scene> change, Func<Track, int?> selectAfter)
    {
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";
        EndLiveEdit();

        try
        {
            var result = change(Scene);
            if (ReferenceEquals(result, Scene)) return null;

            var edited = SceneEditing.Get(result, EditedTrackId);
            _ = new TrackEvaluator(edited);
            history.Record(Current);
            var selected = selectAfter(edited);
            Scene = result;
            Selected = selected;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
```

   `ChangeTrack` keeps calling `Apply(change, …)`: its changes (holds, direction, loop, Clear) don't touch positions, so they work on the local track.

4. Point edits take world values. Add:

```csharp
    /// <summary>A world point as the edited track stores it.</summary>
    private ControlPoint ToLocal(ControlPoint world) => SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world);

    /// <summary>Places the anchors under a first point if needed, then adds the world point to the edited track with <paramref name="add"/>.</summary>
    private Scene WithPoint(Scene scene, ControlPoint world, Func<Track, ControlPoint, Track> add)
    {
        var placed = SceneGeometry.PlaceFor(scene, EditedTrackId, world.Position, footHeight() ?? world.Position.Y);
        var track = SceneEditing.Get(placed, EditedTrackId);
        return SceneEditing.Replace(placed, add(track, SceneGeometry.WorldAnchor(placed, track).ToLocal(world)));
    }
```

   and change the point edits to:

```csharp
    /// <summary>Appends a world point, placing the anchors under a first point; the selection is unchanged.</summary>
    public string? AddToEnd(ControlPoint point)
        => ApplyScene(scene => WithPoint(scene, point, TrackEditing.Append), _ => Selected);

    /// <summary>Inserts a world point after the selected one and selects it.</summary>
    public string? AddAfterSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        var s = Selected!.Value;
        return ApplyScene(scene => WithPoint(scene, point, (t, p) => TrackEditing.InsertAfter(t, s, p)), _ => s + 1);
    }

    /// <summary>Replaces point <paramref name="index"/> with a world point, keeping its timing and the selection.</summary>
    public string? ReplacePoint(int index, ControlPoint point)
        => Apply(t => TrackEditing.Replace(t, index, ToLocal(point)), _ => Selected);
```

   In `PreviewPoint`, replace `TrackEditing.Replace(Track, index, point)` with `TrackEditing.Replace(Local, index, ToLocal(point))` and `Track = result;` with `Local = result;`.

5. Replace `EndLiveEdit` so any live edit — a point, a key, or an anchor — records one step, and none if it came back to the start:

```csharp
    /// <summary>Ends a live edit, recording it as one undo step if the scene changed.</summary>
    public void EndLiveEdit()
    {
        if (liveEditStart is not { } start) return;
        liveEditStart = null;
        if (ReferenceEquals(start.Scene, Scene)) return;

        // Previews rebuild the lists, so compare values: a drag back to the start is no step.
        if (SameValues(start.Scene, Scene)) Scene = start.Scene;
        else history.Record(start);
    }

    /// <summary>True when two scenes hold the same anchor and tracks by value.</summary>
    private static bool SameValues(Scene a, Scene b)
    {
        if (a.Anchor != b.Anchor || a.AnchorPlaced != b.AnchorPlaced || a.Tracks.Count != b.Tracks.Count) return false;
        for (var i = 0; i < a.Tracks.Count; i++)
        {
            var x = a.Tracks[i];
            var y = b.Tracks[i];
            if (ReferenceEquals(x, y)) continue;
            if (x.Id != y.Id || x.Anchor != y.Anchor || x.AnchorPlaced != y.AnchorPlaced || x.Speed != y.Speed
                || !x.Points.SequenceEqual(y.Points) || !x.Timing.SequenceEqual(y.Timing)) return false;
        }

        return true;
    }
```

6. Anchor selection. Add the property and the selections:

```csharp
    /// <summary>The selected anchor, or null; never set together with a selected point.</summary>
    public AnchorKind? SelectedAnchor { get; private set; }

    /// <summary>The selected anchor in the world, or null.</summary>
    public Anchor? SelectedAnchorInWorld => SelectedAnchor switch
    {
        AnchorKind.Scene => Scene.Anchor,
        AnchorKind.Track => SceneGeometry.WorldAnchor(Scene, Local),
        _ => null,
    };

    /// <summary>Selects the scene anchor, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectSceneAnchor()
    {
        if (Mode != CameraMode.Editing) return "Anchors can only be selected while editing.";
        if (!Scene.AnchorPlaced) return "The scene anchor is placed with the scene's first point.";
        SelectAnchor(AnchorKind.Scene);
        return null;
    }

    /// <summary>Edits track <paramref name="id"/> and selects its anchor, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectTrackAnchor(Guid id)
    {
        if (Mode != CameraMode.Editing) return "Anchors can only be selected while editing.";
        if (SceneEditing.IndexOf(Scene, id) < 0) return "There is no such track.";
        if (!SceneEditing.Get(Scene, id).AnchorPlaced) return "A track's anchor is placed with its first point.";
        if (SwitchTrack(id) is { } refusal) return refusal;
        SelectAnchor(AnchorKind.Track);
        return null;
    }

    private void SelectAnchor(AnchorKind kind)
    {
        EndLiveEdit();
        Selected = null;
        SyncKeyToPoint();
        SelectedAnchor = kind;
    }
```

   In `Select(int? index)`, add `SelectedAnchor = null;` before the point is set (any point selection, and a click on empty space, clears the anchor). In `SelectKey`, when a point key selects its point, also set `SelectedAnchor = null`. In `ClearForSwitch`, add `SelectedAnchor = null;`. In `Restore`, after `Selected = s.Selected;`, add `if (Selected is not null) SelectedAnchor = null;`.

7. Anchor moves:

```csharp
    /// <summary>Moves the selected anchor in the world, carrying what hangs off it or alone. Returns why it was refused, or null.</summary>
    public string? MoveAnchor(Anchor world, bool carry)
    {
        if (SelectedAnchor is not { } kind) return "Select an anchor first.";
        return CommitScene(scene => (Moved(scene, kind, world, carry), EditedTrackId));
    }

    /// <summary>During a live edit, moves the selected anchor from where it was when the edit began. Returns why it was refused, or null.</summary>
    public string? PreviewAnchor(Anchor world, bool carry)
    {
        if (liveEditStart is not { } start) return "No live edit is in progress.";
        if (SelectedAnchor is not { } kind) return "Select an anchor first.";
        Scene = Moved(start.Scene, kind, world, carry);
        return null;
    }

    /// <summary>Moves the scene anchor to the camera's X and Z at foot height, keeping its yaw and carrying every track. Returns why it was refused, or null.</summary>
    public string? BringScene(Vector3 camera)
    {
        var height = footHeight() ?? Scene.Anchor.Position.Y;
        return CommitScene(scene => (SceneGeometry.MoveSceneAnchor(scene, scene.Anchor with { Position = new Vector3(camera.X, height, camera.Z) }, carry: true), EditedTrackId));
    }

    private Scene Moved(Scene scene, AnchorKind kind, Anchor world, bool carry)
        => kind == AnchorKind.Scene
            ? SceneGeometry.MoveSceneAnchor(scene, world, carry)
            : SceneGeometry.MoveTrackAnchor(scene, EditedTrackId, world, carry);
```

8. Run `grep -n "\bTrack\b" src/Vista.Core/Session/SessionState.cs` and check each remaining `Track` use is the type name, the public property, `WorldOf`, `Restart`'s `TrackShot`, or `Evaluator`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings. Existing tests read `state.Track` (now the world view); with points at small whole numbers and yaw 0 the round trip is exact. If an existing test fails only because of float round-off in a world position, switch that assertion to a tolerance and list it in the report. Any other failure is a real problem: report it.

- [ ] **Step 6: Wire `CameraSession`**

In `src/Vista.Plugin/Plugin.cs`, add the service next to the others:

```csharp
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
```

In `src/Vista.Plugin/Session/CameraSession.cs`:

- The state becomes `private readonly SessionState state = new(() => Plugin.ObjectTable.LocalPlayer?.Position.Y);`
- Add:

```csharp
    /// <summary>A scene track in the world.</summary>
    public Track WorldOf(Track local) => state.WorldOf(local);

    /// <summary>The selected anchor, or null.</summary>
    public AnchorKind? SelectedAnchor => state.SelectedAnchor;

    /// <summary>The selected anchor in the world, or null.</summary>
    public Anchor? SelectedAnchorInWorld => state.SelectedAnchorInWorld;

    /// <summary>Selects the scene anchor. Returns why it was refused, or null.</summary>
    public string? SelectSceneAnchor() => state.SelectSceneAnchor();

    /// <summary>Edits a track and selects its anchor, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SelectTrackAnchor(Guid id) => state.SelectTrackAnchor(id);

    /// <summary>Moves the selected anchor. Returns why it was refused, or null.</summary>
    public string? MoveAnchor(Anchor world, bool carry) => state.MoveAnchor(world, carry);

    /// <summary>During a live edit, moves the selected anchor. Returns why it was refused, or null.</summary>
    public string? PreviewAnchor(Anchor world, bool carry) => state.PreviewAnchor(world, carry);

    /// <summary>Moves the scene anchor to the editor camera, at the character's feet. Returns why it was refused, or null.</summary>
    public string? BringSceneToMe()
        => CameraAccess.ReadState() is { } camera ? state.BringScene(camera.Position) : "Cannot read the camera.";
```

- `OpenTrack` flies to the track's anchor instead of its first point:

```csharp
    /// <summary>Edits a track and flies the editor camera to look at its anchor. Returns why it was refused, or null.</summary>
    public string? OpenTrack(Guid id)
    {
        var refusal = state.SwitchTrack(id);
        if (refusal is not null) return refusal;

        var track = SceneEditing.Get(state.Scene, id);
        if (!track.AnchorPlaced) return null;
        var (position, lookAt) = SceneGeometry.ViewOf(SceneGeometry.WorldAnchor(state.Scene, track));
        var fov = CameraAccess.ReadState()?.Fov ?? lastFrame?.Fov ?? 1f;
        FlyFrom(new CameraState(position, lookAt, fov));
        return null;
    }
```

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Vista.Core/Session/AnchorKind.cs src/Vista.Core/Session/SessionState.cs src/Vista.Plugin/Session/CameraSession.cs src/Vista.Plugin/Plugin.cs tests/Vista.Tests/Session/SessionAnchorTests.cs
git commit -m "feat(session) store tracks under anchors and edit them in the world"
```

(Add any existing test files you changed for round-off to the `git add`.)

---

### Task 5: Draw, click and drag anchors

Starts once Task 4 is on `main`. Runs in parallel with Task 6.

**Files:**
- Create: `src/Vista.Plugin/Editor/AnchorGizmo.cs`
- Modify: `src/Vista.Plugin/Editor/EditorColours.cs`, `src/Vista.Plugin/Editor/Overlay.cs`, `src/Vista.Plugin/Editor/EditorLayer.cs`

**Interfaces:**
- Consumes: `CameraSession.WorldOf`, `SelectedAnchor`, `SelectedAnchorInWorld`, `SelectSceneAnchor`, `SelectTrackAnchor`, `BeginLiveEdit`, `PreviewAnchor`, `EndLiveEdit`, `Scene`, `EditedTrackId`, `Track` (Task 4); `MarkerKind`, `TrackMarker` (Task 2); `SceneGeometry.WorldAnchor` (Task 3); `PointGizmo.Mode`.
- Produces: `Overlay.DrawTrackAnchor(EditorView view, Anchor world, Vector3? firstPoint, bool edited, bool selected) → Vector2?`, `Overlay.DrawSceneAnchor(EditorView view, Anchor world, bool selected) → Vector2?`, `AnchorGizmo(PointGizmo points)` with `bool Hot` and `void Draw(EditorView view, CameraSession session)`.

No Core changes, so no new tests; the build and the checklist cover it.

- [ ] **Step 1: Colours**

In `EditorColours.cs`, add:

```csharp
    public const uint Anchor = 0xE0F0C040;
    public const uint OtherAnchor = 0x78A0A0A0;
    public const uint AnchorLink = 0x60F0C040;
    public const uint OtherAnchorLink = 0x40A0A0A0;
    public const uint SceneAnchor = 0xF0FF60C0;
```

- [ ] **Step 2: Draw anchors in the overlay**

Add to `Overlay.cs`:

```csharp
    private const float AnchorRadius = 0.5f;
    private const float AnchorArrow = 0.9f;
    private const float SceneAnchorRadius = 1f;
    private const float SceneAnchorArrow = 1.6f;
    private const int AnchorSegments = 24;

    /// <summary>A track anchor: a ground ring, an arrow along its yaw and a faint line to the first point. Returns its centre on screen, or null.</summary>
    public Vector2? DrawTrackAnchor(EditorView view, Anchor world, Vector3? firstPoint, bool edited, bool selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        var colour = selected ? EditorColours.Selected : edited ? EditorColours.Anchor : EditorColours.OtherAnchor;
        if (firstPoint is { } first) DrawEdge(list, view, world.Position, first, edited ? EditorColours.AnchorLink : EditorColours.OtherAnchorLink, GlyphThickness);

        for (var i = 0; i < AnchorSegments; i++)
        {
            var a = MathF.Tau * i / AnchorSegments;
            var b = MathF.Tau * (i + 1) / AnchorSegments;
            DrawEdge(list, view, world.Position + Ring(a, AnchorRadius), world.Position + Ring(b, AnchorRadius), colour, selected ? SelectedGlyphThickness : GlyphThickness);
        }

        DrawArrow(list, view, world, AnchorArrow, colour, selected ? SelectedGlyphThickness : GlyphThickness);
        return view.ToScreen(world.Position);
    }

    /// <summary>The scene anchor: a ground diamond and an arrow along its yaw. Returns its centre on screen, or null.</summary>
    public Vector2? DrawSceneAnchor(EditorView view, Anchor world, bool selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        var colour = selected ? EditorColours.Selected : EditorColours.SceneAnchor;
        var thickness = selected ? SelectedGlyphThickness : PathThickness;
        for (var i = 0; i < 4; i++)
        {
            var a = world.Yaw + (MathF.PI / 2f * i);
            var b = world.Yaw + (MathF.PI / 2f * (i + 1));
            DrawEdge(list, view, world.Position + Ring(a, SceneAnchorRadius), world.Position + Ring(b, SceneAnchorRadius), colour, thickness);
        }

        DrawArrow(list, view, world, SceneAnchorArrow, colour, thickness);
        return view.ToScreen(world.Position);
    }

    /// <summary>An offset on the ground at angle <paramref name="angle"/>, measured like yaw.</summary>
    private static Vector3 Ring(float angle, float radius) => Anchor.Turn(new Vector3(0f, 0f, -radius), angle);

    private static void DrawArrow(ImDrawListPtr list, EditorView view, Anchor world, float length, uint colour, float thickness)
    {
        var tip = world.Position + Ring(world.Yaw, length);
        DrawEdge(list, view, world.Position, tip, colour, thickness);
        DrawEdge(list, view, tip, world.Position + Ring(world.Yaw - 0.4f, length * 0.7f), colour, thickness);
        DrawEdge(list, view, tip, world.Position + Ring(world.Yaw + 0.4f, length * 0.7f), colour, thickness);
    }
```

- [ ] **Step 3: The anchor gizmo**

`src/Vista.Plugin/Editor/AnchorGizmo.cs`:

```csharp
using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Game.ClientState.Keys;

namespace Vista.Plugin.Editor;

/// <summary>The move gizmo and yaw ring on the selected anchor; a drag previews live and holding Alt moves the anchor alone.</summary>
internal sealed unsafe class AnchorGizmo
{
    private const int MoveId = 10;
    private const int YawId = 11;

    private readonly PointGizmo points;
    private Matrix4x4 matrix;
    private Anchor? dragStart;
    private AnchorKind? dragKind;
    private Guid dragTrack;
    private bool waitForRelease;

    public AnchorGizmo(PointGizmo points) => this.points = points;

    /// <summary>True when the cursor was over the gizmo, or dragging it, at the last draw.</summary>
    public bool Hot { get; private set; }

    /// <summary>Abandons a drag in progress, ending its live edit, for leaving editing mode.</summary>
    public void Cancel(CameraSession session)
    {
        if (dragStart is not null) session.EndLiveEdit();
        dragStart = null;
        Hot = false;
    }

    /// <summary>Draws the gizmo on the selected anchor into the current window. Call inside the editor window.</summary>
    public void Draw(EditorView view, CameraSession session)
    {
        if (dragStart is not null && (session.SelectedAnchor != dragKind || session.EditedTrackId != dragTrack))
        {
            session.EndLiveEdit();
            dragStart = null;
            waitForRelease = true;
            Reset();
        }

        if (session.SelectedAnchor is not { } kind || session.SelectedAnchorInWorld is not { } anchor)
        {
            Hot = false;
            dragStart = null;
            return;
        }

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(view.Origin.X, view.Origin.Y, view.Size.X, view.Size.Y);
        ImGuizmo.AllowAxisFlip(false);

        var shown = dragStart ?? anchor;
        if (dragStart is null) matrix = PoseMatrix.From(shown.Position, shown.Yaw, 0f, 0f);
        var rotate = points.Mode == GizmoMode.Rotate;
        ImGuizmo.SetID(rotate ? YawId : MoveId);
        Manipulate(view, rotate ? ImGuizmoOperation.RotateY : ImGuizmoOperation.Translate, rotate ? ImGuizmoMode.Local : ImGuizmoMode.World);
        var usingNow = ImGuizmo.IsUsing();
        Hot = usingNow || ImGuizmo.IsOver();

        if (waitForRelease)
        {
            if (!usingNow) waitForRelease = false;
            return;
        }

        if (usingNow)
        {
            if (dragStart is null)
            {
                dragStart = anchor;
                dragKind = kind;
                dragTrack = session.EditedTrackId;
                session.BeginLiveEdit();
            }

            var edited = rotate
                ? dragStart.Value with { Yaw = TrackAim.FromDirection(-new Vector3(matrix.M31, matrix.M32, matrix.M33)).Yaw }
                : dragStart.Value with { Position = matrix.Translation };
            var carry = !PhysicalKeys.IsDown(VirtualKey.MENU);
            if (session.PreviewAnchor(edited, carry) is { } refusal) Plugin.Log.Warning("[editor] anchor drag refused: {Refusal}", refusal);
            return;
        }

        if (dragStart is not null)
        {
            dragStart = null;
            session.EndLiveEdit();
        }
    }

    private void Manipulate(EditorView view, ImGuizmoOperation operation, ImGuizmoMode space)
    {
        var gizmoView = view.GizmoView;
        var gizmoProjection = view.GizmoProjection;
        fixed (float* m = &matrix.M11)
            ImGuizmo.Manipulate(&gizmoView.M11, &gizmoProjection.M11, operation, space, m, null, null, null, null);
    }

    private static void Reset()
    {
        ImGuizmo.Enable(false);
        ImGuizmo.Enable(true);
    }
}
```

If `PhysicalKeys.IsDown` has a different signature, use it as `EditorLayer` does for `VirtualKey.LBUTTON`. If `VirtualKey.MENU` isn't the name for Alt in `Dalamud.Game.ClientState.Keys.VirtualKey`, use the enum's Alt member and note it.

- [ ] **Step 4: Draw anchors and click them in the editor layer**

In `EditorLayer.cs`:

1. Add `private readonly AnchorGizmo anchorGizmo;` and in the constructor `anchorGizmo = new AnchorGizmo(gizmo);`. Where `Draw` leaves editing mode (`clicks.Reset(); gizmo.Cancel(); return;`), also call `anchorGizmo.Cancel(session);`.
2. In the drawing block:
   - Draw other tracks with `session.WorldOf(other)` in place of `other`, and after each other track, if `other.AnchorPlaced`, draw its anchor and add its marker:

```csharp
            var otherWorld = session.WorldOf(other);
            AddMarkers(markers, other.Id, overlay.Draw(view, otherWorld, null, edited: false));
            if (other.AnchorPlaced)
                markers.Add(new TrackMarker(other.Id, -1, overlay.DrawTrackAnchor(view, SceneGeometry.WorldAnchor(scene, other), FirstPosition(otherWorld), edited: false, selected: false), MarkerKind.TrackAnchor));
```

   - After the edited track, draw its anchor (selected when `session.SelectedAnchor == AnchorKind.Track`) and then the scene anchor (if `scene.AnchorPlaced`, selected when `session.SelectedAnchor == AnchorKind.Scene`), adding a `MarkerKind.TrackAnchor` marker for the edited track and a `new TrackMarker(Guid.Empty, -1, screen, MarkerKind.SceneAnchor)` for the scene:

```csharp
        var editedLocal = SceneEditing.Get(scene, edited);
        if (editedLocal.AnchorPlaced)
            markers.Add(new TrackMarker(edited, -1, overlay.DrawTrackAnchor(view, SceneGeometry.WorldAnchor(scene, editedLocal), FirstPosition(track), edited: true, selected: session.SelectedAnchor == AnchorKind.Track), MarkerKind.TrackAnchor));
        if (scene.AnchorPlaced)
            markers.Add(new TrackMarker(Guid.Empty, -1, overlay.DrawSceneAnchor(view, scene.Anchor, session.SelectedAnchor == AnchorKind.Scene), MarkerKind.SceneAnchor));
```

   - Add the helper `private static Vector3? FirstPosition(Track world) => world.Points.Count > 0 ? world.Points[0].Position : null;`.
3. Draw the anchor gizmo after the point gizmo, and treat either as hot:

```csharp
            gizmo.Draw(view, session);
            anchorGizmo.Draw(view, session);
```

   In the input-capture flag and the click update, use `gizmo.Hot || anchorGizmo.Hot` wherever `gizmo.Hot` appears.
4. In `Apply`, handle the kinds:

```csharp
            case ClickKind.Select when outcome.Index < markers.Count:
                var hit = markers[outcome.Index];
                var refusal = hit.Kind switch
                {
                    MarkerKind.SceneAnchor => session.SelectSceneAnchor(),
                    MarkerKind.TrackAnchor => session.SelectTrackAnchor(hit.Track),
                    _ when hit.Track == session.EditedTrackId => Select(hit.Point),
                    _ => session.SelectPoint(hit.Track, hit.Point),
                };
                Report(refusal);
                break;
```

   with `private string? Select(int point) { session.Select(point); return null; }`.

Add the `using`s this needs (`Vista.Core.Scenes`, `Vista.Core.Session`).

- [ ] **Step 5: Build**

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors. Run `dotnet test tests/Vista.Tests/Vista.Tests.csproj`: every test passes.

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Plugin/Editor/AnchorGizmo.cs src/Vista.Plugin/Editor/EditorColours.cs src/Vista.Plugin/Editor/Overlay.cs src/Vista.Plugin/Editor/EditorLayer.cs
git commit -m "feat(editor) draw, click and drag anchors"
```

---

### Task 6: The Point window and the Hierarchy for anchors

Starts once Task 4 is on `main`. Runs in parallel with Task 5.

**Files:**
- Modify: `src/Vista.Plugin/Ui/PointWindow.cs`, `src/Vista.Plugin/Ui/HierarchyPanel.cs`

**Interfaces:**
- Consumes: `CameraSession.SelectedAnchor`, `SelectedAnchorInWorld`, `SelectSceneAnchor`, `SelectTrackAnchor`, `BeginLiveEdit`, `PreviewAnchor`, `EndLiveEdit`, `BringSceneToMe`, `Scene` (Task 4).

No Core changes, so no new tests; the build and the checklist cover it.

- [ ] **Step 1: The Point window shows a selected anchor**

In `PointWindow.cs`:

1. Track what's shown as a small key, so the live edit ends when the selection moves between points and anchors:

```csharp
    private (int? Point, AnchorKind? Anchor, Guid Track) shown;
```

   `PreOpenCheck` becomes:

```csharp
    /// <summary>Opens while a point or an anchor is selected in editing mode, and applies an unfinished edit when the selection moves.</summary>
    public override void PreOpenCheck()
    {
        var editing = session.Mode == CameraMode.Editing;
        var now = (editing ? session.Selected : null, editing ? session.SelectedAnchor : null, session.EditedTrackId);
        if (now != shown) session.EndLiveEdit();
        shown = now;
        IsOpen = now.Item1 is not null || now.Item2 is not null;
        WindowName = now switch
        {
            (_, AnchorKind.Scene, _) => "Scene anchor###vista-point",
            (_, AnchorKind.Track, _) => "Track anchor###vista-point",
            ({ } index, _, _) => $"Point {index + 1}###vista-point",
            _ => WindowName,
        };
    }
```

2. At the start of `Draw`, draw the anchor fields when an anchor is selected:

```csharp
        if (session.SelectedAnchor is not null)
        {
            DrawAnchor();
            return;
        }
```

   and add:

```csharp
    /// <summary>The selected anchor's gizmo mode and its X, Y, Z and Yaw; typing moves it and carries what hangs off it.</summary>
    private void DrawAnchor()
    {
        if (session.SelectedAnchorInWorld is not { } anchor) return;
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Gizmo");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move", gizmo.Mode == GizmoMode.Move)) gizmo.SetMode(GizmoMode.Move);
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", gizmo.Mode == GizmoMode.Rotate)) gizmo.SetMode(GizmoMode.Rotate);

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4f, 3f));
        if (!ImGui.BeginTable("anchor-fields", 4, ImGuiTableFlags.SizingFixedFit)) return;

        ImGui.TableNextRow();
        AnchorField("X", EditorColours.AxisX, "anchor-x", anchor.Position.X, PositionSpeed, "%.2f", (a, v) => a with { Position = a.Position with { X = EditLimits.Coordinate(v, a.Position.X) } });
        AnchorField("Y", EditorColours.AxisY, "anchor-y", anchor.Position.Y, PositionSpeed, "%.2f", (a, v) => a with { Position = a.Position with { Y = EditLimits.Coordinate(v, a.Position.Y) } });
        ImGui.TableNextRow();
        AnchorField("Z", EditorColours.AxisZ, "anchor-z", anchor.Position.Z, PositionSpeed, "%.2f", (a, v) => a with { Position = a.Position with { Z = EditLimits.Coordinate(v, a.Position.Z) } });
        AnchorField("Yaw", EditorColours.AxisY, "anchor-yaw", Degrees(EditLimits.Angle(anchor.Yaw)), AngleSpeed, "%.1f°", (a, v) => a with { Yaw = EditLimits.Angle(Radians(v)) });
        ImGui.EndTable();
    }

    /// <summary>A label and a drag field for the selected anchor: dragging moves it live, carrying what hangs off it, and each drag is one undo step.</summary>
    private void AnchorField(string label, uint colour, string id, float value, float speed, string format, Func<Anchor, float, Anchor> set)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, colour))
            ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(FieldWidth);

        var edited = value;
        var changed = ImGui.DragFloat($"##{id}", ref edited, speed, 0f, 0f, format);
        if (ImGui.IsItemActivated()) session.BeginLiveEdit();
        if (changed && session.SelectedAnchorInWorld is { } current) _ = session.PreviewAnchor(set(current, edited), carry: true);
        if (ImGui.IsItemDeactivated()) session.EndLiveEdit();
    }
```

   Note `PreviewAnchor` moves from where the anchor was when the live edit began; `set(current, edited)` changes one field of the current world anchor, which gives the same result while only one field is dragged at a time.

   Add the `using`s this needs (`Vista.Core.Session`, `Vista.Core.Tracks` is already there).

- [ ] **Step 2: The Hierarchy's anchor buttons and Bring scene to me**

In `HierarchyPanel.cs`:

1. Replace the header line `ImGui.TextUnformatted("Scene");` with the header and its two buttons, right-aligned by measured width:

```csharp
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Scene");
        ImGui.BeginDisabled(!editing);
        var buttons = IconButton.Width(FontAwesomeIcon.Anchor) + IconButton.Width(FontAwesomeIcon.StreetView) + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttons));
        ImGui.BeginDisabled(!session.Scene.AnchorPlaced);
        if (IconButton.Draw("scene-anchor", FontAwesomeIcon.Anchor, "Select scene anchor")) Report(session.SelectSceneAnchor());
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (IconButton.Draw("bring-scene", FontAwesomeIcon.StreetView, "Bring scene to me")) Report(session.BringSceneToMe());
        ImGui.EndDisabled();
```

   (`GetContentRegionAvail` is the compartment's own region, not the window's width.)

2. In `DrawRow`, after the eye button and before the name, add the track's anchor button:

```csharp
        ImGui.BeginDisabled(!track.AnchorPlaced);
        if (IconButton.Draw("anchor", FontAwesomeIcon.Anchor, "Select track anchor")) Report(session.SelectTrackAnchor(track.Id));
        ImGui.EndDisabled();
        ImGui.SameLine();
```

- [ ] **Step 3: Build**

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors. Run `dotnet test tests/Vista.Tests/Vista.Tests.csproj`: every test passes.

- [ ] **Step 4: Commit**

```bash
git add src/Vista.Plugin/Ui/PointWindow.cs src/Vista.Plugin/Ui/HierarchyPanel.cs
git commit -m "feat(ui) edit anchors in the point window and the hierarchy"
```

---

### After the tasks: the checklist

The controller adds a Phase 3.c section to `CHECKLIST.md` after the final review (falsifiable pass conditions, a Notes line each, keys as words), and marks 3.b check 5 as superseded (rows now fly to the anchor). It covers: anchors placed on the first point at foot height; the ring, arrow and link line, grey for other tracks; the scene anchor's diamond; clicking anchors (and another track's anchor switching tracks); click priority over points; the gizmo's Move and yaw-only Rotate with R; a plain drag carrying, Alt moving the anchor alone; each drag one undo step; the Point window's anchor fields; the Hierarchy's anchor buttons disabled until placed; Bring scene to me; clicking a row flying to look at the anchor; Duplicate sitting on the original; Clear keeping the anchor; timing unchanged after turning the scene; Live playing in the world where the anchors put it.
