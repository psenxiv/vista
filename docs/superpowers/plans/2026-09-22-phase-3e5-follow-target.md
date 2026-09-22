# Follow Target (Phase 3.e.5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the character-aim mode to Watch Target, and add Follow Target: a one-point track whose camera rides with a character at a fixed offset.

**Architecture:** A Follow track stores its one point as an offset in a character frame (`Anchor(characterFeet, characterFacing)`). The scene's world view still carries that point through the track's anchors, so the world point is the "never found" fallback and `world.Anchor.ToLocal(world.Points[0])` recovers the offset exactly. `AimTracker` resolves a Follow camera per frame. `SessionState` converts the points it edits through the character frame for a Follow track, and shows the point at the character.

**Tech Stack:** C# / .NET 10, Dalamud 15.0.3.5, Dalamud.Bindings.ImGui, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-22-follow-target-design.md`

## Global Constraints

- `Vista.Core` never references Dalamud or FFXIVClientStructs and never uses `unsafe`.
- Doc comments are one line; inline comments rare (CLAUDE.md).
- Build with `./build.sh` only (use `--no-incremental` for the final check), never bare `dotnet build`. Test with `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. Both in the foreground.
- Commits: one line, conventional lowercase prefix, no body, no Co-Authored-By trailer. Stage only `src` and `tests`; leave `CHECKLIST-*.md` alone.
- Menu and dialog text is exact: "Watch Target", "Follow Target", "Edit Watch Target", "Edit Follow Target", "Turn with character", "Look at character", "Aim height", "Smoothing", "Done".
- Tooltips are exact: "Watch Target: Name", "Watch Target: choose a character", "Name (Not found): using recorded aim", "Follow Target: Name", "Follow Target: choose a character", "Name (Not found): using the last position", "Follow Target needs a track with one point".
- Refusals are exact: "Follow Target needs a track with one point", "A Follow Target track has one point", "Choose a character to follow", "Character not found".
- Defaults: Turn with character on, Look at character off.

## Technical rulings (cost if wrong in brackets)

- **The offset lives in the stored point.** A Follow track's `Points[0]` is `characterFrame.ToLocal(worldCamera)` where `characterFrame = new Anchor(feet, facing)`. `SceneGeometry.InWorld` treats it like any point, so the world track's point is `worldAnchor.ToWorld(offset)`, which is the "never found" fallback. The tracker recovers the offset with `world.Anchor.ToLocal(world.Points[0])`. [If wrong: a separate offset field.]
- **Anchors never re-express a Follow point.** `MoveTrackAnchor` with carry off keeps points in the world by re-expressing them; for a Follow track it leaves `Points` unchanged. [If wrong: Alt-moving the anchor would change the offset.]
- **`LoadedCharacter.Facing` is a Vista yaw:** the game's `Rotation + π`, so yaw 0 relative to the character looks the way they face. Only its sign matters for turning; the `+ π` makes the Point window's relative yaw readable. [If wrong: the chase camera would turn the wrong way; checked in game.]
- **Turn with character off freezes the facing** at the first found frame after a reset (start, seek, scrub, cut, preview), as the spec says.
- **Lost:** the tracker returns its last Follow frame, or, with none since the last reset, the world point read as a snap point.
- **Display versus playback.** `SessionState.Track` (what the editor draws and edits) shows a Follow track's point at the character where they stand now, unsmoothed, when found. Playback (preview, `FrameAt`, `GoLive`) always uses `WorldOf(Local)`, the anchor-based world view, which the tracker reads as offset plus fallback. `SessionState.Shown(Track local)` gives the display view for any track, for the editor's other tracks.
- **Leaving Follow Target keeps the point where it is shown:** switching from Follow to another mode, with the character found, re-expresses the point back into the track's anchor frame so the camera doesn't jump. This is the mirror of the spec's rule for switching in.
- **One shared set:** Watch and Follow share `TargetName`, `TargetWorld`, `AimHeight` and `Smoothing`.

## Order

All tasks run sequentially on `main`: each builds on the last.

---

### Task 1: Rename Follow Target to Watch Target

**Files:** every file in `src/` and `tests/` that names `AimMode.FollowTarget`, `FollowTargetWindow`, or the user-facing strings.

- [ ] **Step 1: Rename the code**
  - `AimMode.FollowTarget` becomes `AimMode.WatchTarget`.
  - `FollowTargetWindow` (file and class) becomes `WatchTargetWindow`, titled `"Watch Target###vista-watch-target"`.
  - Test names that say Follow meaning this mode say Watch. For example, in `AimTrackerTests` `Following(...)` becomes `Watching(...)`.
  - Grep `src tests` for `FollowTarget`, `Follow Target` and `Following`, and change every one that means the character-aim mode.

- [ ] **Step 2: Rename the text**
  - Aim menu entry: "Watch Target".
  - Pencil tooltip: "Edit Watch Target".
  - Aim icon tooltips: "Watch Target: Name" and "Watch Target: choose a character". "Name (Not found): using recorded aim" is unchanged.

- [ ] **Step 3: Build, test, commit**

Run `./build.sh` and `dotnet test`. Expect 0 warnings, with every test passing and none changed in count.

```bash
git add src tests
git commit -m "refactor(tracks) rename follow target to watch target"
```

---

### Task 2: Follow Target in Core

**Files:**
- Modify: `src/Vista.Core/Tracks/AimMode.cs`, `Track.cs`, `TrackEditing.cs`, `NearbyCharacters.cs` (with `LoadedCharacter`), `IAimTargets.cs`, `AimTracker.cs`, `src/Vista.Core/Scenes/SceneGeometry.cs`, `src/Vista.Core/Session/SessionState.cs`
- Test: `tests/Vista.Tests/Tracks/AimTrackerTests.cs`, `NearbyCharactersTests.cs`, `AimSettingsTests.cs`, `tests/Vista.Tests/Scenes/SceneGeometryTests.cs`, new `tests/Vista.Tests/Session/SessionFollowTests.cs`

**Interfaces (produced):**
- `AimMode.FollowTarget` (added after `WatchTarget`).
- `Track`: `bool FollowTurns = true, bool FollowLooks = false`, appended as the last two positional parameters.
- `TrackEditing.SetFollowTurns(Track, bool)`, `TrackEditing.SetFollowLooks(Track, bool)`. Each returns the same instance when unchanged.
- `LoadedCharacter(string Name, string? World, Vector3 Position, float Facing = 0f)`.
- `IAimTargets.FindCharacter(string name, string? world, Vector3 near) : LoadedCharacter?`. `Find` stays, returning `FindCharacter(...)?.Position`.
- `AimTracker.Character(Track world, IAimTargets? targets) : LoadedCharacter?`, the named character for Watch or Follow.
- `AimTracker.TargetLost` now covers both modes.
- `SessionState`:
  - `Shown(Track local) : Track`;
  - `SetFollowTurns(bool)`, `SetFollowLooks(bool)`;
  - `Track` shows the Follow point at the character;
  - the refusals and conversions per the rulings.

- [ ] **Step 1: Failing model tests** (`AimSettingsTests.cs`; use its existing helpers)

```csharp
    [Fact]
    public void FollowSwitchesDefaultToTurningAndNotLooking()
    {
        var track = TrackEditing.Empty();
        Assert.True(track.FollowTurns);
        Assert.False(track.FollowLooks);
    }

    [Fact]
    public void FollowSwitchesSetAndKeepTheSameInstanceWhenUnchanged()
    {
        var track = TrackEditing.Empty();
        var off = TrackEditing.SetFollowTurns(track, false);
        Assert.False(off.FollowTurns);
        Assert.Same(off, TrackEditing.SetFollowTurns(off, false));
        Assert.True(TrackEditing.SetFollowLooks(track, true).FollowLooks);
    }
