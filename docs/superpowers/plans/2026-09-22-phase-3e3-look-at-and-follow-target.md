# Look At and Follow Target (Phase 3.e.3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two new aim modes for a track. Look At keeps the camera on a fixed point in the world. Follow Target keeps it on a nearby character, eased by a smoothing setting.

**Architecture:** Core gains the two modes, five new `Track` fields and their edits. A track's world view carries its Look At point and anchor. A stateful `AimTracker` resolves each frame's target and eases a followed character through an `AimSmoother`. Each playback owns one tracker, and the session owns one more for scrubbed frames. The game is reached only through `IAimTargets`. Core's `NearbyCharacters` implements it from a snapshot of loaded characters, which the Plugin refreshes each frame from the object table. The Look At point is selected as a third `AnchorKind` and edited through the anchor gizmo and the Point window. The Plugin adds the aim menu entries, the character button, aim height and smoothing to the track row, and a not-found warning to the playlist. It also draws the Look At point and the character marker.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`, `Dalamud.Bindings.ImGuizmo`.

**Spec:** `docs/superpowers/specs/2026-09-22-look-at-and-follow-target-design.md`

## Global Constraints

- `Vista.Core` never references Dalamud or FFXIVClientStructs and never uses `unsafe`. `Vista.Tests` references Core only.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only, with no `until … sleep` loops.
- Doc comments are one line. Inline comments are rare (CLAUDE.md).
- Commits: one line, conventional lowercase prefix, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`, and leave the untracked `CHECKLIST-*.md` files alone. Check `git status` after committing. Do not push.
- No backwards compatibility: nothing is saved yet, so there is no migration.
- The aim menu's names are exact: "Recorded aim", "Direction of travel", "Look At", "Follow Target".
- Other text from the spec is exact: the Point window title "Look At point", the button suffix "(Not found)", the tooltip "Not found nearby: using recorded aim", and the none-chosen text "Choose a character".
- Look At placement: 10 yalms along the first point's recorded aim, or 10 yalms ahead of the camera with no points. Only the first time a track is set to Look At.
- Aim height: 0 to 3 yalms above the character's feet, default 1.3. Smoothing: 0 (exact) to 1 (heavy), default 0.3. The eased target closes on the character with a time constant of Smoothing × 0.5 seconds.
- A target closer than 0.1 yalms to the camera keeps the last good aim. Pitch is clamped to `TrackAim.PitchLimit`, as today.
- Roll and FoV always come from the points.
- Each of these is one undo step: choosing a mode, moving the Look At point, choosing a character, and setting aim height or smoothing.
- In a self-sizing window, never align to the window's own width. Measure icon buttons with `IconButton.Width`. Tooltips on disabled items use `ImGuiHoveredFlags.AllowWhenDisabled`.
- Keys are named in words in user-facing text ("Backtick").

## Technical rulings (the cost if wrong is in brackets)

- **Smoother state lives in an `AimTracker`, one per playback.** `TrackPlayback` (the edit preview and a `TrackShot`) and `PlaylistPlayback` (Live) each own one. `SessionState` owns a separate one for scrubbed frames (`FrameAt`). A new playback starts fresh. `Seek` and `Restart` reset it, and `PlaylistPlayback` also resets it on every cut to an entry, including a wrap. [If wrong: move the tracker into the Director.]
- **Per-frame target resolution:** `AimTracker.Frame(evaluator, worldTrack, time, dt)` resolves the target, then evaluates:
  - Look At, placed: the world Look At point.
  - Follow Target with a found character: `IAimTargets.Find(name, worldTrack.Anchor.Position)` plus `AimHeight`, through the smoother.
  - Anything else: null, which gives the recorded aim.
  - [If wrong: resolve in each playback instead.]
- **A track's world view carries its anchor in the world as well as its points and Look At point.** `SceneGeometry.InWorld` sets `Anchor` to the world anchor, so `worldTrack.Anchor.Position` is the `near` for duplicate names. Nothing reads `Anchor` from a world track today. [If wrong: add the world anchor to `PlaylistItem` and `TrackPlayback`.]
- **Scrubbed frames are exact.** `FrameAt` resets its tracker on every call, so scrubbing and jumping always aim at the character where they are now. [If wrong: none.]
- **`dt` of 0 holds the eased aim.** A paused Live frame (the Director passes `dt = 0`) keeps the smoothed target where it is, even at smoothing 0. A seek while paused still resets, so the next frame lands on the character. [If wrong: let a paused camera keep following.]
- **When the character can't be found, the smoother is seeded with the recorded aim's look point** (`frame.LookAt`, 10 yalms along the recorded aim). When the character is found again, the aim eases from the recorded aim onto them, as the spec asks. [If wrong: snap back.]
- **The near-target guard is split.** `TrackEvaluator` is stateless and shared, so given a target closer than 0.1 yalms it falls back to recorded aim. `AimTracker` remembers the last good aim and puts it back over that fallback. With no good aim yet, recorded aim stands. [If wrong: none.]
- **`TrackEvaluator` with no target uses recorded aim under Look At and Follow Target.** The existing test `Aim == AimKeys ? keys : tangent` becomes `Aim == PathTangent ? tangent : keys`. [If wrong: none.]
- **The Look At point is selected as `AnchorKind.LookAt`.** It reuses the anchor rules: it is never selected together with a point, and an undo drops it when it no longer applies. It can be selected only while its track's aim is Look At and the point is placed. Any edit that leaves Look At drops the selection. `SelectedAnchorInWorld` stays scene or track only, and a separate `SelectedLookAtInWorld` returns the point. `MoveAnchor` and `PreviewAnchor` refuse it. [If wrong: a sibling `SelectedLookAt` flag.]
- **A Look At point placed before any point stays put in the world when the first point places the anchors.** `SceneGeometry.PlaceFor` re-expresses it. [If wrong: the point jumps by the anchor's offset on the first point.]
- **Clear track forgets the Look At point and the character,** as it already resets aim, speed, direction and loop. `TrackEditing.Clear` builds from `Empty`, so this needs no code. [If wrong: carry them in `Clear`.]
- **Characters are snapshotted once a frame** by `CameraSession.RefreshCharacters()` from `Framework.Update` into Core's `NearbyCharacters`. The object table is read on the main thread only, and the camera hook and UI read the snapshot. "Players and NPCs" means `ObjectKind.Pc`, `BattleNpc` and `EventNpc`, with a non-empty name. [If wrong: widen the kinds.]
- **The edited track's Look At point is drawn only while its aim is Look At,** the same rule the spec gives other tracks. [If wrong: draw it whenever it is placed.]
- **Under Look At, a point's camera glyph points at the Look At point.** Under Follow Target it shows the recorded aim. [If wrong: none; drawing only.]
- **The character button, aim height and smoothing show only under Follow Target.** With none chosen, the button reads "Choose a character" and so does its tooltip. With a character found, it shows the name, with the tooltip "Choose a character". [If wrong: wording.]
- **The playlist warning shows when an entry's track follows a named character who isn't found.** With no character chosen there is no warning. [If wrong: warn then too, with "Choose a character".]
- **The R key does nothing with the Look At point selected,** since it only moves. [If wrong: none.]

## Order and parallelism

- **Wave 1:** Task 1, on main.
- **Wave 2:** Task 2 ∥ Task 3, in worktrees from local main after Task 1 is committed.
- **Wave 3:** Task 4 ∥ Task 5, in worktrees from local main after Tasks 2 and 3 are merged.

**Pre-flight: shared files.**
- `src/Vista.Core/Session/SessionState.cs`: Tasks 2 and 3.
  - Task 2 touches the fields, the constructor, `Director`, `StartPreview` and `FrameAt`, and adds `CharacterAim`/`TargetLost` after `FrameAt`.
  - Task 3 touches `CommitEdit`, `UnplacedRefusal`, `MoveAnchor`, `PreviewAnchor` and `SelectedAnchorInWorld`'s doc. It adds members after `UnifyHandles`, after `SelectTrackAnchor` and after `Moved`.
- `src/Vista.Plugin/Session/CameraSession.cs`: Tasks 2, 3 and 4.
  - Task 2 adds two members after `WorldOf`.
  - Task 3 adds members after `PreviewAnchor` and after `UnifyHandles`, and replaces `WithCurrentPoint`.
  - Task 4 changes the `state` field and the constructor, and adds members after `Director`.
  - Tasks 2 and 3 both add `using System.Numerics;` at the top. Git takes identical additions cleanly; if not, keep one.
- Merge Task 2, then Task 3. Tasks 4 and 5 share no files. Task 5 never touches `CameraSession.cs`.
- Test files don't overlap between parallel tasks.

**File ownership**

| Task | Owns |
|---|---|
| 1 | `Tracks/AimMode.cs`, `Tracks/Track.cs`, `Tracks/TrackEditing.cs`, `Scenes/SceneGeometry.cs`; tests `Tracks/AimSettingsTests.cs` (new), `Scenes/SceneGeometryTests.cs` |
| 2 | `Tracks/TrackAim.cs`, `Tracks/TrackEvaluator.cs`, `Tracks/IAimTargets.cs`, `Tracks/NearbyCharacters.cs`, `Tracks/AimSmoother.cs`, `Tracks/AimTracker.cs` (all new but the first two), `Tracks/TrackPlayback.cs`, `Tracks/PlaylistPlayback.cs`, `Tracks/Director.cs`, `SessionState.cs` (its part), `CameraSession.cs` (its part); tests `TrackAimTests`, `TrackEvaluatorTests`, `AimSmootherTests` (new), `NearbyCharactersTests` (new), `AimTrackerTests` (new), `TrackPlaybackTests`, `PlaylistPlaybackTests`, `DirectorTests`, `Session/SessionAimTests.cs` (new) |
| 3 | `Session/AnchorKind.cs`, `Editing/TrackMarkerHitTest.cs`, `SessionState.cs` (its part), `CameraSession.cs` (its part); tests `Session/SessionLookAtTests.cs` (new), `Editing/TrackMarkerHitTestTests.cs` |
| 4 | `Plugin.cs`, `Game/CharacterTable.cs` (new), `CameraSession.cs` (its part), `Ui/TrackEditorWindow.cs`, `Ui/PlaylistPanel.cs`, `Ui/IconButton.cs` |
| 5 | `Ui/PointWindow.cs`, `Editor/Overlay.cs`, `Editor/EditorLayer.cs`, `Editor/AnchorGizmo.cs`, `Editor/PointGizmo.cs`, `Editor/EditorKeys.cs` |

---

### Task 1: The aim modes, the track's new fields, their edits and the world view

**Files:**
- Modify: `src/Vista.Core/Tracks/AimMode.cs`, `src/Vista.Core/Tracks/Track.cs`, `src/Vista.Core/Tracks/TrackEditing.cs`, `src/Vista.Core/Scenes/SceneGeometry.cs`
- Test: `tests/Vista.Tests/Tracks/AimSettingsTests.cs` (new), `tests/Vista.Tests/Scenes/SceneGeometryTests.cs`

**Interfaces:**
- Produces (namespace `Vista.Core.Tracks` unless noted):
  - `enum AimMode { PathTangent, AimKeys, LookAt, FollowTarget }`
  - `record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop, Anchor Anchor = default, bool AnchorPlaced = false, Vector3 LookAt = default, bool LookAtPlaced = false, string? TargetName = null, float AimHeight = TrackEditing.DefaultAimHeight, float Smoothing = TrackEditing.DefaultSmoothing)`. `LookAt` is local to the track's anchor.
  - `TrackEditing.DefaultAimHeight = 1.3f`, `TrackEditing.MaxAimHeight = 3f`, `TrackEditing.DefaultSmoothing = 0.3f`
  - `TrackEditing.SetAim(Track track, AimMode aim, ControlPoint camera) : Track`. `camera` is local to the track's anchor and is used only with no points.
  - `TrackEditing.SetLookAt(Track track, Vector3 local) : Track`
  - `TrackEditing.SetTarget(Track track, string? name) : Track`
  - `TrackEditing.SetAimHeight(Track track, float yalms) : Track`
  - `TrackEditing.SetSmoothing(Track track, float smoothing) : Track`
  - Each setter returns the same instance when nothing changes.
  - `SceneGeometry.InWorld(Scene, Track)` (`Vista.Core.Scenes`): the returned track has `Anchor`, `Points` and `LookAt` in the world.

- [ ] **Step 1: Write the failing edit tests**

Create `tests/Vista.Tests/Tracks/AimSettingsTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AimSettingsTests
{
    private const float Tolerance = 1e-4f;

    // A camera at (100, 5, 100) looking along yaw 90°, which faces −x.
    private static readonly ControlPoint Camera = new(new Vector3(100f, 5f, 100f), MathF.PI / 2f, 0f, 1f);

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void ANewTrackHasNoLookAtNoCharacterAndTheDefaultFollowSettings()
    {
        var track = TrackEditing.Empty();

        Assert.False(track.LookAtPlaced);
        Assert.Null(track.TargetName);
        Assert.Equal(1.3f, track.AimHeight);
        Assert.Equal(0.3f, track.Smoothing);
    }

    [Fact]
    public void ChoosingLookAtFirstPlacesItTenYalmsAlongTheFirstPointsAim()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), new ControlPoint(new Vector3(1f, 2f, 3f), MathF.PI / 2f, 0f, 1f));

        var looking = TrackEditing.SetAim(track, AimMode.LookAt, Camera);

        Assert.Equal(AimMode.LookAt, looking.Aim);
        Assert.True(looking.LookAtPlaced);
        Near(new Vector3(-9f, 2f, 3f), looking.LookAt);
    }

    [Fact]
    public void WithNoPointsTheLookAtGoesTenYalmsAheadOfTheCamera()
        => Near(new Vector3(90f, 5f, 100f), TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera).LookAt);

    [Fact]
    public void ComingBackToLookAtKeepsThePointWhereItWas()
    {
        var moved = TrackEditing.SetLookAt(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera), new Vector3(7f, 8f, 9f));

        var back = TrackEditing.SetAim(TrackEditing.SetAim(moved, AimMode.AimKeys, Camera), AimMode.LookAt, Camera);

        Assert.Equal(new Vector3(7f, 8f, 9f), back.LookAt);
    }

    [Fact]
    public void OtherModesPlaceNoLookAtAndTheSameModeChangesNothing()
    {
        Assert.False(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.FollowTarget, Camera).LookAtPlaced);
        var track = TrackEditing.Empty();
        Assert.Same(track, TrackEditing.SetAim(track, AimMode.AimKeys, Camera));
    }

    [Fact]
    public void TheFollowSettingsClampAndAnEmptyNameClearsTheCharacter()
    {
        var track = TrackEditing.Empty();
        var named = TrackEditing.SetTarget(track, "Guard");

        Assert.Equal("Guard", named.TargetName);
        Assert.Null(TrackEditing.SetTarget(named, "").TargetName);
        Assert.Same(track, TrackEditing.SetTarget(track, null));
        Assert.Equal(3f, TrackEditing.SetAimHeight(track, 9f).AimHeight);
        Assert.Equal(0f, TrackEditing.SetAimHeight(track, -1f).AimHeight);
        Assert.Same(track, TrackEditing.SetAimHeight(track, float.NaN));
        Assert.Equal(1f, TrackEditing.SetSmoothing(track, 2f).Smoothing);
        Assert.Equal(0f, TrackEditing.SetSmoothing(track, -0.5f).Smoothing);
        Assert.Same(track, TrackEditing.SetSmoothing(track, 0.3f));
    }

    [Fact]
    public void ClearForgetsTheLookAtAndTheCharacter()
    {
        var track = TrackEditing.SetTarget(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera), "Guard");

        var cleared = TrackEditing.Clear(track);

        Assert.False(cleared.LookAtPlaced);
        Assert.Null(cleared.TargetName);
    }
}
```

The first placement test works like this. Yaw 90° faces −x, and `FreeCamMotion.LookAtFrom` reaches 10 yalms, so (1, 2, 3) becomes (−9, 2, 3). The camera case goes from (100, 5, 100) to (90, 5, 100).

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter AimSettingsTests`
Expected: the build fails. `Track` has no `LookAt`, and `TrackEditing` has no `SetAim`.

- [ ] **Step 3: The modes, the fields and the edits**

`src/Vista.Core/Tracks/AimMode.cs`:

```csharp
namespace Vista.Core.Tracks;

/// <summary>How a track decides which way the camera looks.</summary>
public enum AimMode { PathTangent, AimKeys, LookAt, FollowTarget }
```

`src/Vista.Core/Tracks/Track.cs`:

```csharp
using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>A camera move: its identity and name, a path through control points local to its anchor, their timing, speed, aim and playback, its Look At point and the character it follows.</summary>
public sealed record Track(Guid Id, string Name, IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop, Anchor Anchor = default, bool AnchorPlaced = false, Vector3 LookAt = default, bool LookAtPlaced = false, string? TargetName = null, float AimHeight = TrackEditing.DefaultAimHeight, float Smoothing = TrackEditing.DefaultSmoothing);
```

In `TrackEditing.cs`, add `using System.Numerics;` and `using Vista.Core.Camera;` at the top. Add the constants after `MinKeyGap`:

```csharp
    /// <summary>A new track's aim height above its character's feet, in yalms.</summary>
    public const float DefaultAimHeight = 1.3f;

    /// <summary>The highest aim height, in yalms.</summary>
    public const float MaxAimHeight = 3f;

    /// <summary>A new track's smoothing, from 0 (exact) to 1 (heavy).</summary>
    public const float DefaultSmoothing = 0.3f;
```

Add the setters after `SetLoop`:

```csharp
    /// <summary>Sets the aim mode; the first Look At places its point 10 yalms along the first point's aim, or <paramref name="camera"/>'s with no points.</summary>
    public static Track SetAim(Track track, AimMode aim, ControlPoint camera)
    {
        if (track.Aim == aim) return track;
        var result = track with { Aim = aim };
        if (aim != AimMode.LookAt || track.LookAtPlaced) return result;

        var from = track.Points.Count > 0 ? track.Points[0] : camera;
        return result with { LookAt = FreeCamMotion.LookAtFrom(from.Position, from.Yaw, from.Pitch), LookAtPlaced = true };
    }

    /// <summary>Puts the Look At point at <paramref name="local"/>, relative to the track's anchor.</summary>
    public static Track SetLookAt(Track track, Vector3 local)
        => track.LookAtPlaced && track.LookAt == local ? track : track with { LookAt = local, LookAtPlaced = true };

    /// <summary>Names the character to follow; null or blank chooses none.</summary>
    public static Track SetTarget(Track track, string? name)
    {
        var chosen = string.IsNullOrWhiteSpace(name) ? null : name;
        return track.TargetName == chosen ? track : track with { TargetName = chosen };
    }

    /// <summary>Sets the aim height above the character's feet, clamped to 0 to <see cref="MaxAimHeight"/>.</summary>
    public static Track SetAimHeight(Track track, float yalms)
    {
        if (!float.IsFinite(yalms)) return track;
        var clamped = Math.Clamp(yalms, 0f, MaxAimHeight);
        return clamped == track.AimHeight ? track : track with { AimHeight = clamped };
    }

    /// <summary>Sets how heavily the aim eases onto the character, clamped to 0 to 1.</summary>
    public static Track SetSmoothing(Track track, float smoothing)
    {
        if (!float.IsFinite(smoothing)) return track;
        var clamped = Math.Clamp(smoothing, 0f, 1f);
        return clamped == track.Smoothing ? track : track with { Smoothing = clamped };
    }
```

- [ ] **Step 4: Run the edit tests**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter AimSettingsTests`
Expected: PASS.

- [ ] **Step 5: Write the failing world-view tests**

Add to `SceneGeometryTests.cs`:

```csharp
    // The track in Anchored() with its Look At point at (0, 3, −10) local to its anchor.
    private static Scene WithLookAt(Scene scene)
        => SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(0f, 3f, -10f), LookAtPlaced = true });

    private static Vector3 WorldLookAt(Scene scene) => SceneGeometry.InWorld(scene, scene.Tracks[0]).LookAt;

    [Fact]
    public void InWorldCarriesTheLookAtAndTheAnchorThroughBothAnchors()
    {
        var scene = WithLookAt(Anchored());
        var anchor = scene.Anchor.ToWorld(scene.Tracks[0].Anchor);
        var world = SceneGeometry.InWorld(scene, scene.Tracks[0]);

        Near(anchor.ToWorld(new Vector3(0f, 3f, -10f)), world.LookAt);
        Assert.Equal(anchor, world.Anchor);
    }

    [Fact]
    public void ATrackWithOnlyALookAtIsStillCarried()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 2f, 50f), 0f), AnchorPlaced = true };
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(1f, 0f, 0f), LookAtPlaced = true });

        Near(new Vector3(101f, 2f, 50f), WorldLookAt(scene));
    }

    [Fact]
    public void MovingATrackAnchorCarriesItsLookAt()
    {
        var scene = WithLookAt(Anchored());
        var to = new Anchor(new Vector3(90f, 2f, 60f), 0.2f);

        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, to, carry: true);

        Near(to.ToWorld(new Vector3(0f, 3f, -10f)), WorldLookAt(moved));
    }

    [Fact]
    public void MovingATrackAnchorAloneLeavesItsLookAtInTheWorld()
    {
        var scene = WithLookAt(Anchored());
        var before = WorldLookAt(scene);

        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(new Vector3(80f, 0f, 30f), 2f), carry: false);

        Near(before, WorldLookAt(moved));
    }

    [Fact]
    public void MovingTheSceneAnchorAloneLeavesTheLookAtInTheWorld()
    {
        var scene = WithLookAt(Anchored());
        var before = WorldLookAt(scene);

        var moved = SceneGeometry.MoveSceneAnchor(scene, new Anchor(new Vector3(-30f, 1f, 8f), -0.7f), carry: false);

        Near(before, WorldLookAt(moved));
    }

    [Fact]
    public void PlacingTheAnchorsLeavesALookAtPlacedBeforeThemInTheWorld()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(5f, 6f, 7f), LookAtPlaced = true });

        var placed = SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(20f, 9f, -3f), 2f);

        Near(new Vector3(5f, 6f, 7f), WorldLookAt(placed));
    }
```

Trace of the last test. The ground is (20, 2, −3) with yaw 0. Placing the scene anchor moves the Look At point from the origin to the ground: local (5, 6, 7) becomes (−15, 4, 10). The track anchor then goes to `ground.ToLocal(ground)`, which is the origin. Its world anchor is still the ground, so nothing moves again. Back in the world, (−15, 4, 10) plus (20, 2, −3) is (5, 6, 7).

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter SceneGeometryTests`
Expected: FAIL. `InWorld` doesn't carry the Look At point or set the anchor, and placing or moving an anchor alone moves the point.

- [ ] **Step 7: Carry the Look At point**

In `SceneGeometry.cs`, replace `InWorld`, `PlaceFor` and `MoveTrackAnchor`, and add `KeepLookAt`:

```csharp
    /// <summary>The track with its anchor, points and Look At point in the world; the track itself when nothing moves it.</summary>
    public static Track InWorld(Scene scene, Track track)
    {
        var anchor = WorldAnchor(scene, track);
        if (anchor == Anchor.Origin && track.Anchor == Anchor.Origin) return track;
        return track with { Anchor = anchor, Points = track.Points.Select(anchor.ToWorld).ToArray(), LookAt = anchor.ToWorld(track.LookAt) };
    }

    /// <summary>Places the scene's and the track's anchors under a first point at ground height, yaw 0, where not placed yet; a Look At point already placed stays in the world.</summary>
    public static Scene PlaceFor(Scene scene, Guid trackId, Vector3 worldPosition, float groundHeight)
    {
        var ground = new Anchor(worldPosition with { Y = groundHeight }, 0f);
        var result = scene;
        if (!scene.AnchorPlaced)
        {
            var placed = scene with { Anchor = ground, AnchorPlaced = true };
            result = placed with { Tracks = scene.Tracks.Select(t => KeepLookAt(t, WorldAnchor(scene, t), WorldAnchor(placed, t))).ToArray() };
        }

        var track = SceneEditing.Get(result, trackId);
        if (track.AnchorPlaced) return result;
        var anchored = track with { Anchor = result.Anchor.ToLocal(ground), AnchorPlaced = true };
        return SceneEditing.Replace(result, KeepLookAt(anchored, WorldAnchor(result, track), WorldAnchor(result, anchored)));
    }
```

`MoveTrackAnchor` (only the `!carry` block changes):

```csharp
    /// <summary>Moves a track's anchor to <paramref name="toWorld"/>, carrying its points and Look At point, or alone so they stay where they are.</summary>
    public static Scene MoveTrackAnchor(Scene scene, Guid trackId, Anchor toWorld, bool carry)
    {
        var track = SceneEditing.Get(scene, trackId);
        var moved = track with { Anchor = scene.Anchor.ToLocal(toWorld), AnchorPlaced = true };
        if (!carry)
        {
            var from = WorldAnchor(scene, track);
            moved = KeepLookAt(moved with { Points = track.Points.Select(p => toWorld.ToLocal(from.ToWorld(p))).ToArray() }, from, toWorld);
        }

        return SceneEditing.Replace(scene, moved);
    }

    /// <summary>The track with its placed Look At point re-expressed so it stays in the world when its anchor goes from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Track KeepLookAt(Track track, Anchor from, Anchor to)
        => track.LookAtPlaced ? track with { LookAt = to.ToLocal(from.ToWorld(track.LookAt)) } : track;
```

`MoveSceneAnchor` needs no change. Moving it alone keeps every track's world anchor, so the Look At point stays too.