```

In `NearbyCharactersTests.cs`:

```csharp
    [Fact]
    public void FindCharacterReturnsTheNearestMatchWithItsFacing()
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", null, new Vector3(10f, 0f, 0f), 1f), new LoadedCharacter("Guard", null, new Vector3(-2f, 0f, 0f), 2f)]);

        var found = characters.FindCharacter("Guard", null, Vector3.Zero);

        Assert.Equal(new Vector3(-2f, 0f, 0f), found!.Value.Position);
        Assert.Equal(2f, found.Value.Facing);
    }
```

- [ ] **Step 2: Run them to see them fail** (they don't compile).

- [ ] **Step 3: The model**

- In `AimMode.cs`: `public enum AimMode { PathTangent, AimKeys, LookAt, WatchTarget, FollowTarget }`, with the doc comment unchanged.
- In `Track.cs`: append `bool FollowTurns = true, bool FollowLooks = false`, and extend the doc comment to "…the character it watches or follows and how it follows".
- In `TrackEditing.cs`, next to `SetSmoothing`:

```csharp
    /// <summary>Sets whether a Follow Target offset turns as the character turns.</summary>
    public static Track SetFollowTurns(Track track, bool turns) => track.FollowTurns == turns ? track : track with { FollowTurns = turns };

    /// <summary>Sets whether a Follow Target camera looks at the character rather than keeping its recorded aim.</summary>
    public static Track SetFollowLooks(Track track, bool looks) => track.FollowLooks == looks ? track : track with { FollowLooks = looks };