- [ ] **Step 8: Run all tests and build**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`, then `./build.sh`
Expected: all pass, 0 warnings. `InWorldIsTheTrackItselfAtTheOrigin` and `PlaceForLeavesPlacedAnchorsAlone` still pass.

- [ ] **Step 9: Commit**

```bash
git add src/Vista.Core/Tracks/AimMode.cs src/Vista.Core/Tracks/Track.cs src/Vista.Core/Tracks/TrackEditing.cs src/Vista.Core/Scenes/SceneGeometry.cs tests/Vista.Tests/Tracks/AimSettingsTests.cs tests/Vista.Tests/Scenes/SceneGeometryTests.cs
git commit -m "feat(tracks) add look at and follow target modes and fields"
```

---

### Task 2: Aiming at a target in playback, preview and scrubbing

**Files:**
- Create: `src/Vista.Core/Tracks/IAimTargets.cs`, `src/Vista.Core/Tracks/NearbyCharacters.cs`, `src/Vista.Core/Tracks/AimSmoother.cs`, `src/Vista.Core/Tracks/AimTracker.cs`
- Modify: `src/Vista.Core/Tracks/TrackAim.cs`, `src/Vista.Core/Tracks/TrackEvaluator.cs`, `src/Vista.Core/Tracks/TrackPlayback.cs`, `src/Vista.Core/Tracks/PlaylistPlayback.cs`, `src/Vista.Core/Tracks/Director.cs`, `src/Vista.Core/Session/SessionState.cs` (fields, constructor, `Director`, `StartPreview`, `FrameAt`, new members after `FrameAt`), `src/Vista.Plugin/Session/CameraSession.cs` (two members after `WorldOf`)
- Test: `tests/Vista.Tests/Tracks/TrackAimTests.cs`, `tests/Vista.Tests/Tracks/TrackEvaluatorTests.cs`, `tests/Vista.Tests/Tracks/AimSmootherTests.cs` (new), `tests/Vista.Tests/Tracks/NearbyCharactersTests.cs` (new), `tests/Vista.Tests/Tracks/AimTrackerTests.cs` (new), `tests/Vista.Tests/Tracks/TrackPlaybackTests.cs`, `tests/Vista.Tests/Tracks/PlaylistPlaybackTests.cs`, `tests/Vista.Tests/Tracks/DirectorTests.cs`, `tests/Vista.Tests/Session/SessionAimTests.cs` (new)

**Interfaces:**
- Consumes (Task 1): `AimMode.LookAt`, `AimMode.FollowTarget`, and `Track.LookAt`, `LookAtPlaced`, `TargetName`, `AimHeight`, `Smoothing`. A world track's `Anchor` is its world anchor.
- Produces (namespace `Vista.Core.Tracks`):
  - `TrackAim.MinTargetDistance = 0.1f`
  - `TrackAim.Toward(Vector3 from, Vector3 target) : (float Yaw, float Pitch)?`
  - `TrackEvaluator.Evaluate(double time, Vector3? target = null) : CameraState?`
  - `interface IAimTargets { Vector3? Find(string name, Vector3 near); }`
  - `record struct LoadedCharacter(string Name, Vector3 Position)`
  - `sealed class NearbyCharacters : IAimTargets`, with members:
    - `IReadOnlyList<LoadedCharacter> All`
    - `void Update(IReadOnlyList<LoadedCharacter> loaded)`
    - `Vector3? Find(string name, Vector3 near)`
    - `IReadOnlyList<LoadedCharacter> NearestTo(Vector3 place)`
  - `sealed class AimSmoother`, with members:
    - `const float SecondsPerSmoothing = 0.5f`
    - `Vector3 Step(Vector3 target, float dt, float smoothing)`
    - `void Seed(Vector3 position)`
    - `void Reset()`
  - `sealed class AimTracker(IAimTargets? targets)`, with members:
    - `static Vector3? CharacterAim(Track world, IAimTargets? targets)`
    - `static bool TargetLost(Track world, IAimTargets? targets)`
    - `CameraState? Frame(TrackEvaluator evaluator, Track world, double time, float dt)`
    - `void Reset()`
  - `TrackPlayback(Track track, IAimTargets? targets = null)`
  - `PlaylistPlayback(IReadOnlyList<PlaylistItem> items, bool loops = false, IAimTargets? targets = null)`
  - `Director(IAimTargets? targets = null)`
  - `SessionState(Func<Vector3, float?>? groundBelow = null, IAimTargets? aimTargets = null)`
  - `SessionState.CharacterAim(Track world) : Vector3?` and `SessionState.TargetLost(Track world) : bool`
  - `CameraSession.CharacterAim(Track world) : Vector3?` and `CameraSession.TargetLost(Track world) : bool`

- [ ] **Step 1: Write the failing aim and evaluator tests**

Add to `TrackAimTests.cs`:

```csharp
    [Fact]
    public void TowardAimsFromOnePlaceAtAnother()
    {
        var ahead = TrackAim.Toward(Vector3.Zero, new Vector3(0f, 0f, -10f))!.Value;
        Assert.Equal(0f, ahead.Yaw, 4);
        Assert.Equal(0f, ahead.Pitch, 4);

        var left = TrackAim.Toward(Vector3.Zero, new Vector3(-10f, 0f, 0f))!.Value;
        Assert.Equal(90f * Deg, left.Yaw, 4);
    }

    [Fact]
    public void TowardClampsPitchAndGivesNoAimForATargetOnTheCamera()
    {
        Assert.Equal(TrackAim.PitchLimit, TrackAim.Toward(Vector3.Zero, new Vector3(0f, 10f, 0f))!.Value.Pitch, 5);
        Assert.Null(TrackAim.Toward(Vector3.Zero, new Vector3(0.05f, 0f, 0f)));
    }
```

Add to `TrackEvaluatorTests.cs`:

```csharp
    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.FollowTarget)]
    public void ATargetAimsTheCameraAtIt(AimMode aim)
    {
        var track = TrackEditing.SetSpeed(Build(new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f), Point(20f, 0f, 0f) }, aim), 5f);
        var target = new Vector3(10f, 5f, -30f);

        var state = new TrackEvaluator(track).Evaluate(2.0, target)!.Value;

        var look = Vector3.Normalize(state.LookAt - state.Position);
        var toTarget = Vector3.Normalize(target - state.Position);
        Assert.Equal(toTarget.X, look.X, 3);
        Assert.Equal(toTarget.Y, look.Y, 3);
        Assert.Equal(toTarget.Z, look.Z, 3);
    }

    [Theory]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.FollowTarget)]
    public void WithNoTargetTheNewModesUseTheRecordedAim(AimMode aim)
    {
        var points = new[] { Point(0f, 0f, 0f, yaw: 90f * Deg), Point(10f, 0f, 0f, yaw: 90f * Deg) };

        var state = new TrackEvaluator(Build(points, aim)).Evaluate(1.0)!.Value;

        Assert.Equal(90f * Deg, TrackAim.FromDirection(state.LookAt - state.Position).Yaw, 3);
    }

    [Theory]
    [InlineData(AimMode.LookAt)]
    [InlineData(AimMode.FollowTarget)]
    public void ASinglePointTrackTurnsToATarget(AimMode aim)
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f, fov: 1.2f, roll: 0.3f);

        var state = new TrackEvaluator(Build(new[] { point }, aim)).Evaluate(0.0, new Vector3(1f, 2f, -7f))!.Value;

        var (yaw, pitch) = TrackAim.FromDirection(state.LookAt - state.Position);
        Assert.Equal(point.Position, state.Position);
        Assert.Equal(0f, yaw, 4);
        Assert.Equal(0f, pitch, 4);
        Assert.Equal(1.2f, state.Fov);
        Assert.Equal(0.3f, state.Roll);
    }

    [Fact]
    public void ATargetOnTheCameraLeavesTheRecordedAim()
    {
        var point = Point(1f, 2f, 3f, yaw: 0.5f, pitch: 0.1f);

        var state = new TrackEvaluator(Build(new[] { point }, AimMode.LookAt)).Evaluate(0.0, new Vector3(1.05f, 2f, 3f))!.Value;

        Assert.Equal(FreeCamMotion.LookAtFrom(point.Position, 0.5f, 0.1f), state.LookAt);
    }
```

Hand-traced values:
- In the single-point case the target is 10 yalms straight along −z, and yaw 0 faces −z, so yaw and pitch come out as 0.
- In the recorded-aim case the yaw is 90° at both points. The channel of a constant is that constant. Direction of travel would give −90° here, so this test tells the two apart.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "TrackAimTests|TrackEvaluatorTests"`
Expected: the build fails. `TrackAim.Toward` doesn't exist and `Evaluate` takes no target.

- [ ] **Step 3: Aim toward a target**

In `TrackAim.cs`, add after `PitchLimit`:

```csharp
    /// <summary>Closer than this, in yalms, a target gives no aim.</summary>
    public const float MinTargetDistance = 0.1f;
```

And after `FromDirection`:

```csharp
    /// <summary>The pitch-clamped aim from <paramref name="from"/> at <paramref name="target"/>, or null when it is closer than <see cref="MinTargetDistance"/>.</summary>
    public static (float Yaw, float Pitch)? Toward(Vector3 from, Vector3 target)
    {
        var direction = target - from;
        return direction.Length() < MinTargetDistance ? null : ClampPitch(FromDirection(direction));
    }
```

In `TrackEvaluator.cs`, replace `Evaluate`:

```csharp
    /// <summary>The camera's state at <paramref name="time"/>, aimed at <paramref name="target"/> when given, or null for a track with no points.</summary>
    public CameraState? Evaluate(double time, Vector3? target = null)
    {
        if (_track.Points.Count == 0) return null;

        if (_track.Points.Count == 1)
        {
            var only = _track.Points[0];
            var (onlyYaw, onlyPitch) = Toward(only.Position, target) ?? (only.Yaw, only.Pitch);
            return new CameraState(only.Position, FreeCamMotion.LookAtFrom(only.Position, onlyYaw, onlyPitch), only.Fov, only.Roll);
        }

        var (segment, fraction) = LocateDistance(_curve.PositionAt(time));
        var parameter = _table.ParameterAt(segment, fraction);
        var cameraPosition = CatmullRom.Evaluate(_positions, segment, parameter);

        var (yaw, pitch) = Toward(cameraPosition, target)
            ?? (_track.Aim == AimMode.PathTangent
                ? TrackAim.PathTangent(_positions, _table, segment, fraction, (_yaws[0], _pitches[0]))
                : AimKeys(segment, fraction));

        var fov = Math.Clamp(TrackAim.Channel(_fovs, segment, fraction), _fovMin, _fovMax);
        var roll = TrackAim.Channel(_rolls, segment, fraction);

        return new CameraState(cameraPosition, FreeCamMotion.LookAtFrom(cameraPosition, yaw, pitch), fov, roll);
    }

    /// <summary>The aim at <paramref name="target"/> from <paramref name="from"/>, or null with no target or one on the camera.</summary>
    private static (float Yaw, float Pitch)? Toward(Vector3 from, Vector3? target)
        => target is { } at ? TrackAim.Toward(from, at) : null;
```

- [ ] **Step 4: Run them**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "TrackAimTests|TrackEvaluatorTests"`
Expected: PASS.

- [ ] **Step 5: Write the failing smoother and character tests**

`tests/Vista.Tests/Tracks/AimSmootherTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AimSmootherTests
{
    private static readonly Vector3 Start = Vector3.Zero;
    private static readonly Vector3 Target = new(10f, 0f, 0f);

    // At smoothing 1 the time constant is 0.5 s; half a second closes 1 − 1/e of the gap.
    private static readonly float HalfSecondAtFull = 10f * (1f - MathF.Exp(-1f));

    [Fact]
    public void TheFirstStepLandsOnTheTarget()
        => Assert.Equal(Target, new AimSmoother().Step(Target, 1f / 60f, 1f));

    [Fact]
    public void AtFullSmoothingHalfASecondClosesOneTimeConstant()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 1f);

        Assert.Equal(HalfSecondAtFull, smoother.Step(Target, 0.5f, 1f).X, 4);
    }

    [Fact]
    public void EasingDependsOnTimeNotOnFrames()
    {
        var sixty = new AimSmoother();
        var thirty = new AimSmoother();
        sixty.Step(Start, 0f, 0.3f);
        thirty.Step(Start, 0f, 0.3f);
        var a = Vector3.Zero;
        var b = Vector3.Zero;

        for (var i = 0; i < 12; i++) a = sixty.Step(Target, 1f / 60f, 0.3f);
        for (var i = 0; i < 6; i++) b = thirty.Step(Target, 1f / 30f, 0.3f);

        Assert.Equal(a.X, b.X, 3);
    }

    [Fact]
    public void ZeroSmoothingIsExact()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 0f);

        Assert.Equal(Target, smoother.Step(Target, 1f / 60f, 0f));
    }

    [Fact]
    public void AResetStartsAfreshWithNoEasing()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 1f);
        smoother.Reset();

        Assert.Equal(Target, smoother.Step(Target, 1f / 60f, 1f));
    }

    [Fact]
    public void NoTimeHoldsWhereItIs()
    {
        var smoother = new AimSmoother();
        smoother.Step(Start, 0f, 0f);

        Assert.Equal(Start, smoother.Step(Target, 0f, 0f));
    }

    [Fact]
    public void ASeedIsWhereEasingStartsFrom()
    {
        var smoother = new AimSmoother();
        smoother.Seed(Start);

        Assert.Equal(HalfSecondAtFull, smoother.Step(Target, 0.5f, 1f).X, 4);
    }
}
```

`tests/Vista.Tests/Tracks/NearbyCharactersTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class NearbyCharactersTests
{
    private static NearbyCharacters With(params LoadedCharacter[] loaded)
    {
        var characters = new NearbyCharacters();
        characters.Update(loaded);
        return characters;
    }

    [Fact]
    public void FindGivesTheNamedCharactersFeet()
    {
        var characters = With(new LoadedCharacter("Guard", new Vector3(3f, 0f, 4f)), new LoadedCharacter("Merchant", new Vector3(9f, 0f, 9f)));

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.Find("Guard", Vector3.Zero));
    }

    [Fact]
    public void AnUnknownOrDifferentlyCasedNameIsNotFound()
    {
        var characters = With(new LoadedCharacter("Guard", Vector3.Zero));

        Assert.Null(characters.Find("Merchant", Vector3.Zero));
        Assert.Null(characters.Find("guard", Vector3.Zero));
        Assert.Null(new NearbyCharacters().Find("Guard", Vector3.Zero));
    }

    [Fact]
    public void ADuplicateNameResolvesToTheOneNearestThePlaceGiven()
    {
        var characters = With(new LoadedCharacter("Guard", new Vector3(-20f, 0f, 0f)), new LoadedCharacter("Guard", new Vector3(20f, 0f, 0f)));

        Assert.Equal(new Vector3(20f, 0f, 0f), characters.Find("Guard", new Vector3(15f, 0f, 0f)));
        Assert.Equal(new Vector3(-20f, 0f, 0f), characters.Find("Guard", new Vector3(-1f, 0f, 0f)));
    }

    [Fact]
    public void NearestToSortsByDistance()
    {
        var characters = With(
            new LoadedCharacter("Far", new Vector3(30f, 0f, 0f)),
            new LoadedCharacter("Near", new Vector3(2f, 0f, 0f)),
            new LoadedCharacter("Middle", new Vector3(0f, 0f, 10f)));

        Assert.Equal(new[] { "Near", "Middle", "Far" }, characters.NearestTo(Vector3.Zero).Select(c => c.Name));
    }
}
```

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "AimSmootherTests|NearbyCharactersTests"`
Expected: the build fails. `AimSmoother`, `NearbyCharacters` and `LoadedCharacter` don't exist.

- [ ] **Step 7: The target source, the characters and the smoother**

`src/Vista.Core/Tracks/IAimTargets.cs`:

```csharp
using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>Finds characters in the game for a track to follow.</summary>
public interface IAimTargets
{
    /// <summary>The feet of the character named <paramref name="name"/> nearest <paramref name="near"/>, or null when none is loaded.</summary>
    Vector3? Find(string name, Vector3 near);
}
```

`src/Vista.Core/Tracks/NearbyCharacters.cs`:

```csharp
using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>A character loaded in the game: its name and where its feet are.</summary>
public readonly record struct LoadedCharacter(string Name, Vector3 Position);

/// <summary>The characters loaded nearby as last read, found by exact name.</summary>
public sealed class NearbyCharacters : IAimTargets
{
    private IReadOnlyList<LoadedCharacter> characters = [];

    /// <summary>Every character as last read.</summary>
    public IReadOnlyList<LoadedCharacter> All => characters;

    /// <summary>Replaces the characters with <paramref name="loaded"/>.</summary>
    public void Update(IReadOnlyList<LoadedCharacter> loaded) => characters = loaded;

    public Vector3? Find(string name, Vector3 near)
    {
        Vector3? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var character in characters)
        {
            if (!string.Equals(character.Name, name, StringComparison.Ordinal)) continue;
            var distance = Vector3.DistanceSquared(character.Position, near);
            if (distance >= bestDistance) continue;
            best = character.Position;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Every character, nearest <paramref name="place"/> first.</summary>
    public IReadOnlyList<LoadedCharacter> NearestTo(Vector3 place)
        => characters.OrderBy(c => Vector3.DistanceSquared(c.Position, place)).ToArray();
}
```

`src/Vista.Core/Tracks/AimSmoother.cs`:

```csharp
using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>Eases a target position towards the live one with a time constant of smoothing × <see cref="SecondsPerSmoothing"/>.</summary>
public sealed class AimSmoother
{
    /// <summary>Seconds of time constant at smoothing 1.</summary>
    public const float SecondsPerSmoothing = 0.5f;

    private Vector3? current;

    /// <summary>Moves towards <paramref name="target"/> by <paramref name="dt"/> seconds: lands on it when fresh or at smoothing 0, and holds with no time.</summary>
    public Vector3 Step(Vector3 target, float dt, float smoothing)
    {
        if (current is not { } from)
        {
            current = target;
            return target;
        }

        if (dt <= 0f) return from;
        var timeConstant = Math.Clamp(smoothing, 0f, 1f) * SecondsPerSmoothing;
        var next = timeConstant <= 0f ? target : Vector3.Lerp(from, target, 1f - MathF.Exp(-dt / timeConstant));
        current = next;
        return next;
    }

    /// <summary>Sets where the next step eases from.</summary>
    public void Seed(Vector3 position) => current = position;

    /// <summary>Forgets where it was, so the next step lands on its target.</summary>
    public void Reset() => current = null;
}
```

- [ ] **Step 8: Run them**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "AimSmootherTests|NearbyCharactersTests"`
Expected: PASS.

- [ ] **Step 9: Write the failing tracker tests**

`tests/Vista.Tests/Tracks/AimTrackerTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AimTrackerTests
{
    // One point at the origin, recorded aim yaw 0.5.
    private static readonly ControlPoint Camera = new(Vector3.Zero, 0.5f, 0f, 1f);
    private static readonly Vector3 Recorded = FreeCamMotion.LookAtFrom(Vector3.Zero, 0.5f, 0f);

    private static Track Single(AimMode aim) => TrackEditing.Append(TrackEditing.Empty(aim), Camera);

    private static Track Following(string? name = "Guard", float smoothing = 0f)
        => Single(AimMode.FollowTarget) with { TargetName = name, Smoothing = smoothing };

    // A guard whose aim point, 1.3 above the feet, is at (x, 0, −10).
    private static NearbyCharacters GuardAt(float x)
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", new Vector3(x, -1.3f, -10f))]);
        return characters;
    }

    private static CameraState Frame(AimTracker tracker, Track track, float dt = 1f / 60f)
        => tracker.Frame(new TrackEvaluator(track), track, 0.0, dt)!.Value;

    private static void AimsAt(Vector3 target, CameraState frame)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, 3);
        Assert.Equal(want.Y, got.Y, 3);
        Assert.Equal(want.Z, got.Z, 3);
    }

    [Fact]
    public void FollowAimsAtTheCharacterAtItsAimHeight()
    {
        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(GuardAt(0f)), Following()));
        Assert.Equal(new Vector3(0f, 0f, -10f), AimTracker.CharacterAim(Following(), GuardAt(0f)));
    }

    [Fact]
    public void RecordedAimIsUsedWhenTheCharacterIsNotFoundOrNoneIsChosen()
    {
        Assert.Equal(Recorded, Frame(new AimTracker(new NearbyCharacters()), Following()).LookAt);
        Assert.Equal(Recorded, Frame(new AimTracker(GuardAt(0f)), Following(name: null)).LookAt);
        Assert.True(AimTracker.TargetLost(Following(), new NearbyCharacters()));
        Assert.False(AimTracker.TargetLost(Following(name: null), new NearbyCharacters()));
        Assert.False(AimTracker.TargetLost(Following(), GuardAt(0f)));
    }

    [Fact]
    public void ADuplicateNameResolvesNearestTheTracksAnchor()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", new Vector3(-20f, -1.3f, -10f)), new LoadedCharacter("Guard", new Vector3(20f, -1.3f, -10f))]);
        var track = Following() with { Anchor = new Anchor(new Vector3(15f, 0f, 0f), 0f) };

        AimsAt(new Vector3(20f, 0f, -10f), Frame(new AimTracker(characters), track));
    }

    [Fact]
    public void LookAtAimsAtThePoint()
    {
        var track = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };

        AimsAt(new Vector3(0f, 0f, -10f), Frame(new AimTracker(null), track));
    }

    [Theory]
    [InlineData(AimMode.AimKeys)]
    [InlineData(AimMode.PathTangent)]
    public void ASinglePointKeepsItsRecordedAimUnderTheOtherModes(AimMode aim)
    {
        var track = Single(aim) with { TargetName = "Guard", LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };

        Assert.Equal(Recorded, Frame(new AimTracker(GuardAt(5f)), track).LookAt);
    }

    [Fact]
    public void TheAimEasesOntoAMovingCharacterAndAResetSnapsBack()
    {
        var characters = GuardAt(0f);
        var tracker = new AimTracker(characters);
        var track = Following(smoothing: 1f);
        Frame(tracker, track);

        characters.Update(GuardAt(10f).All);
        AimsAt(Vector3.Lerp(new Vector3(0f, 0f, -10f), new Vector3(10f, 0f, -10f), 1f - MathF.Exp(-1f)), Frame(tracker, track, 0.5f));

        tracker.Reset();
        AimsAt(new Vector3(10f, 0f, -10f), Frame(tracker, track));
    }

    [Fact]
    public void FindingTheCharacterAgainEasesFromTheRecordedAim()
    {
        var characters = new NearbyCharacters();
        var tracker = new AimTracker(characters);
        var track = Following(smoothing: 1f);
        Frame(tracker, track);

        characters.Update(GuardAt(0f).All);

        AimsAt(Vector3.Lerp(Recorded, new Vector3(0f, 0f, -10f), 1f - MathF.Exp(-1f)), Frame(tracker, track, 0.5f));
    }

    [Fact]
    public void ATargetOnTheCameraKeepsTheLastGoodAim()
    {
        var tracker = new AimTracker(null);
        var far = Single(AimMode.LookAt) with { LookAt = new Vector3(0f, 0f, -10f), LookAtPlaced = true };
        var near = far with { LookAt = new Vector3(0.05f, 0f, 0f) };
        Frame(tracker, far);

        AimsAt(new Vector3(0f, 0f, -10f), Frame(tracker, near));
        Assert.Equal(Recorded, Frame(new AimTracker(null), near).LookAt);
    }
}
```

Hand-traced values:
- Guard's feet are at (x, −1.3, −10) and the aim height is 1.3. `-1.3f + 1.3f` is exactly 0, so the aim point is (x, 0, −10).
- In the easing test, the first frame snaps to (0, 0, −10). The second, 0.5 s at smoothing 1, closes 1 − e⁻¹ of the gap to (10, 0, −10).
- In the found-again test, the first frame isn't found, so the smoother is seeded with `Recorded` (10 yalms along yaw 0.5). The next frame eases from there.
- In the last test, the near frame's evaluator falls back to recorded yaw 0.5, and the tracker puts back yaw 0 from the far frame. A fresh tracker has no good aim, so it keeps the recorded one.

- [ ] **Step 10: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter AimTrackerTests`
Expected: the build fails. `AimTracker` doesn't exist.

- [ ] **Step 11: The tracker**

`src/Vista.Core/Tracks/AimTracker.cs`:

```csharp
using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Aims a track in the world each frame: at its Look At point or its eased character, keeping the last good aim when the target is on the camera.</summary>
public sealed class AimTracker
{
    private readonly IAimTargets? targets;
    private readonly AimSmoother smoother = new();
    private (float Yaw, float Pitch)? lastAim;

    /// <summary>A tracker finding followed characters with <paramref name="targets"/>; with none, no character is ever found.</summary>
    public AimTracker(IAimTargets? targets) => this.targets = targets;

    /// <summary>The aim point on the character a track in the world follows, unsmoothed; null unless one is named and found.</summary>
    public static Vector3? CharacterAim(Track world, IAimTargets? targets)
        => world is { Aim: AimMode.FollowTarget, TargetName: { } name } && targets?.Find(name, world.Anchor.Position) is { } feet
            ? feet + (Vector3.UnitY * world.AimHeight)
            : null;

    /// <summary>True when a track in the world follows a named character who isn't found.</summary>
    public static bool TargetLost(Track world, IAimTargets? targets)
        => world is { Aim: AimMode.FollowTarget, TargetName: not null } && CharacterAim(world, targets) is null;

    /// <summary>The frame of <paramref name="world"/> at <paramref name="time"/>, <paramref name="dt"/> seconds after the last.</summary>
    public CameraState? Frame(TrackEvaluator evaluator, Track world, double time, float dt)
    {
        var target = Target(world, dt);
        if (evaluator.Evaluate(time, target) is not { } frame) return null;

        // Lost, the smoother waits on the recorded aim, so finding the character again eases from it.
        if (world.Aim == AimMode.FollowTarget && target is null) smoother.Seed(frame.LookAt);
        if (target is not { } at) return frame;

        if (TrackAim.Toward(frame.Position, at) is not null)
        {
            lastAim = TrackAim.FromDirection(frame.LookAt - frame.Position);
            return frame;
        }

        return lastAim is { } aim ? frame with { LookAt = FreeCamMotion.LookAtFrom(frame.Position, aim.Yaw, aim.Pitch) } : frame;
    }

    /// <summary>Starts the smoothing afresh, so the next frame lands on the character.</summary>
    public void Reset() => smoother.Reset();

    private Vector3? Target(Track world, float dt) => world.Aim switch
    {
        AimMode.LookAt when world.LookAtPlaced => world.LookAt,
        AimMode.FollowTarget when CharacterAim(world, targets) is { } aim => smoother.Step(aim, dt, world.Smoothing),
        _ => null,
    };
}
```

- [ ] **Step 12: Run them**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter AimTrackerTests`
Expected: PASS.

- [ ] **Step 13: Write the failing playback tests**

Add to `TrackPlaybackTests.cs`:

```csharp
    private static void AimsAt(Vector3 target, CameraState frame)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, 3);
        Assert.Equal(want.Y, got.Y, 3);
        Assert.Equal(want.Z, got.Z, 3);
    }

    private static void GuardAt(NearbyCharacters characters, float x)
        => characters.Update([new LoadedCharacter("Guard", new Vector3(x, -1.3f, -10f))]);

    [Fact]
    public void AFollowedCharacterIsEasedOntoAndASeekOrRestartSnapsBackOntoThem()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, 0f);
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), Point(0f, 0f, 0f)) with { TargetName = "Guard", Smoothing = 1f };
        var playback = new TrackPlayback(track, characters);

        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(1f / 60f)!.Value);

        GuardAt(characters, 10f);
        AimsAt(Vector3.Lerp(new Vector3(0f, 0f, -10f), new Vector3(10f, 0f, -10f), 1f - MathF.Exp(-1f)), playback.Advance(0.5f)!.Value);

        playback.Seek(0.0);
        AimsAt(new Vector3(10f, 0f, -10f), playback.Advance(1f / 60f)!.Value);

        GuardAt(characters, -10f);
        playback.Restart();
        AimsAt(new Vector3(-10f, 0f, -10f), playback.Advance(1f / 60f)!.Value);
    }
```

Add to `PlaylistPlaybackTests.cs` (add `using Vista.Core.Camera;` at the top):

```csharp
    // A single point at the origin, held 1 s, following Guard with heavy smoothing.
    private static Track Follow() => TrackEditing.SetHold(TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), Point(0f)), 0, 1f) with { TargetName = "Guard", Smoothing = 1f };

    private static void GuardAt(NearbyCharacters characters, float x)
        => characters.Update([new LoadedCharacter("Guard", new Vector3(x, -1.3f, -10f))]);

    private static void AimsAt(Vector3 target, CameraState frame)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, 3);
        Assert.Equal(want.Y, got.Y, 3);
        Assert.Equal(want.Z, got.Z, 3);
    }

    [Fact]
    public void ACutOrASeekStartsTheSmoothingAfresh()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, 0f);
        var playback = new PlaylistPlayback([Item(Follow()), Item(Follow())], targets: characters);
        AimsAt(new Vector3(0f, 0f, -10f), playback.Advance(0.1f)!.Value);

        GuardAt(characters, 10f);
        AimsAt(Vector3.Lerp(new Vector3(0f, 0f, -10f), new Vector3(10f, 0f, -10f), 1f - MathF.Exp(-1f)), playback.Advance(0.5f)!.Value);

        var cut = playback.Advance(0.5f)!.Value;
        Assert.Equal(1, playback.Index);
        AimsAt(new Vector3(10f, 0f, -10f), cut);

        GuardAt(characters, -10f);
        playback.Seek(0.2);
        AimsAt(new Vector3(-10f, 0f, -10f), playback.Advance(0.01f)!.Value);
    }
```

Trace of the cut test:
- The clock reaches 0.1, then 0.6, then 1.1. At 1.1 it cuts to entry 1 with 0.1 carried over.
- The reset on the cut makes that frame land on (10, 0, −10). Without it, the frame would ease to about (8.6, 0, −10).
- The seek to 0.2 on entry 1 resets again, so the next frame lands on (−10, 0, −10).

Add to `DirectorTests.cs`:

```csharp
    [Fact]
    public void LiveFollowsACharacterTheDirectorWasGiven()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", new Vector3(0f, -1.3f, -10f))]);
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), Point(0f, 0f, 0f)) with { TargetName = "Guard" };
        var director = new Director(characters);
        director.GoLive(new TrackShot(track));

        var frame = director.Tick(1f / 60f)!.Value;

        var look = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(0f, look.X, 4);
        Assert.Equal(0f, look.Y, 4);
        Assert.Equal(-1f, look.Z, 4);
    }