```

- In `NearbyCharacters.cs`:
  - `LoadedCharacter` gains `float Facing = 0f`, and its doc comment adds "and its facing as a camera yaw".
  - Rename the loop in `Find` into `FindCharacter`, returning the whole `LoadedCharacter?`.
  - `Find` becomes `=> FindCharacter(name, world, near)?.Position;`.
- In `IAimTargets.cs`: add `LoadedCharacter? FindCharacter(string name, string? world, Vector3 near);` with a one-line doc comment. `NearbyCharacters` is the only implementation; update any test doubles that implement the interface.

- [ ] **Step 4: Run the model tests** (PASS).

- [ ] **Step 5: Failing tracker tests** (`AimTrackerTests.cs`)

```csharp
    // A Follow track whose one point is the offset (0, 2, 5) behind and above, looking back along −z (yaw 0).
    private static Track FollowingAt(ControlPoint offset, bool turns = true, bool looks = false, float smoothing = 0f)
        => TrackEditing.Append(TrackEditing.Empty(AimMode.FollowTarget), offset) with { TargetName = "Guard", FollowTurns = turns, FollowLooks = looks, Smoothing = smoothing };

    private static readonly ControlPoint Behind = new(new Vector3(0f, 2f, 5f), 0f, 0f, 1f);

    private static NearbyCharacters GuardStanding(Vector3 feet, float facing)
    {
        var characters = new NearbyCharacters();
        characters.Update([new LoadedCharacter("Guard", null, feet, facing)]);
        return characters;
    }

    [Fact]
    public void FollowPlacesTheCameraAtTheOffsetFromTheCharacter()
    {
        var frame = Frame(new AimTracker(GuardStanding(new Vector3(10f, 0f, 0f), 0f)), FollowingAt(Behind));

        Assert.Equal(new Vector3(10f, 2f, 5f), frame.Position);
        Assert.Equal(FreeCamMotion.LookAtFrom(frame.Position, 0f, 0f), frame.LookAt);
    }

    [Fact]
    public void FollowTurnsTheOffsetAndTheAimWithTheCharacter()
    {
        var quarter = MathF.PI / 2f;
        var frame = Frame(new AimTracker(GuardStanding(Vector3.Zero, quarter)), FollowingAt(Behind));

        var expected = new Anchor(Vector3.Zero, quarter).ToWorld(Behind);
        Assert.Equal(expected.Position.X, frame.Position.X, 4);
        Assert.Equal(expected.Position.Z, frame.Position.Z, 4);
        AimsAt(expected.Position + (FreeCamMotion.LookAtFrom(Vector3.Zero, expected.Yaw, 0f) - Vector3.Zero), frame);
    }

    [Fact]
    public void WithTurningOffTheFacingIsHeldFromTheFirstFrame()
    {
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, turns: false);
        Frame(tracker, track);

        characters.Update([new LoadedCharacter("Guard", null, Vector3.Zero, MathF.PI / 2f)]);
        var frame = Frame(tracker, track);

        Assert.Equal(new Vector3(0f, 2f, 5f), frame.Position);
    }

    [Fact]
    public void LookAtCharacterAimsAtTheirAimHeight()
    {
        var frame = Frame(new AimTracker(GuardStanding(Vector3.Zero, 0f)), FollowingAt(Behind, looks: true) with { AimHeight = 1.3f });

        AimsAt(new Vector3(0f, 1.3f, 0f), frame);
    }

    [Fact]
    public void ALostCharacterHoldsTheLastFrameAndNeverFoundUsesTheAnchorFallback()
    {
        var characters = GuardStanding(new Vector3(10f, 0f, 0f), 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind);
        var seen = Frame(tracker, track);

        characters.Update([]);
        Assert.Equal(seen, Frame(tracker, track));

        var fresh = Frame(new AimTracker(characters), track);
        Assert.Equal(Behind.Position, fresh.Position);
    }

    [Fact]
    public void FollowPositionEasesAndAResetLandsOnTheCharacter()
    {
        var characters = GuardStanding(Vector3.Zero, 0f);
        var tracker = new AimTracker(characters);
        var track = FollowingAt(Behind, smoothing: 1f);
        Frame(tracker, track, 0.5f);

        characters.Update([new LoadedCharacter("Guard", null, new Vector3(10f, 0f, 0f), 0f)]);
        var eased = Frame(tracker, track, 0.5f);
        Assert.Equal(10f * (1f - MathF.Exp(-1f)), eased.Position.X, 3);

        tracker.Reset();
        Assert.Equal(10f, Frame(tracker, track, 0.5f).Position.X, 4);
    }