```

- [ ] **Step 14: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "TrackPlaybackTests|PlaylistPlaybackTests|DirectorTests"`
Expected: the build fails. The playbacks and the Director take no targets.

- [ ] **Step 15: Resolve targets in the playbacks and the Director**

In `TrackPlayback.cs`, add a field `private readonly AimTracker aim;`. Replace the constructor, and change `Advance`'s return, `Restart` and `Seek`:

```csharp
    /// <summary>Starts <paramref name="track"/>, a track in the world, at the start of its cycle; <paramref name="targets"/> finds a followed character.</summary>
    public TrackPlayback(Track track, IAimTargets? targets = null)
    {
        _track = track;
        _evaluator = new TrackEvaluator(track);
        aim = new AimTracker(targets);
    }
```

The last line of `Advance` becomes:

```csharp
        return aim.Frame(_evaluator, _track, ShotTime, Math.Max(dt, 0f));
```

`Restart` gains `aim.Reset();` as its first line, and so does `Seek`. Update their docs:

```csharp
    /// <summary>Puts the clock back to the start of the cycle, clears <see cref="IsFinished"/> and starts the smoothing afresh.</summary>
```

```csharp
    /// <summary>Jumps to shot time <paramref name="time"/>, clamped to the shot, keeping a Ping-pong shot's pass; the smoothing starts afresh.</summary>
```

In `PlaylistPlayback.cs`, add a field `private readonly AimTracker aim;`, then change the constructor:

```csharp
    /// <summary>Plays <paramref name="items"/> from the first, wrapping at the end when <paramref name="loops"/>; <paramref name="targets"/> finds followed characters. Refused when empty.</summary>
    public PlaylistPlayback(IReadOnlyList<PlaylistItem> items, bool loops = false, IAimTargets? targets = null)
    {
        if (items.Count == 0) throw new ArgumentException("A playlist needs an entry to play.");
        this.items = items;
        this.loops = loops;
        evaluators = items.Select(i => new TrackEvaluator(i.Track)).ToArray();
        aim = new AimTracker(targets);
    }
```

Replace `Advance`. Its logic is unchanged apart from `cut` and the frame:

```csharp
    /// <summary>Moves on by <paramref name="dt"/>, cutting to later entries as earlier ones finish, and returns the frame; a cut starts the smoothing afresh.</summary>
    public CameraState? Advance(float dt)
    {
        // A zero-length first entry gets its own frame before time starts moving.
        if (Index == 0 && !shownFirstFrame)
        {
            shownFirstFrame = true;
            if (Total == 0) return Frame(dt);
        }

        if (!IsFinished) clock += Math.Max(dt, 0f);

        var wrapped = false;
        var cut = false;
        while (!IsFinished && clock >= Total && (Total > 0 || clock > 0))
        {
            if (Index == items.Count - 1)
            {
                if (!loops)
                {
                    clock = Total;
                    IsFinished = true;
                    break;
                }

                // One wrap per Advance, so a very long frame or a playlist with no length never spins.
                if (wrapped)
                {
                    Index = 0;
                    clock = 0;
                    cut = true;
                    break;
                }

                wrapped = true;
                clock -= Total;
                Index = 0;
                cut = true;
            }
            else
            {
                clock -= Total;
                Index++;
                cut = true;
            }

            // A zero-length entry is shown for the frame it's reached on.
            if (Total == 0)
            {
                clock = 0;
                break;
            }
        }

        if (cut) aim.Reset();
        return Frame(dt);
    }

    /// <summary>The playing entry's frame now, aimed at its target.</summary>
    private CameraState? Frame(float dt) => aim.Frame(evaluators[Index], items[Index].Track, ShotTime, Math.Max(dt, 0f));
```

`Seek` and `Restart` each gain `aim.Reset();` as their last line. Their docs gain "; the smoothing starts afresh".

In `Director.cs`:

```csharp
    private readonly IAimTargets? targets;

    /// <summary>A Director whose playbacks find followed characters with <paramref name="targets"/>.</summary>
    public Director(IAimTargets? targets = null) => this.targets = targets;
```

In `GoLive`:

```csharp
            TrackShot t => (IPlayback)new TrackPlayback(t.Track, targets),
            PlaylistShot p => new PlaylistPlayback(p.Items, p.Loops, targets),
```

- [ ] **Step 16: Run them**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "TrackPlaybackTests|PlaylistPlaybackTests|DirectorTests"`
Expected: PASS, including every earlier test in those classes.

- [ ] **Step 17: Write the failing session tests**

`tests/Vista.Tests/Session/SessionAimTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionAimTests
{
    private static readonly Vector3 A = new(0f, 0f, -10f);
    private static readonly Vector3 B = new(10f, 0f, -10f);
    private static readonly Vector3 Eased = Vector3.Lerp(A, B, 1f - MathF.Exp(-1f));

    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Puts Guard's aim point, 1.3 above the feet, at <paramref name="aim"/>.
    private static void GuardAt(NearbyCharacters characters, Vector3 aim)
        => characters.Update([new LoadedCharacter("Guard", aim - new Vector3(0f, 1.3f, 0f))]);

    // Editing a 2 s track, x = 0 to 10, following Guard with heavy smoothing; Guard aimed at A.
    private static (SessionState State, NearbyCharacters Characters) Following()
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, A);
        var state = new SessionState(null, characters);
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.ChangeTrack(t => t with { Aim = AimMode.FollowTarget, TargetName = "Guard", Smoothing = 1f });
        return (state, characters);
    }

    private static void AimsAt(Vector3 target, CameraState frame)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, 3);
        Assert.Equal(want.Y, got.Y, 3);
        Assert.Equal(want.Z, got.Z, 3);
    }

    [Fact]
    public void ScrubbedFramesAimAtTheCharacterWhereTheyAreNow()
    {
        var (state, characters) = Following();
        AimsAt(A, state.FrameAt(0.0)!.Value);

        GuardAt(characters, B);

        AimsAt(B, state.FrameAt(0.0)!.Value);
    }

    [Fact]
    public void APreviewStartsOnTheCharacterAndEasesAfterThem()
    {
        var (state, characters) = Following();
        state.Play();
        AimsAt(A, state.AdvancePreview(1f / 60f)!.Value);

        GuardAt(characters, B);

        AimsAt(Eased, state.AdvancePreview(0.5f)!.Value);
    }

    [Fact]
    public void LiveFollowsTheCharacterAndAScrubSnapsBackOntoThem()
    {
        var (state, characters) = Following();
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();
        state.Play();
        AimsAt(A, state.Director.Tick(1f / 60f)!.Value);

        GuardAt(characters, B);
        AimsAt(Eased, state.Director.Tick(0.5f)!.Value);

        state.BeginScrub();
        state.ScrubTo(1.0);
        AimsAt(B, state.Director.Tick(1f / 60f)!.Value);
    }

    [Fact]
    public void TheSessionSaysWhenTheCharacterIsLost()
    {
        var (state, characters) = Following();
        Assert.False(state.TargetLost(state.Track));
        Assert.Equal(A, state.CharacterAim(state.Track));

        characters.Update([]);

        Assert.True(state.TargetLost(state.Track));
        Assert.Null(state.CharacterAim(state.Track));
    }
}
```

Hand-traced details:
- Guard's feet are at `aim − (0, 1.3, 0)`, and the aim point adds 1.3 back. For A and B the Y is `0 − 1.3f + 1.3f`, which is exactly 0.
- The scrub test works like this. `BeginScrub` pauses the Director and `ScrubTo(1.0)` seeks, which resets the tracker. The paused tick passes `dt = 0` to a fresh smoother, and that lands on B.

- [ ] **Step 18: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter SessionAimTests`
Expected: the build fails. `SessionState` takes no targets and has no `CharacterAim`.

- [ ] **Step 19: Wire targets through the session**

In `SessionState.cs`, add the fields after `preview`:

```csharp
    private readonly IAimTargets? aimTargets;
    private readonly AimTracker scrubAim;
```

Replace `public Director Director { get; } = new();` with:

```csharp
    public Director Director { get; }
```

Replace the constructor:

```csharp
    /// <summary>A session; <paramref name="groundBelow"/> finds the ground's height under a world point, or null when it can't, and <paramref name="aimTargets"/> finds followed characters.</summary>
    public SessionState(Func<Vector3, float?>? groundBelow = null, IAimTargets? aimTargets = null)
    {
        this.groundBelow = groundBelow ?? (_ => null);
        this.aimTargets = aimTargets;
        scrubAim = new AimTracker(aimTargets);
        Director = new Director(aimTargets);
        EditedTrackId = Scene.Tracks[0].Id;
    }
```

In `StartPreview`: `var playback = new TrackPlayback(Track, aimTargets);`

Replace `FrameAt` and add the two queries after it:

```csharp
    /// <summary>The track's frame at <paramref name="time"/> seconds, aimed at its target where it is now, or null with no points.</summary>
    public CameraState? FrameAt(double time)
    {
        if (Local.Points.Count == 0) return null;
        scrubAim.Reset();
        return scrubAim.Frame(Evaluator, Track, time, 0f);
    }

    /// <summary>The aim point on the character a track in the world follows, or null unless one is named and found.</summary>
    public Vector3? CharacterAim(Track world) => AimTracker.CharacterAim(world, aimTargets);

    /// <summary>True when a track in the world follows a named character who isn't found.</summary>
    public bool TargetLost(Track world) => AimTracker.TargetLost(world, aimTargets);
```

In `CameraSession.cs`, add `using System.Numerics;` and, after `WorldOf`:

```csharp
    /// <summary>The aim point on the character a track in the world follows, or null unless one is found.</summary>
    public Vector3? CharacterAim(Track world) => state.CharacterAim(world);

    /// <summary>True when a track in the world follows a named character who isn't found nearby.</summary>
    public bool TargetLost(Track world) => state.TargetLost(world);
```

- [ ] **Step 20: Run all tests and build**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`, then `./build.sh`
Expected: all pass, 0 warnings.

- [ ] **Step 21: Commit**

```bash
git add src/Vista.Core/Tracks src/Vista.Core/Session/SessionState.cs src/Vista.Plugin/Session/CameraSession.cs tests/Vista.Tests/Tracks tests/Vista.Tests/Session/SessionAimTests.cs
git commit -m "feat(tracks) aim at a look at point or an eased character in playback, preview and scrub"
```

---

### Task 3: Selecting and editing the Look At point, and the aim settings as undo steps

**Files:**
- Modify: `src/Vista.Core/Session/AnchorKind.cs`, `src/Vista.Core/Editing/TrackMarkerHitTest.cs`, `src/Vista.Core/Session/SessionState.cs` (the parts listed below), `src/Vista.Plugin/Session/CameraSession.cs` (the parts listed below)
- Test: `tests/Vista.Tests/Session/SessionLookAtTests.cs` (new), `tests/Vista.Tests/Editing/TrackMarkerHitTestTests.cs`

**Interfaces:**
- Consumes (Task 1): `TrackEditing.SetAim`, `SetLookAt`, `SetTarget`, `SetAimHeight`, `SetSmoothing`. The world track's `LookAt`.
- Produces:
  - `enum AnchorKind { Scene, Track, LookAt }` (`Vista.Core.Session`)
  - `enum MarkerKind { Point, TrackAnchor, SceneAnchor, LookAt }` (`Vista.Core.Editing`). `TrackMarkerHitTest.Nearest` ranks the edited track's Look At point after its anchor, and other tracks' Look At points after their anchors.
  - `SessionState` members:
    - `string? SetAim(AimMode aim, ControlPoint camera)`, where `camera` is in the world
    - `string? SetTarget(string? name)`
    - `string? SetAimHeight(float yalms)`
    - `string? SetSmoothing(float smoothing)`
    - `string? SelectLookAt(Guid id)`
    - `Vector3? SelectedLookAtInWorld`
    - `string? MoveLookAt(Vector3 world)`
    - `string? PreviewLookAt(Vector3 world)`
  - `CameraSession` members:
    - `string? SetAim(AimMode aim)`, which reads the camera itself
    - `string? SetTarget(string? name)`
    - `string? SetAimHeight(float yalms)`
    - `string? SetSmoothing(float smoothing)`
    - `string? SelectLookAt(Guid id)`
    - `Vector3? SelectedLookAtInWorld`
    - `string? MoveLookAt(Vector3 world)`
    - `string? PreviewLookAt(Vector3 world)`

- [ ] **Step 1: Write the failing session tests**

`tests/Vista.Tests/Session/SessionLookAtTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionLookAtTests
{
    private const float Tolerance = 1e-4f;

    private static readonly ControlPoint Camera = new(new Vector3(0f, 5f, 0f), 0f, 0f, 1f);

    private static ControlPoint Point(float x, float y = 5f, float z = 0f) => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    // Editing with the ground at y = 1; Track 1 has points at x = 10, 20, 30 (y = 5), all aimed along −z.
    private static SessionState Editing()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddToEnd(Point(30f));
        return state;
    }

    // Editing() set to Look At; its point sits 10 yalms along the first point's aim, at (10, 5, −10).
    private static SessionState Looking()
    {
        var state = Editing();
        state.SetAim(AimMode.LookAt, Camera);
        return state;
    }

    [Fact]
    public void ChoosingLookAtPlacesThePointAlongTheFirstPointsAimAsOneUndoStep()
    {
        var state = Editing();

        Assert.Null(state.SetAim(AimMode.LookAt, Camera));

        Near(new Vector3(10f, 5f, -10f), state.Track.LookAt);
        Assert.True(state.Undo());
        Assert.Equal(AimMode.AimKeys, state.Track.Aim);
        Assert.False(state.Track.LookAtPlaced);
    }

    [Fact]
    public void WithNoPointsItGoesAheadOfTheCameraAndStaysThereWhenTheFirstPointIsAdded()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.SetAim(AimMode.LookAt, new ControlPoint(new Vector3(50f, 5f, 50f), 0f, 0f, 1f));
        Near(new Vector3(50f, 5f, 40f), state.Track.LookAt);

        state.AddToEnd(Point(10f));

        Near(new Vector3(50f, 5f, 40f), state.Track.LookAt);
    }

    [Fact]
    public void TheFollowSettingsAreEachOneUndoStep()
    {
        var state = Editing();
        Assert.Null(state.SetAim(AimMode.FollowTarget, Camera));
        Assert.Null(state.SetTarget("Guard"));
        Assert.Null(state.SetAimHeight(2f));
        Assert.Null(state.SetSmoothing(0.8f));
        Assert.Equal(0.8f, state.Track.Smoothing);

        state.Undo();
        Assert.Equal(0.3f, state.Track.Smoothing);
        Assert.Equal(2f, state.Track.AimHeight);
        state.Undo();
        Assert.Equal(1.3f, state.Track.AimHeight);
        state.Undo();
        Assert.Null(state.Track.TargetName);
        state.Undo();
        Assert.Equal(AimMode.AimKeys, state.Track.Aim);
    }

    [Fact]
    public void TheSettingsAreRefusedUnlessEditing()
    {
        var state = new SessionState();

        Assert.NotNull(state.SetAim(AimMode.LookAt, Camera));
        Assert.NotNull(state.SetTarget("Guard"));
        Assert.NotNull(state.SelectLookAt(state.EditedTrackId));
    }

    [Fact]
    public void TheLookAtPointCanBeSelectedOnlyUnderLookAt()
    {
        var state = Editing();
        state.Select(1);
        Assert.NotNull(state.SelectLookAt(state.EditedTrackId));

        state.SetAim(AimMode.LookAt, Camera);

        Assert.Null(state.SelectLookAt(state.EditedTrackId));
        Assert.Equal(AnchorKind.LookAt, state.SelectedAnchor);
        Assert.Null(state.Selected);
        Assert.Null(state.SelectedAnchorInWorld);
        Near(new Vector3(10f, 5f, -10f), state.SelectedLookAtInWorld!.Value);
    }

    [Fact]
    public void SelectingAnotherTracksLookAtSwitchesToIt()
    {
        var state = Looking();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));

        Assert.Null(state.SelectLookAt(first));

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(AnchorKind.LookAt, state.SelectedAnchor);
    }

    [Fact]
    public void MovingTheLookAtLandsWhereItWasPutUnderTurnedAnchorsAsOneUndoStep()
    {
        var state = Looking();
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(new Vector3(100f, 1f, 20f), 0.7f), carry: true);
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(80f, 1f, 30f), -1.1f), carry: true);
        state.SelectLookAt(state.EditedTrackId);
        var before = state.SelectedLookAtInWorld!.Value;
        var target = new Vector3(12f, 6f, 8f);

        Assert.Null(state.MoveLookAt(target));

        Near(target, state.SelectedLookAtInWorld!.Value);
        Assert.True(state.Undo());
        Near(before, state.Track.LookAt);
    }

    [Fact]
    public void ALookAtDragIsOneUndoStep()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);
        var start = state.Track.LookAt;

        state.BeginLiveEdit();
        Assert.Null(state.PreviewLookAt(new Vector3(0f, 5f, 0f)));
        Assert.Null(state.PreviewLookAt(new Vector3(3f, 5f, 0f)));
        state.EndLiveEdit();

        Near(new Vector3(3f, 5f, 0f), state.Track.LookAt);
        Assert.True(state.Undo());
        Near(start, state.Track.LookAt);
    }

    [Fact]
    public void TheTrackAnchorCarriesTheLookAtAndAloneLeavesItInTheWorld()
    {
        var state = Looking();
        var before = state.Track.LookAt;
        state.SelectTrackAnchor(state.EditedTrackId);

        state.MoveAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false);
        Near(before, state.Track.LookAt);

        state.MoveAnchor(new Anchor(new Vector3(6f, 0f, 9f), 1.3f), carry: true);
        Near(before + new Vector3(10f, 0f, 0f), state.Track.LookAt);
    }

    [Fact]
    public void LeavingLookAtDropsItsSelection()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);

        state.SetAim(AimMode.AimKeys, Camera);

        Assert.Null(state.SelectedAnchor);
        Assert.Null(state.SelectedLookAtInWorld);
    }

    [Fact]
    public void TheLookAtMovesOnlyWhenSelectedAndIsNoAnchor()
    {
        var state = Looking();
        Assert.NotNull(state.MoveLookAt(Vector3.Zero));

        state.SelectLookAt(state.EditedTrackId);

        Assert.NotNull(state.MoveAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        state.BeginLiveEdit();
        Assert.NotNull(state.PreviewAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        state.EndLiveEdit();
    }
}
```

Hand-traced values:
- The first point is stored at local (0, 4, 0) under the anchors at (10, 1, 0) with yaw 0. Yaw 0 faces −z, so the Look At point is local (0, 4, −10), which is (10, 5, −10) in the world.
- With no points the anchors are unplaced, so local is the world, and the camera case gives (50, 5, 40). Task 1's `PlaceFor` keeps that point in the world when the first point places the anchors.
- In the anchor test, moving the anchor with carry keeps its yaw at 1.3 and shifts it by +10 in x. The carried point moves the same way.

- [ ] **Step 2: Write the failing hit-test tests**

Add to `TrackMarkerHitTestTests.cs`:

```csharp
    [Fact]
    public void TheEditedTracksLookAtComesAfterItsAnchorAndBeforeOtherTracks()
    {
        var markers = new[]
        {
            new TrackMarker(Edited, -1, new Vector2(100f, 100f), MarkerKind.LookAt),
            new TrackMarker(Edited, -1, new Vector2(104f, 100f), MarkerKind.TrackAnchor),
            new TrackMarker(Other, 0, new Vector2(100f, 100f)),
        };

        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(0, TrackMarkerHitTest.Nearest([markers[0], markers[2]], Edited, new Vector2(100f, 100f), 10f));
    }

    [Fact]
    public void AnotherTracksLookAtComesAfterItsAnchorAndBeforeTheSceneAnchor()
    {
        var markers = new[]
        {
            new TrackMarker(Guid.Empty, -1, new Vector2(100f, 100f), MarkerKind.SceneAnchor),
            new TrackMarker(Other, -1, new Vector2(105f, 100f), MarkerKind.LookAt),
            new TrackMarker(Other, -1, new Vector2(108f, 100f), MarkerKind.TrackAnchor),
        };

        Assert.Equal(2, TrackMarkerHitTest.Nearest(markers, Edited, new Vector2(100f, 100f), 10f));
        Assert.Equal(1, TrackMarkerHitTest.Nearest(markers[..2], Edited, new Vector2(100f, 100f), 10f));
    }
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter "SessionLookAtTests|TrackMarkerHitTestTests"`
Expected: the build fails. `AnchorKind.LookAt`, `MarkerKind.LookAt` and the session members don't exist.

- [ ] **Step 4: The kinds and the hit test**

`AnchorKind.cs`:

```csharp
namespace Vista.Core.Session;

/// <summary>What is selected in place of a point: the scene's anchor, the edited track's anchor, or its Look At point.</summary>
public enum AnchorKind { Scene, Track, LookAt }
```

In `TrackMarkerHitTest.cs`:

```csharp
/// <summary>What a marker on screen stands for.</summary>
public enum MarkerKind { Point, TrackAnchor, SceneAnchor, LookAt }

/// <summary>One marker on screen; <see cref="Screen"/> is null when off screen, and anchors and Look At points use point −1.</summary>
public readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen, MarkerKind Kind = MarkerKind.Point);
```

```csharp
    /// <summary>The index of the hit marker, or null: edited points, anchor and Look At, then other points, anchors and Look Ats, then the scene anchor.</summary>
    public static int? Nearest(IReadOnlyList<TrackMarker> markers, Guid edited, Vector2 cursor, float radius)
        => NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.Point && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.TrackAnchor && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.LookAt && m.Track == edited, laterWinsTie: false)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.Point && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.TrackAnchor && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.LookAt && m.Track != edited, laterWinsTie: true)
        ?? NearestOf(markers, cursor, radius, m => m.Kind == MarkerKind.SceneAnchor, laterWinsTie: false);
```

- [ ] **Step 5: The session edits and the Look At selection**

In `SessionState.cs`:

After `UnifyHandles`, add the settings:

```csharp
    /// <summary>Sets the aim mode; the first Look At places its point from the first point, or from the world <paramref name="camera"/> with no points. Returns why it was refused, or null.</summary>
    public string? SetAim(AimMode aim, ControlPoint camera)
    {
        var local = ToLocal(camera);
        return ApplySetting(t => TrackEditing.SetAim(t, aim, local));
    }

    /// <summary>Names the character to follow, or none. Returns why it was refused, or null.</summary>
    public string? SetTarget(string? name) => ApplySetting(t => TrackEditing.SetTarget(t, name));

    /// <summary>Sets the aim height above the character's feet. Returns why it was refused, or null.</summary>
    public string? SetAimHeight(float yalms) => ApplySetting(t => TrackEditing.SetAimHeight(t, yalms));

    /// <summary>Sets how heavily the aim eases onto the character. Returns why it was refused, or null.</summary>
    public string? SetSmoothing(float smoothing) => ApplySetting(t => TrackEditing.SetSmoothing(t, smoothing));

    /// <summary>Applies a change to the edited track's settings as one undo step, keeping the selection.</summary>
    private string? ApplySetting(Func<Track, Track> change) => CommitEdit(ChangeEdited(change), _ => Selected);
```

Change the doc of `SelectedAnchorInWorld` to "The selected scene or track anchor in the world, or null." Its switch already returns null for `LookAt`.

After `SelectTrackAnchor`, add:

```csharp
    /// <summary>Edits track <paramref name="id"/> and selects its Look At point, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectLookAt(Guid id)
    {
        if (Mode != CameraMode.Editing) return "The Look At point can only be selected while editing.";
        if (SceneEditing.IndexOf(Scene, id) < 0) return "There is no such track.";
        if (SceneEditing.Get(Scene, id) is not { Aim: AimMode.LookAt, LookAtPlaced: true }) return LookAtUnused;
        if (SwitchTrack(id) is { } refusal) return refusal;
        SelectAnchor(AnchorKind.LookAt);
        return null;
    }

    /// <summary>The selected Look At point in the world, or null.</summary>
    public Vector3? SelectedLookAtInWorld => SelectedAnchor == AnchorKind.LookAt ? Track.LookAt : null;
```

Add the constant beside the other two:

```csharp
    private const string LookAtUnused = "The Look At point is used only while the track aims at it.";
```

Replace `UnplacedRefusal`:

```csharp
    /// <summary>Why the <paramref name="kind"/> selection cannot be used: an unplaced anchor, or a Look At point not in use.</summary>
    private string? UnplacedRefusal(AnchorKind kind) => kind switch
    {
        AnchorKind.Scene => Scene.AnchorPlaced ? null : SceneAnchorUnplaced,
        AnchorKind.Track => Local.AnchorPlaced ? null : TrackAnchorUnplaced,
        _ => Local is { Aim: AimMode.LookAt, LookAtPlaced: true } ? null : LookAtUnused,
    };
```

In both `MoveAnchor` and `PreviewAnchor`, change the selection check to:

```csharp
        if (SelectedAnchor is not { } kind || kind == AnchorKind.LookAt) return "Select an anchor first.";
```

After `Moved`, add:

```csharp
    /// <summary>Moves the selected Look At point to <paramref name="world"/> as one undo step. Returns why it was refused, or null.</summary>
    public string? MoveLookAt(Vector3 world)
    {
        if (SelectedAnchor != AnchorKind.LookAt) return "Select the Look At point first.";
        var local = SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world);
        return ApplySetting(t => TrackEditing.SetLookAt(t, local));
    }

    /// <summary>During a live edit, moves the selected Look At point to <paramref name="world"/>. Returns why it was refused, or null.</summary>
    public string? PreviewLookAt(Vector3 world)
    {
        if (liveEditStart is null) return "No live edit is in progress.";
        if (SelectedAnchor != AnchorKind.LookAt) return "Select the Look At point first.";
        if (UnplacedRefusal(AnchorKind.LookAt) is { } unused) return unused;
        Local = TrackEditing.SetLookAt(Local, SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world));
        return null;
    }
```

A selected Look At point implies it is in use, because `CommitEdit` drops the selection otherwise. `MoveLookAt` therefore needs no separate in-use check; `CommitEdit` refuses outside editing.

In `CommitEdit`, after `Selected = selected;`, add:

```csharp
            if (SelectedAnchor is { } kind && UnplacedRefusal(kind) is not null) SelectedAnchor = null;
```

- [ ] **Step 6: Pass them through `CameraSession`**

In `CameraSession.cs`, add `using System.Numerics;` if it isn't already there; Task 2 adds it too.

After `PreviewAnchor`:

```csharp
    /// <summary>Edits a track and selects its Look At point. Returns why it was refused, or null.</summary>
    public string? SelectLookAt(Guid id) => state.SelectLookAt(id);

    /// <summary>The selected Look At point in the world, or null.</summary>
    public Vector3? SelectedLookAtInWorld => state.SelectedLookAtInWorld;

    /// <summary>Moves the selected Look At point. Returns why it was refused, or null.</summary>
    public string? MoveLookAt(Vector3 world) => state.MoveLookAt(world);

    /// <summary>During a live edit, moves the selected Look At point. Returns why it was refused, or null.</summary>
    public string? PreviewLookAt(Vector3 world) => state.PreviewLookAt(world);
```

After `UnifyHandles`:

```csharp
    /// <summary>Sets the aim mode; the first Look At with no points goes ahead of the camera. Returns why it was refused, or null.</summary>
    public string? SetAim(AimMode aim) => CameraPoint() is { } camera ? state.SetAim(aim, camera) : "Cannot read the camera.";

    /// <summary>Names the character to follow, or none. Returns why it was refused, or null.</summary>
    public string? SetTarget(string? name) => state.SetTarget(name);

    /// <summary>Sets the aim height above the character's feet. Returns why it was refused, or null.</summary>
    public string? SetAimHeight(float yalms) => state.SetAimHeight(yalms);

    /// <summary>Sets how heavily the aim eases onto the character. Returns why it was refused, or null.</summary>
    public string? SetSmoothing(float smoothing) => state.SetSmoothing(smoothing);