```

Traces:
- `Frame(...)` builds `TrackEvaluator(track)` from the raw track. The test tracks have no anchor, so `world.Anchor` is `Anchor.Origin` and the offset recovered is the point itself.
- The turn test uses `Anchor.ToWorld`, the same maths as the implementation. The aim check compares directions, which is enough.
- In the easing test, the first frame lands at x = 0. The second eases by `1 − e^(−0.5 / 0.5)` towards 10.

- [ ] **Step 6: Run them to see them fail.**

- [ ] **Step 7: The tracker**

In `AimTracker.cs`, add the fields and branch to `Frame` first:

```csharp
    private readonly AimSmoother positionSmoother = new();
    private float? heldFacing;
    private CameraState? lastFollow;
```

```csharp
    public CameraState? Frame(TrackEvaluator evaluator, Track world, double time, float dt)
    {
        if (world.Aim == AimMode.FollowTarget && world.Points.Count == 1) return Follow(world, dt);
        // …as now…
    }

    /// <summary>A Follow Target frame: the offset from the character, turned with them or held, looking at them or along its recorded aim.</summary>
    private CameraState Follow(Track world, float dt)
    {
        var offset = world.Anchor.ToLocal(world.Points[0]);
        if (Character(world, targets) is not { } character)
            return lastFollow ?? new CameraState(world.Points[0].Position, FreeCamMotion.LookAtFrom(world.Points[0].Position, world.Points[0].Yaw, world.Points[0].Pitch), offset.Fov, offset.Roll);

        var facing = world.FollowTurns ? character.Facing : heldFacing ??= character.Facing;
        var at = new Anchor(character.Position, facing).ToWorld(offset);
        var position = positionSmoother.Step(at.Position, dt, world.Smoothing);
        var look = FreeCamMotion.LookAtFrom(position, at.Yaw, at.Pitch);
        if (world.FollowLooks && TrackAim.Toward(position, smoother.Step(character.Position + (Vector3.UnitY * world.AimHeight), dt, world.Smoothing)) is { } aim)
            look = FreeCamMotion.LookAtFrom(position, aim.Yaw, aim.Pitch);

        lastFollow = new CameraState(position, look, offset.Fov, offset.Roll);
        return lastFollow.Value;
    }
```

Read `TrackAim.Toward`'s real return type. If it returns the look point rather than angles, adapt: the aim is from `position` toward the eased aim point. Keep the recorded look when it's null, meaning the aim point is on the camera.

`Reset()` also clears the new state: `positionSmoother.Reset(); heldFacing = null; lastFollow = null;`.

Add `Character` and widen the lookups:

```csharp
    /// <summary>The character a Watch or Follow track in the world names, found nearest its anchor; null unless named and found.</summary>
    public static LoadedCharacter? Character(Track world, IAimTargets? targets)
        => world is { Aim: AimMode.WatchTarget or AimMode.FollowTarget, TargetName: { } name } ? targets?.FindCharacter(name, world.TargetWorld, world.Anchor.Position) : null;