```

Replace `WithCurrentPoint` with it and `CameraPoint`:

```csharp
    /// <summary>Runs <paramref name="edit"/> with the current camera as a control point, or the previewed frame while previewing.</summary>
    private string? WithCurrentPoint(Func<ControlPoint, string?> edit)
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be added while editing.";
        if (state.Scrubbing) return "Points cannot be added while scrubbing.";
        return CameraPoint() is { } point ? edit(point) : "Cannot read the camera.";
    }

    /// <summary>The current camera as a control point, or the previewed frame while previewing; null when the camera can't be read.</summary>
    private ControlPoint? CameraPoint()
    {
        if (state.Previewing && lastFrame is { } previewed)
        {
            var (previewYaw, previewPitch) = TrackAim.FromDirection(previewed.LookAt - previewed.Position);
            return new ControlPoint(previewed.Position, previewYaw, previewPitch, previewed.Fov, previewed.Roll);
        }

        var camera = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (camera is null || angles is null) return null;

        var (yaw, pitch) = angles.Value;
        return new ControlPoint(camera.Value.Position, yaw, pitch, camera.Value.Fov, freeCam.Roll);
    }
```

- [ ] **Step 7: Run all tests and build**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`, then `./build.sh`
Expected: all pass, including `SessionAnchorTests`, with 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Vista.Core/Session/AnchorKind.cs src/Vista.Core/Editing/TrackMarkerHitTest.cs src/Vista.Core/Session/SessionState.cs src/Vista.Plugin/Session/CameraSession.cs tests/Vista.Tests/Session/SessionLookAtTests.cs tests/Vista.Tests/Editing/TrackMarkerHitTestTests.cs
git commit -m "feat(session) select and move the look at point and set aim as undo steps"
```

---

### Task 4: Characters from the game, and Follow Target in the track row and playlist

**Files:**
- Create: `src/Vista.Plugin/Game/CharacterTable.cs`
- Modify: `src/Vista.Plugin/Plugin.cs`, `src/Vista.Plugin/Session/CameraSession.cs` (the `state` field, the constructor, and new members after `Director`), `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, `src/Vista.Plugin/Ui/PlaylistPanel.cs`, `src/Vista.Plugin/Ui/IconButton.cs`

**Interfaces:**
- Consumes:
  - Task 2: `NearbyCharacters`, `LoadedCharacter`, `SessionState(groundBelow, aimTargets)`, `CameraSession.TargetLost(Track)`.
  - Task 3: `CameraSession.SetAim(AimMode)`, `SetTarget(string?)`, `SetAimHeight(float)`, `SetSmoothing(float)`.
- Produces:
  - `CameraSession.Characters : NearbyCharacters`
  - `CameraSession.RefreshCharacters()`
  - `CameraSession.CameraPosition : Vector3?`
  - `IconButton.TargetNotFound()` and `IconButton.WarningWidth()`

No Core tests; this task is plugin UI. The checks are the build and the in-game checklist.

- [ ] **Step 1: Read the characters**

`src/Vista.Plugin/Game/CharacterTable.cs`:

```csharp
using Dalamud.Game.ClientState.Objects.Enums;
using Vista.Core.Tracks;

namespace Vista.Plugin.Game;

/// <summary>Reads the players and NPCs loaded nearby from the object table.</summary>
internal static class CharacterTable
{
    /// <summary>Every loaded player and NPC with a name, and where its feet are. Main thread only.</summary>
    public static IReadOnlyList<LoadedCharacter> Read()
    {
        var found = new List<LoadedCharacter>();
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind is not (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc)) continue;
            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name)) continue;
            found.Add(new LoadedCharacter(name, obj.Position));
        }

        return found;
    }
}
```

In Dalamud 15, `ObjectKind` is generated in `Dalamud.Game.ClientState.Objects.Enums` from ClientStructs' enum (see `~/code/Dalamud/Dalamud/EnumCloneMap.txt`), so it isn't in the clone's source tree. If that namespace doesn't resolve, use `FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind`, which has the same members.

- [ ] **Step 2: Give the session the characters**

In `CameraSession.cs`, replace `private readonly SessionState state = new(Ground.Below);` with:

```csharp
    private readonly NearbyCharacters characters = new();
    private readonly SessionState state;
```

Replace the constructor:

```csharp
    public CameraSession(MovementLock movement)
    {
        this.movement = movement;
        state = new SessionState(Ground.Below, characters);
    }
```

After `Director`, add:

```csharp
    /// <summary>The characters loaded nearby, as last read.</summary>
    public NearbyCharacters Characters => characters;

    /// <summary>Reads the characters loaded nearby. Call once a frame from Framework.Update.</summary>
    public void RefreshCharacters() => characters.Update(CharacterTable.Read());

    /// <summary>Where the camera is now, or null when it can't be read.</summary>
    public Vector3? CameraPosition => CameraAccess.ReadState()?.Position;
```

In `Plugin.OnFrameworkUpdate`, after `editorKeys.Update(Session, pointGizmo);`, add:

```csharp
        Session.RefreshCharacters();
```

- [ ] **Step 3: The warning icon**

In `IconButton.cs`, change the class doc to "Frameless icon buttons with a tooltip, their toggle and row-action variants, the not-found warning, and their width for right-aligning them." Add:

```csharp
    /// <summary>The tooltip on every warning that a followed character can't be found.</summary>
    public const string NotFoundTooltip = "Not found nearby: using recorded aim";

    /// <summary>A red warning icon saying the followed character can't be found, its tooltip shown even while disabled.</summary>
    public static void TargetNotFound()
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red))
            ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(NotFoundTooltip);
    }

    /// <summary>The warning icon's width.</summary>
    public static float WarningWidth()
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(FontAwesomeIcon.ExclamationTriangle.ToIconString()).X;
    }
```

- [ ] **Step 4: The aim menu**

In `TrackEditorWindow.cs`:

```csharp
    private static readonly string[] AimNames = ["Recorded aim", "Direction of travel", "Look At", "Follow Target"];
    private static readonly AimMode[] AimModes = [AimMode.AimKeys, AimMode.PathTangent, AimMode.LookAt, AimMode.FollowTarget];
    private const float CharacterWidth = 140f;
    private const float SmallFieldWidth = 50f;
    private float? smoothingDrag;
```

At the top of `DrawTrackRow`, replace the aim icon and menu with:

```csharp
        var aim = Array.IndexOf(AimModes, session.Track.Aim);
        if (IconButton.Draw("aim", FontAwesomeIcon.Crosshairs, $"Select aim ({AimNames[aim]})")) ImGui.OpenPopup("aim-menu");
        if (ImGui.BeginPopup("aim-menu"))
        {
            for (var i = 0; i < AimNames.Length; i++)
            {
                if (!ImGui.Selectable(AimNames[i], i == aim) || i == aim) continue;
                Report(session.SetAim(AimModes[i]));
            }

            ImGui.EndPopup();
        }

        if (session.Track.Aim == AimMode.FollowTarget)
        {
            ImGui.SameLine();
            DrawCharacter();
            ImGui.SameLine();
            DrawAimHeight();
            ImGui.SameLine();
            DrawSmoothing();
        }
```

Update the method's doc: "Aim and direction icons with their menus, the character, aim height and smoothing under Follow Target, the loop toggle, the Speed and Duration fields, the add button and its menu, and Clear track at the right end."

- [ ] **Step 5: The character button, aim height and smoothing**

Add these methods to `TrackEditorWindow`:

```csharp
    /// <summary>The followed character's button: its name, or red with a warning and "(Not found)" when not loaded; opens the nearby list.</summary>
    private void DrawCharacter()
    {
        var track = session.Track;
        var lost = session.TargetLost(track);
        if (lost)
        {
            IconButton.TargetNotFound();
            ImGui.SameLine(0f, 4f);
        }

        var label = track.TargetName is not { } name ? "Choose a character" : lost ? $"{name} (Not found)" : name;
        bool pressed;
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red, lost))
            pressed = ImGui.Button($"{label}##character", new Vector2(CharacterWidth, 0f));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(lost ? IconButton.NotFoundTooltip : "Choose a character");
        if (pressed) ImGui.OpenPopup("character-menu");
        DrawCharacterMenu(track.TargetName);
    }

    /// <summary>The characters loaded nearby, nearest the camera first, each with its distance; picking one follows it.</summary>
    private void DrawCharacterMenu(string? chosen)
    {
        if (!ImGui.BeginPopup("character-menu")) return;

        var camera = session.CameraPosition ?? Vector3.Zero;
        var nearby = session.Characters.NearestTo(camera);
        if (nearby.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
                ImGui.TextUnformatted("No characters nearby");
        }

        for (var i = 0; i < nearby.Count; i++)
        {
            var character = nearby[i];
            using var id = ImRaii.PushId($"character{i}");
            if (ImGui.Selectable($"{character.Name}  ({Vector3.Distance(character.Position, camera):0.0} yalms)", character.Name == chosen))
                Report(session.SetTarget(character.Name));
        }

        ImGui.EndPopup();
    }

    /// <summary>The aim height above the character's feet, as a small field.</summary>
    private void DrawAimHeight()
    {
        fields.Draw("aim-height", session.Track.AimHeight, "%.1f", SmallFieldWidth, v => Report(session.SetAimHeight(v)));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Aim height above the character's feet, in yalms");
    }

    /// <summary>The smoothing slider; a drag is applied as one undo step when it lets go.</summary>
    private void DrawSmoothing()
    {
        var value = smoothingDrag ?? session.Track.Smoothing;
        ImGui.SetNextItemWidth(SmallFieldWidth);
        if (ImGui.SliderFloat("##smoothing", ref value, 0f, 1f, "%.2f")) smoothingDrag = value;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Smoothing: 0 exact, 1 heavy");
        if (ImGui.IsItemActive() || smoothingDrag is not { } done) return;
        smoothingDrag = null;
        Report(session.SetSmoothing(done));
    }
```

- [ ] **Step 6: The track row's width**

Change `TrackRowWidth` to take `bool following`:

```csharp
    /// <summary>The track row's full width: its items, the Follow Target items when <paramref name="following"/>, the gaps between them, and the window padding.</summary>
    private static float TrackRowWidth(bool following)
    {
        var style = ImGui.GetStyle();
        var direction = DirectionIcons.Max(IconButton.Width);
        var items = IconButton.Width(FontAwesomeIcon.Crosshairs) + direction + IconButton.Width(FontAwesomeIcon.Repeat)
            + IconWidth(FontAwesomeIcon.TachometerAlt) + IconWidth(FontAwesomeIcon.Stopwatch) + (FieldWidth * 2f)
            + IconButton.Width(FontAwesomeIcon.Plus) + IconButton.Width(FontAwesomeIcon.CaretDown) + IconButton.Width(FontAwesomeIcon.Trash);
        var follow = following ? IconButton.WarningWidth() + 4f + CharacterWidth + (SmallFieldWidth * 2f) + (Spacing.X * 3f) : 0f;
        return items + follow + (Spacing.X * 8f) + (style.WindowPadding.X * 2f);
    }
```

In `PreDraw`, call `TrackRowWidth(session.Track.Aim == AimMode.FollowTarget)`.

- [ ] **Step 7: The playlist warning**

In `PlaylistPanel.DrawRow`, replace the lines from `var remove = …` through the `Selectable` with the following. Then add the warning after `DropTarget(scene, index, editing);`:

```csharp
        var remove = IconButton.Width(FontAwesomeIcon.Times);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var track = SceneEditing.Get(scene, entry.TrackId);
        var lost = session.TargetLost(session.WorldOf(track));
        var warning = lost ? IconButton.WarningWidth() + gap : 0f;
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - LoopWidth - remove - (gap * 2f) - warning);
        var name = track.Name;
        ImGui.Selectable($"{index + 1}  {name}", entry.Id == playing, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(nameWidth, ImGui.GetFrameHeight()));
```

```csharp
        DropTarget(scene, index, editing);

        if (lost)
        {
            ImGui.SameLine();
            IconButton.TargetNotFound();
        }
```

The panel's entries sit inside `BeginDisabled(!editing)`. The tooltip uses `AllowWhenDisabled`, so the operator still sees it in Live. Update `DrawRow`'s doc: "One entry: its number and track, a warning when its followed character isn't found, drag to reorder or drop a track on it, its loop cell and its remove button, shown on hover; greyed when never reached."

- [ ] **Step 8: Build and test**

Run: `./build.sh`, then `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: 0 warnings, all pass.

- [ ] **Step 9: Commit**

```bash
git add src/Vista.Plugin/Game/CharacterTable.cs src/Vista.Plugin/Plugin.cs src/Vista.Plugin/Session/CameraSession.cs src/Vista.Plugin/Ui/TrackEditorWindow.cs src/Vista.Plugin/Ui/PlaylistPanel.cs src/Vista.Plugin/Ui/IconButton.cs
git commit -m "feat(ui) choose look at or a character to follow and warn when not found"
```

---

### Task 5: The Look At point in the editor and the Point window

**Files:**
- Modify: `src/Vista.Plugin/Ui/PointWindow.cs`, `src/Vista.Plugin/Editor/Overlay.cs`, `src/Vista.Plugin/Editor/EditorLayer.cs`, `src/Vista.Plugin/Editor/AnchorGizmo.cs`, `src/Vista.Plugin/Editor/PointGizmo.cs`, `src/Vista.Plugin/Editor/EditorKeys.cs`

**Interfaces:**
- Consumes:
  - Task 2: `TrackAim.Toward`, `CameraSession.CharacterAim(Track)`.
  - Task 3: `AnchorKind.LookAt`, `MarkerKind.LookAt`, `CameraSession.SelectLookAt(Guid)`, `SelectedLookAtInWorld`, `PreviewLookAt(Vector3)`.
- Produces:
  - `Overlay.DrawLookAt(EditorView view, Vector3 world, Vector3? firstPoint, bool edited, bool selected) : Vector2?`
  - `Overlay.DrawTargetMarker(EditorView view, Vector3 world)`

No Core tests; this task is plugin UI. The checks are the build and the in-game checklist.

- [ ] **Step 1: Draw the Look At point and the character marker**

In `Overlay.cs`, add the constants:

```csharp
    private const float LookAtCross = 0.5f;
    private const float TargetCross = 0.25f;