```

- `CharacterAim` stays Watch-only: it is the Watch marker and the Watch aim target. Build it from `Character(...)` when the aim is `WatchTarget`.
- `TargetLost` becomes `world is { Aim: AimMode.WatchTarget or AimMode.FollowTarget, TargetName: not null } && Character(world, targets) is null`.

- [ ] **Step 8: Run the tracker tests** (PASS).

- [ ] **Step 9: Anchors leave a Follow point alone** (`SceneGeometryTests.cs`, using its helpers)

```csharp
    [Fact]
    public void MovingAFollowTracksAnchorAloneLeavesItsOffset()
    {
        // A scene whose track is under Follow Target with one point at (0, 2, 5), anchors placed.
        var scene = /* the file's usual placed scene with its track set to AimMode.FollowTarget and Points = [new ControlPoint(new Vector3(0f, 2f, 5f), 0f, 0f, 1f)] */;
        var track = scene.Tracks[0];

        var moved = SceneGeometry.MoveTrackAnchor(scene, track.Id, new Anchor(new Vector3(30f, 0f, 30f), 1f), carry: false);

        Assert.Equal(track.Points, moved.Tracks[0].Points);
    }
```

In `MoveTrackAnchor`, guard the re-expression: `if (!carry && track.Aim != AimMode.FollowTarget)`. Keep `KeepLookAt` for other tracks as now. Add one line to the doc comment: "A Follow Target track's point is an offset from its character and stays as it is."

- [ ] **Step 10: Failing session tests** (new `tests/Vista.Tests/Session/SessionFollowTests.cs`)

Build the session with a `NearbyCharacters` passed as `aimTargets`, following how `SessionAimTests` constructs one, and copy its `Editing()`-style helper. Tests:

```csharp
    [Fact]
    public void FollowTargetIsRefusedForATrackWithMoreThanOnePoint()
    {
        var (state, _) = EditingWith(points: 2);
        Assert.Equal("Follow Target needs a track with one point", state.SetAim(AimMode.FollowTarget, Camera));
    }

    [Fact]
    public void ASecondPointIsRefusedUnderFollowTarget()
    {
        var (state, _) = FollowingGuard();
        Assert.Equal("A Follow Target track has one point", state.AddToEnd(Camera));
    }

    [Fact]
    public void CapturingNeedsAFoundCharacter()
    {
        var (state, characters) = EditingWith(points: 0);
        state.SetAim(AimMode.FollowTarget, Camera);
        Assert.Equal("Choose a character to follow", state.AddToEnd(Camera));

        state.SetTarget("Guard", null);
        characters.Update([]);
        Assert.Equal("Character not found", state.AddToEnd(Camera));
    }

    [Fact]
    public void CapturingStoresTheOffsetAndTheTrackShowsItAtTheCharacter()
    {
        var (state, characters) = EditingWith(points: 0);
        state.SetAim(AimMode.FollowTarget, Camera);
        state.SetTarget("Guard", null);                       // guard at (10, 0, 0), facing 0
        state.AddToEnd(new ControlPoint(new Vector3(10f, 2f, 5f), 0f, 0f, 1f));

        characters.Update([new LoadedCharacter("Guard", null, new Vector3(20f, 0f, 0f), 0f)]);

        Assert.Equal(new Vector3(20f, 2f, 5f), state.Track.Points[0].Position);
    }

    [Fact]
    public void SwitchingToFollowKeepsTheCameraWhereItIs()
    {
        var (state, _) = EditingWith(points: 1);             // one point somewhere in the world
        var before = state.Track.Points[0];
        state.SetTarget("Guard", null);

        state.SetAim(AimMode.FollowTarget, Camera);

        AssertNear(before.Position, state.Track.Points[0].Position);
    }

    [Fact]
    public void ChoosingANewCharacterKeepsTheCameraWhereItIs() { /* follow Guard, then SetTarget to a second character elsewhere: the shown point doesn't move */ }

    [Fact]
    public void LeavingFollowKeepsTheCameraWhereItIs() { /* follow, then SetAim(AimKeys): the shown point doesn't move */ }

    [Fact]
    public void EditingTheFollowPointEditsItWhereItIsShown()
    {
        var (state, _) = FollowingGuard();
        var target = new ControlPoint(new Vector3(12f, 3f, 4f), 0.2f, 0f, 1f);

        state.ReplacePoint(0, target);

        AssertNear(target.Position, state.Track.Points[0].Position);
    }

    [Fact]
    public void TheFollowSwitchesAreUndoStepsAndRefusedUnlessEditing() { /* SetFollowTurns(false) then Undo restores true; after Release both are refused */ }

    [Fact]
    public void PlaybackUsesTheOffsetNotTheShownPoint()
    {
        var (state, characters) = FollowingGuard();          // captured at guard (10, 0, 0)
        characters.Update([new LoadedCharacter("Guard", null, new Vector3(40f, 0f, 0f), 0f)]);

        var frame = state.FrameAt(0.0)!.Value;

        Assert.Equal(40f, frame.Position.X, 3);                // the offset's x is 0, carried to the guard's new place
    }
```

Fill in the three bodies marked `/* … */` with real code before running; they state the behaviour exactly. `EditingWith(points: n)` puts the session in Edit with n recorded points, and a `NearbyCharacters` holding "Guard" at (10, 0, 0) facing 0. `FollowingGuard()` is `EditingWith(points: 0)`, then Follow Target, then target Guard, then capture at (10, 2, 5). `AssertNear` checks each component to 3 places.

- [ ] **Step 11: Run them to see them fail.**

- [ ] **Step 12: The session**

In `SessionState.cs`:

- **Character frame.**

```csharp
    /// <summary>The frame a Follow track's point is stored in: its character where they stand, or null when not Follow or not found.</summary>
    private Anchor? FollowFrame(Track local)
        => local.Aim == AimMode.FollowTarget && AimTracker.Character(WorldOf(local), aimTargets) is { } c ? new Anchor(c.Position, c.Facing) : null;
```

- **Display.**

```csharp
    /// <summary>A scene track as the editor shows it: a Follow track's point at its character where they stand now.</summary>
    public Track Shown(Track local)
    {
        var world = WorldOf(local);
        return FollowFrame(local) is { } frame && local.Points.Count == 1 ? world with { Points = [frame.ToWorld(local.Points[0])] } : world;
    }
```

  - `Track` becomes `=> Shown(Local);`, with the doc comment "The edited track in the world as the editor shows it; Edit builds it."
  - `StartPreview` and `FrameAt` build their playback from `WorldOf(Local)`, not `Track`.
  - `Evaluator` must stay built from the anchor-based world view, so it is unchanged by a moving character. Check how it's built and keep it on `WorldOf(Local)`.
- **Point conversion.** `ToLocal(ControlPoint world)` becomes `FollowFrame(Local) is { } frame ? frame.ToLocal(world) : SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world)`.
- **`WithPoint`** starts with the Follow checks:

```csharp
        var local = SceneEditing.Get(scene, EditedTrackId);
        if (local.Aim == AimMode.FollowTarget)
        {
            if (local.Points.Count >= 1) throw new ArgumentException("A Follow Target track has one point");
            if (local.TargetName is null) throw new ArgumentException("Choose a character to follow");
            if (FollowFrame(local) is null) throw new ArgumentException("Character not found");
        }