```

Add after `DrawSceneAnchor`:

```csharp
    /// <summary>A Look At point: a crosshair in the anchor colour and a faint line to the first point. Returns its centre on screen, or null.</summary>
    public Vector2? DrawLookAt(EditorView view, Vector3 world, Vector3? firstPoint, bool edited, bool selected)
    {
        var list = ImGui.GetBackgroundDrawList();
        var colour = selected ? EditorColours.Selected : edited ? EditorColours.Anchor : EditorColours.OtherAnchor;
        if (firstPoint is { } first) DrawEdge(list, view, world, first, edited ? EditorColours.AnchorLink : EditorColours.OtherAnchorLink, GlyphThickness);
        DrawCross(list, view, world, LookAtCross, colour, selected ? SelectedGlyphThickness : GlyphThickness);
        return view.ToScreen(world);
    }

    /// <summary>The aim point on a followed character: a small crosshair in the anchor colour.</summary>
    public void DrawTargetMarker(EditorView view, Vector3 world)
        => DrawCross(ImGui.GetBackgroundDrawList(), view, world, TargetCross, EditorColours.Anchor, GlyphThickness);

    private static void DrawCross(ImDrawListPtr list, EditorView view, Vector3 centre, float size, uint colour, float thickness)
    {
        DrawEdge(list, view, centre - (Vector3.UnitX * size), centre + (Vector3.UnitX * size), colour, thickness);
        DrawEdge(list, view, centre - (Vector3.UnitY * size), centre + (Vector3.UnitY * size), colour, thickness);
        DrawEdge(list, view, centre - (Vector3.UnitZ * size), centre + (Vector3.UnitZ * size), colour, thickness);
    }
```

Replace the start of `Pose`, down to the evaluator cache, so that under Look At each glyph faces the point, and under recorded aim or Follow Target it shows the recorded aim:

```csharp
    /// <summary>Point <paramref name="index"/>'s aim, roll and FoV: at the Look At point, along the path in Direction-of-travel mode, or recorded.</summary>
    private static (Vector3 Forward, float Roll, float Fov) Pose(Track track, TrackCache cache, int index)
    {
        var point = track.Points[index];
        if (track is { Aim: AimMode.LookAt, LookAtPlaced: true } && TrackAim.Toward(point.Position, track.LookAt) is { } toward)
            return (FreeCamMotion.LookAtFrom(Vector3.Zero, toward.Yaw, toward.Pitch), point.Roll, point.Fov);

        if (track.Aim != AimMode.PathTangent)
            return (FreeCamMotion.LookAtFrom(Vector3.Zero, point.Yaw, point.Pitch), point.Roll, point.Fov);

        // …the evaluator cache and the path-tangent return stay as they are…
    }
```

- [ ] **Step 2: Add them to the editor layer and click the Look At point**

In `EditorLayer.Draw`, in the other-tracks loop after the anchor marker:

```csharp
            if (other is { Aim: AimMode.LookAt, LookAtPlaced: true })
                markers.Add(new TrackMarker(other.Id, -1, overlay.DrawLookAt(view, otherWorld.LookAt, FirstPosition(otherWorld), edited: false, selected: false), MarkerKind.LookAt));
```

After the edited track's anchor marker:

```csharp
        if (editedLocal is { Aim: AimMode.LookAt, LookAtPlaced: true })
            markers.Add(new TrackMarker(edited, -1, overlay.DrawLookAt(view, track.LookAt, FirstPosition(track), edited: true, selected: session.SelectedAnchor == AnchorKind.LookAt), MarkerKind.LookAt));
        if (session.CharacterAim(track) is { } characterAim) overlay.DrawTargetMarker(view, characterAim);
```

`track` is the edited world track, with the point gizmo's preview applied. The anchor gizmo moves the Look At point through a live edit, so `session.Track` already shows it mid-drag. The marker isn't added to `markers`, so it can't be clicked.

In `Apply`'s switch, add before the `_ when` arms:

```csharp
                    MarkerKind.LookAt => session.SelectLookAt(hit.Track),
```

Update the method's doc: "Selects a clicked point, anchor or Look At point, switching to its track first when it isn't the edited one; a click on empty space clears the selection."

- [ ] **Step 3: Move the Look At point with the anchor gizmo**

In `AnchorGizmo.cs`, change the class doc to "The move gizmo and yaw ring on the selected anchor, or the move gizmo alone on the Look At point; a drag previews live and holding Alt moves an anchor alone."

In `Draw`, replace the selection check:

```csharp
        if (session.SelectedAnchor is not { } kind || Shown(session, kind) is not { } anchor)
        {
            Hot = false;
            dragStart = null;
            return;
        }
```

Replace the `rotate` line:

```csharp
        var rotate = kind != AnchorKind.LookAt && (dragStart is not null ? dragRotate : points.Mode == GizmoMode.Rotate);
```

Replace the preview inside `if (usingNow)`, from `var carry = …` to the end of its refusal block:

```csharp
            var refusal = kind == AnchorKind.LookAt
                ? session.PreviewLookAt(edited.Position)
                : session.PreviewAnchor(edited, carry: !PhysicalKeys.IsDown(VirtualKey.MENU));
            if (refusal is not null)
            {
                Plugin.Log.Warning("[editor] anchor drag abandoned: {Refusal}", refusal);
                dragStart = null;
                waitForRelease = true;
                Reset();
            }
```

Add:

```csharp
    /// <summary>The selected anchor in the world, or the Look At point as an anchor with no yaw.</summary>
    private static Anchor? Shown(CameraSession session, AnchorKind kind)
    {
        if (kind != AnchorKind.LookAt) return session.SelectedAnchorInWorld;
        return session.SelectedLookAtInWorld is { } point ? new Anchor(point, 0f) : null;
    }
```

- [ ] **Step 4: The Point window**

In `PointWindow.PreOpenCheck`, add a title arm before the point arm:

```csharp
            (_, AnchorKind.LookAt, _) => "Look At point###vista-point",
```

Change the class doc to "The selected point's, anchor's or Look At point's number fields and gizmo mode; shown only while one is selected in editing mode."

Replace `Draw`:

```csharp
    public override void Draw()
    {
        Anchor? anchor = null;
        Vector3? lookAt = null;
        var index = -1;
        if (session.SelectedAnchor == AnchorKind.LookAt)
        {
            if (session.SelectedLookAtInWorld is not { } point) return;
            lookAt = point;
        }
        else if (session.SelectedAnchor is not null)
        {
            if (session.SelectedAnchorInWorld is not { } a) return;
            anchor = a;
        }
        else if (session.Selected is { } i && i < session.Track.Points.Count) index = i;
        else return;

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f));
        if (!DrawHeader(index >= 0 ? index : null, rotates: lookAt is null)) return;

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4f, 3f));
        if (!ImGui.BeginTable("fields", 4, ImGuiTableFlags.SizingFixedFit)) return;

        if (lookAt is { } shownLookAt) DrawLookAtRows(shownLookAt);
        else if (anchor is { } shownAnchor) DrawAnchorRows(shownAnchor);
        else DrawPointRows(index, session.Track.Points[index]);

        ImGui.EndTable();
        fieldsWidth = ImGui.GetItemRectSize().X;
    }
```

`DrawHeader` gains `bool rotates`. Its doc becomes "Gizmo mode, with Rotate disabled when the selection only moves, then copy, paste and delete, disabled unless a point; false once the point is deleted." Its radio buttons become:

```csharp
        if (ImGui.RadioButton("Move", !rotates || gizmo.Mode == GizmoMode.Move)) gizmo.SetMode(GizmoMode.Move);
        ImGui.SameLine();
        ImGui.BeginDisabled(!rotates);
        if (ImGui.RadioButton("Rotate", rotates && gizmo.Mode == GizmoMode.Rotate)) gizmo.SetMode(GizmoMode.Rotate);
        ImGui.EndDisabled();
```

In `DrawPointRows`, the pitch and yaw fields are disabled under Look At as well:

```csharp
        ImGui.BeginDisabled(session.Track.Aim is AimMode.PathTangent or AimMode.LookAt);
```

Change that method's doc to "The point's position, rotation and FoV rows; pitch and yaw are disabled under Direction of travel and Look At."

Add the Look At rows after `DrawAnchorRows`:

```csharp
    /// <summary>The Look At point's rows: X, Y and Z move it, and rotation and FoV show "—".</summary>
    private void DrawLookAtRows(Vector3 lookAt)
    {
        ImGui.TableNextRow();
        LookAtField("look-x", "X", EditorColours.AxisX, lookAt.X, (p, v) => p with { X = EditLimits.Coordinate(v, p.X) });
        LookAtField("look-y", "Y", EditorColours.AxisY, lookAt.Y, (p, v) => p with { Y = EditLimits.Coordinate(v, p.Y) });
        LookAtField("look-z", "Z", EditorColours.AxisZ, lookAt.Z, (p, v) => p with { Z = EditLimits.Coordinate(v, p.Z) });
        RowIcon(FontAwesomeIcon.ArrowsAlt, "Position");

        ImGui.TableNextRow();
        MissingField("look-pitch", "Pitch", EditorColours.AxisX);
        MissingField("look-yaw", "Yaw", EditorColours.AxisY);
        MissingField("look-roll", "Roll", EditorColours.AxisZ);
        RowIcon(FontAwesomeIcon.SyncAlt, "Rotation");

        ImGui.TableNextRow();
        MissingField("look-fov", "FoV", null);
    }

    /// <summary>A Look At point's field: dragging moves it live, and each drag is one undo step.</summary>
    private void LookAtField(string id, string name, uint border, float value, Func<Vector3, float, Vector3> set)
    {
        var edited = value;
        var changed = BorderedField(id, name, border, ref edited, PositionSpeed, "%.2f");
        if (ImGui.IsItemActivated()) session.BeginLiveEdit();
        if (changed && session.SelectedLookAtInWorld is { } current) _ = session.PreviewLookAt(set(current, edited));
        if (ImGui.IsItemDeactivated()) session.EndLiveEdit();
    }
```

Copy, paste and delete stay disabled for the Look At point, because `DrawHeader` receives no point index.

- [ ] **Step 5: Keys and the point gizmo**

In `EditorKeys.Act`, R toggles the gizmo only for a point or an anchor:

```csharp
            VirtualKey.R when session.Selected is not null || session.SelectedAnchor is AnchorKind.Scene or AnchorKind.Track => Toggle(gizmo),
```

Delete and Backspace already do nothing without a selected point, which covers the Look At point.

In `PointGizmo.Draw`, recorded yaw and pitch stay editable under Follow Target:

```csharp
            : DrawRings(view, Preview?.Point ?? point, session.Track.Aim is AimMode.AimKeys or AimMode.FollowTarget ? AllRings : RollOnly);
```

- [ ] **Step 6: Build and test**

Run: `./build.sh`, then `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: 0 warnings, all pass.

- [ ] **Step 7: Commit**

```bash
git add src/Vista.Plugin/Ui/PointWindow.cs src/Vista.Plugin/Editor
git commit -m "feat(editor) draw, click and move the look at point and mark the followed character"
```

---

### After the tasks

**Backlog.** Remove the "LookAt aim" and "Aim tracking a game entity" sections from `FEATURES.md`, then commit:

```bash
git add FEATURES.md
git commit -m "docs(features) move look at and follow target out of the backlog"
```

**Checklist.** After the final review, the controller writes `CHECKLIST-3e3.md` (untracked), in the format of `CHECKLIST-3e2.md`. It holds one check per line below, each with a pass condition and a Notes line.

1. The aim menu lists "Recorded aim", "Direction of travel", "Look At" and "Follow Target". Its tooltip names the current one.
2. The first Look At puts the crosshair 10 yalms along point 1's aim, with a faint line to point 1. On a track with no points, it goes 10 yalms ahead of the camera. Adding the first point doesn't move it.
3. Switch to another mode and back to Look At: the crosshair is where it was.
4. Play, preview and scrub under Look At: the camera stays on the crosshair all the way. A single-point track pans on the spot.
5. Click the crosshair. The Point window is titled "Look At point". X, Y and Z drag it live. Rotation and FoV show "—" and are greyed, and so are copy, paste, delete and Rotate. Each drag is one Ctrl + Z.
6. With the Look At point selected, the gizmo shows only move arrows, R does nothing, and Delete and Backspace do nothing.
7. Another track under Look At shows its crosshair greyed; under another mode, none. Clicking it edits that track and selects its point.
8. Moving the track anchor carries the crosshair. Alt-moving it leaves the crosshair in place.
9. Under Look At, the recorded Pitch and Yaw fields are greyed, and the point glyphs face the crosshair.
10. Follow Target shows a "Choose a character" button, an aim height field and a smoothing slider. They are hidden under the other modes.
11. The list shows nearby players and NPCs, nearest first, with their distances. Picking one names the button.
12. Play, preview, scrub and Live aim at the character's chest (1.3). Changing the aim height moves the aim up and down, and a small crosshair marks the aim point while editing.
13. Smoothing 0 is locked on. At 1, the aim takes about half a second to catch up. Starting, cutting to the entry, seeking or scrubbing lands on the character without easing.
14. With the character gone (walk away or pick a name then move zones), the button turns red with a warning icon and "(Not found)". Its tooltip reads "Not found nearby: using recorded aim", and the camera uses the recorded aim. When they return, the aim eases back.
15. A playlist entry following a missing character shows the same warning and tooltip, in Edit and in Live.
16. Under Follow Target the recorded Pitch and Yaw fields and the gizmo rings still work.
17. Each of these is one Ctrl + Z: mode, character, aim height, and a smoothing drag.