```

  Check how `ApplyScene` turns an `ArgumentException` into a returned refusal. If it doesn't, check before calling `ApplyScene` in `AddToEnd` and `AddAfterSelected` and return the message instead. After placing the anchors, store `FollowFrame(track)!.Value.ToLocal(world)` rather than the anchor conversion.
- **`SetAim`.**
  - Refuse `AimMode.FollowTarget` when `Local.Points.Count > 1` with the exact message.
  - Wrap the change so the point stays where it is shown, all as one undo step through `ApplySetting`. Take `var shown = Track.Points.Count == 1 ? Track.Points[0] : (ControlPoint?)null;` before the change, then apply the aim.
  - If the result is Follow and its frame is found, set `Points[0] = frame.ToLocal(shown)`.
  - If the old aim was Follow and the new isn't, set `Points[0] = worldAnchor.ToLocal(shown)`.
  - Use the frame computed for the result track: pass the result into `FollowFrame`.
- **`SetTarget`.** When the track is Follow with one point, re-express the same way into the new character's frame, if found.
- **`SetFollowTurns` and `SetFollowLooks`** go through `ApplySetting`, next to `SetSmoothing`.
- **`PreviewPoint` and `ReplacePoint`** already go through `ToLocal`, so nothing more is needed.

- [ ] **Step 13: Run everything** (PASS), then build.

- [ ] **Step 14: Commit**

```bash
git add src tests
git commit -m "feat(tracks) follow a character at a fixed offset"
```

---

### Task 3: Follow Target in the Plugin

**Files:**
- Modify: `src/Vista.Plugin/Game/CharacterTable.cs`, `src/Vista.Plugin/Ui/WatchTargetWindow.cs`, `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, `src/Vista.Plugin/Session/CameraSession.cs`, `src/Vista.Plugin/Editor/EditorLayer.cs`, `src/Vista.Plugin/Plugin.cs`
- Create: `src/Vista.Plugin/Ui/CharacterPicker.cs`, `src/Vista.Plugin/Ui/FollowTargetWindow.cs`

- [ ] **Step 1: Facing.** In `CharacterTable`, fill `Facing` with `obj.Rotation + MathF.PI`.

- [ ] **Step 2: A shared picker.** Move `WatchTargetWindow`'s character drop-down (the combo, search, deduplication and `###` IDs) into `CharacterPicker.Draw(CameraSession session, ref string search, float width)`.
  - It needs a small `CharacterPicker` class holding the search text, or a static method with the state passed in; follow the codebase's style.
  - `WatchTargetWindow` then uses it.

- [ ] **Step 3: `FollowTargetWindow`.** A copy of `WatchTargetWindow`'s shape, titled `"Follow Target###vista-follow-target"`.
  - It shows the picker, then two checkboxes, then Aim height, Smoothing and Done:
    - **"Turn with character"** → `session.SetFollowTurns`;
    - **"Look at character"** → `session.SetFollowLooks`.
  - It closes itself when the edited track isn't under `AimMode.FollowTarget`, when the edited track changes, or when the mode leaves Edit.
  - Register it in `Plugin.cs` next to the Watch window.
  - Add `SetFollowTurns`, `SetFollowLooks` and `Shown` passthroughs to `CameraSession`.

- [ ] **Step 4: The aim menu.**
  - The Follow Target entry has a pencil ("Edit Follow Target") built the same way as Watch Target's. Each pencil shows only on the entry for the track's current mode; Watch Target's pencil must change to follow this rule too.
  - With more than one point, the entry and its pencil are disabled, with the tooltip "Follow Target needs a track with one point", shown while disabled.
  - Switching a track into Follow Target or Watch Target always opens that mode's dialog; choosing the mode the track is already on doesn't. Change Watch Target's current "opens when no character is chosen" rule to this too.
  - Under Follow Target the aim icon is drawn in `UiColours.Accent` when found and `UiColours.Red` otherwise. Its tooltips are "Follow Target: Name", "Name (Not found): using the last position", and "Follow Target: choose a character".
  - Choosing Follow Target closes the Watch dialog, and choosing Watch Target closes the Follow dialog. Each window's own close check already covers this.

- [ ] **Step 5: The overlay.** In `EditorLayer`:
  - Draw other tracks from `session.Shown(local)` instead of `session.WorldOf(local)`.
  - For a track under Follow Target, skip its track anchor ring, name and link, and its anchor marker for clicks.
  - The edited track already comes from `session.Track`, which shows it.
  - The Watch character marker stays Watch-only.

- [ ] **Step 6: The playlist.** `PlaylistPanel`'s warning uses `TargetLost`, which now covers Follow. Keep the tooltip "Not found nearby: using recorded aim" for Watch and use "Not found nearby: using the last position" for Follow.

- [ ] **Step 7: Build and test.** `./build.sh --no-incremental` gives 0 warnings, and every test passes.

- [ ] **Step 8: Commit**

```bash
git add src
git commit -m "feat(ui) follow target in the aim menu with its own dialog"
```

---

### After the tasks

The controller adds Follow Target checks to `CHECKLIST-3e3.md` and renames Follow Target to Watch Target in that checklist's existing checks.
