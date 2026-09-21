# Housekeeping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clean up before phase 2c: remove the GPose probe and phase-1 debug commands, move the Off/Editing/Live state machine into Core with tests, and fix every deferred minor from the phase 2 final review.

**Architecture:** Core gains `CinematicCam.Core.Session.SessionState`, which owns the mode, the Director, the track and the "only while editing" rule. Each action returns what happened. The plugin's `CameraSession` keeps only the game-side work: free-cam, movement lock, camera ownership, snapshot and game UI. The minors are small, test-first fixes in Core, plus two plugin fixes.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5 (`Dalamud.NET.Sdk/15.0.0`), FFXIVClientStructs.

**Spec:** `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`. Every decision in this plan was made by the user on 2026-09-21. The spec lines it relies on: nothing can be edited while live, paused included (spec:316); automatic release (spec:347-350); timing position must never decrease (spec:267).

## Global Constraints

- `CinematicCam.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`.
- `tests/CinematicCam.Tests` references Core only.
- Namespaces match folders. **Never create a `CinematicCam.Plugin.Camera` namespace**, because it shadows FFXIVClientStructs' `Camera`.
- Build the plugin with `./build.sh`, never bare `dotnet build`. Tests: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`.
- Never write `Camera.Distance` or `InterpDistance`. This plan writes no new game field.
- Doc comments are one line, stating what a thing is or does. Inline comments are rare.
- Commits: one line, conventional prefix, lowercase, no trailing period, **no body, no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Work and commit on `main`. Do not push.
- Do not add features, options or behaviour this plan does not name. If something seems to need a decision, stop and report it; do not settle it yourself.

---

### Task 1: Bounds-check segment indices and drop `ArcLengthTable.Locate`

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/ArcLengthTable.cs`
- Modify: `src/CinematicCam.Core/Tracks/TrackAim.cs` (`Channel`)
- Test: `tests/CinematicCam.Tests/Tracks/ArcLengthTableTests.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackAimTests.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TimingCurveTests.cs` (one call site)

**Interfaces:**
- Produces: `ArcLengthTable.SegmentLength(int)` and `ParameterAt(int, float)` throw `ArgumentOutOfRangeException` for a segment outside `0..SegmentCount-1`. `TrackAim.Channel` throws `ArgumentOutOfRangeException` for a segment outside `0..values.Count-2`. `ArcLengthTable.Locate` no longer exists.

- [ ] **Step 1: Write the failing tests**

Add to `ArcLengthTableTests` (it already has a `BunchedThenSpread` point set):

```csharp
    [Fact]
    public void SegmentQueriesRejectAnOutOfRangeSegment()
    {
        var table = new ArcLengthTable(BunchedThenSpread);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SegmentLength(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SegmentLength(table.SegmentCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ParameterAt(-1, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ParameterAt(table.SegmentCount, 0.5f));
    }
```

Add to `TrackAimTests`:

```csharp
    [Fact]
    public void ChannelRejectsAnOutOfRangeSegment()
    {
        var values = new[] { 1f, 2f, 3f };
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackAim.Channel(values, -1, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackAim.Channel(values, 2, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackAim.Channel(new[] { 1f }, 0, 0.5f));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "FullyQualifiedName~SegmentQueriesRejectAnOutOfRangeSegment|FullyQualifiedName~ChannelRejectsAnOutOfRangeSegment"`
Expected: both FAIL. The table throws `IndexOutOfRangeException`; `Channel` clamps and returns a value.

- [ ] **Step 3: Implement the checks**

In `ArcLengthTable`, replace `SegmentLength` and the first line of `ParameterAt`, and add a helper:

```csharp
    /// <summary>Arc length of one segment.</summary>
    public float SegmentLength(int segment) => _cumulative[CheckSegment(segment)][^1];

    /// <summary>Spline parameter t in [0, 1] whose arc distance from the segment start is fraction x SegmentLength.</summary>
    public float ParameterAt(int segment, float fraction)
    {
        var samples = _cumulative[CheckSegment(segment)];
        // ...rest unchanged
    }

    private int CheckSegment(int segment)
    {
        if (segment < 0 || segment >= SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(segment),
                SegmentCount == 0 ? "the table has no segments" : $"segment must be 0..{SegmentCount - 1}");
        return segment;
    }
```

In `TrackAim.Channel`, add the check as the first statement:

```csharp
        if (segment < 0 || segment > values.Count - 2)
            throw new ArgumentOutOfRangeException(nameof(segment),
                values.Count < 2 ? "a channel needs at least two values" : $"segment must be 0..{values.Count - 2}");
```

- [ ] **Step 4: Remove `Locate`**

Delete `ArcLengthTable.Locate` and its doc comment. Delete the three `Locate…` tests in `ArcLengthTableTests`: `LocateClampsBelowZeroToTheStart`, `LocateClampsAboveSegmentCountToTheEnd` and `LocateAtExactlySegmentCountReturnsLastSegmentAtFractionOne`. In `TimingCurveTests.StraightCurveGivesConstantWorldSpeedWithinASegmentOnUnevenlySpacedPoints`, replace

```csharp
            var (segment, fraction) = table.Locate(position);
```

with

```csharp
            var segment = (int)MathF.Floor(position);
            var fraction = position - segment;
```

The loop keeps `position` within [1, 2] and the table has 3 segments, so this matches what `Locate` returned.

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/CinematicCam.Core/Tracks/ArcLengthTable.cs src/CinematicCam.Core/Tracks/TrackAim.cs tests/CinematicCam.Tests/Tracks
git commit -m "fix(core) bounds-check segment indices and drop locate"
```

---

### Task 2: Reject unknown tangent modes and keep timing lookups in `double`

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TimingCurve.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TimingCurveTests.cs`

**Interfaces:**
- Produces: `new TimingCurve(keys)` throws `ArgumentException("timing key {i} has an unknown tangent mode")` for a `Mode` outside the `TangentMode` enum. `TrackEvaluator` and `CameraSession.ChangeTrack` build a curve, so such a track is refused at edit time.

- [ ] **Step 1: Write the failing tests**

Add to `TimingCurveTests` (its `Key(time, position, mode = Auto, …)` helper already exists):

```csharp
    [Fact]
    public void ConstructorRejectsAnUnknownTangentMode()
    {
        var keys = new[] { Key(0f, 0f, (TangentMode)99), Key(1f, 1f) };
        var ex = Assert.Throws<ArgumentException>(() => new TimingCurve(keys));
        Assert.Equal("timing key 0 has an unknown tangent mode", ex.Message);
    }

    [Fact]
    public void PositionAtKeepsFullPrecisionOnLongShots()
    {
        // Keys 0.0625 s apart, one float step at a million seconds. A time cast to float
        // rounds 1_000_000.04 up to the next key and evaluates the wrong interval.
        var keys = new[]
        {
            Key(0f, 0f),
            Key(1_000_000f, 1f),
            Key(1_000_000.0625f, 1f),
            Key(1_000_000.125f, 2f),
        };
        var curve = new TimingCurve(keys);

        Assert.Equal(1f, curve.PositionAt(1_000_000.04), 4);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "FullyQualifiedName~ConstructorRejectsAnUnknownTangentMode|FullyQualifiedName~PositionAtKeepsFullPrecisionOnLongShots"`
Expected: both FAIL. The first because no exception is thrown; the second because the result is about 1.31, not 1.

- [ ] **Step 3: Implement**

In `Validate`, check every key's mode before the pairwise checks:

```csharp
    private static void Validate(IReadOnlyList<TimingKey> keys)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (!Enum.IsDefined(keys[i].Mode))
                throw new ArgumentException($"timing key {i} has an unknown tangent mode");
            if (i == 0) continue;
            if (keys[i].Time <= keys[i - 1].Time)
                throw new ArgumentException("timing keys must have strictly increasing times");
            if (keys[i].Position < keys[i - 1].Position)
                throw new ArgumentException("timing key positions must not decrease");
        }
    }
```

In `BuildTangents`, replace `default: // Auto` with `case TangentMode.Auto:`, and add after that case's `break;`:

```csharp
                default:
                    throw new ArgumentOutOfRangeException(nameof(keys), $"unknown tangent mode {key.Mode}");
```

In `PositionAt`, delete `var t = time;`, use `time` wherever `t` was used, and call `FindInterval(time)`. Change `FindInterval` to take `double`:

```csharp
    private int FindInterval(double t)
```

Its body is unchanged; `_keys[mid].Time <= t` now compares as `double`.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TimingCurve.cs tests/CinematicCam.Tests/Tracks/TimingCurveTests.cs
git commit -m "fix(core) reject unknown tangent modes and keep timing in double"
```

---

### Task 3: Refuse malformed tracks cleanly in `TrackEditing`

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackEditing.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs`

**Interfaces:**
- Produces: when a point has no timing key, `LegSeconds`/`SetLeg`/`HoldSeconds`/`SetHold` throw `ArgumentException("point {n} has no timing key")`. This must be exactly `ArgumentException`, because `ChangeTrack` catches `ArgumentException` and returns its message. A leg index on a track with fewer than 2 points throws `ArgumentOutOfRangeException("this track has no legs")`. A point index on an empty track throws `ArgumentOutOfRangeException("this track has no points")`. Existing messages for other cases are unchanged, e.g. `"hold index must be 0..2 for a 3-point track"`.

Keys may sit between points (spec: position 2.5), so a track is *not* required to have a key at every point. These operations refuse a track they cannot handle; nothing checks this when a track is built.

- [ ] **Step 1: Write the failing tests**

Add to `TrackEditingTests` (its `Point(x, y, z)` helper already exists):

```csharp
    [Fact]
    public void LegOnATrackWithFewerThanTwoPointsSaysItHasNoLegs()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(0f, 0f, 0f));
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.LegSeconds(track, 1));
        Assert.Equal("this track has no legs", ex.Message);
    }

    [Fact]
    public void HoldOnAnEmptyTrackSaysItHasNoPoints()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TrackEditing.HoldSeconds(TrackEditing.Empty(), 0));
        Assert.Equal("this track has no points", ex.Message);
    }

    [Fact]
    public void APointWithNoTimingKeyIsRefusedAsAnArgumentError()
    {
        var points = new[] { Point(0f, 0f, 0f), Point(10f, 0f, 0f) };
        var timing = new[] { new TimingKey(0f, 0f, TangentMode.Auto, 0f, 0f) };
        var track = new Track(points, timing, AimMode.AimKeys, PlaybackMode.Once);

        var leg = Assert.Throws<ArgumentException>(() => TrackEditing.LegSeconds(track, 1));
        Assert.Equal("point 1 has no timing key", leg.Message);
        var hold = Assert.Throws<ArgumentException>(() => TrackEditing.SetHold(track, 1, 2f));
        Assert.Equal("point 1 has no timing key", hold.Message);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "FullyQualifiedName~TrackEditingTests"`
Expected: the three new tests FAIL (messages read "1..0"/"0..-1", and the malformed track throws `InvalidOperationException`).

- [ ] **Step 3: Implement**

Replace the two validators:

```csharp
    private static void ValidateLegIndex(Track track, int index)
    {
        var n = track.Points.Count;
        if (n < 2)
            throw new ArgumentOutOfRangeException(null, "this track has no legs");
        if (index < 1 || index > n - 1)
            throw new ArgumentOutOfRangeException(null, $"leg index must be 1..{n - 1} for a {n}-point track");
    }

    private static void ValidatePointIndex(Track track, int index, string what)
    {
        var n = track.Points.Count;
        if (n == 0)
            throw new ArgumentOutOfRangeException(null, "this track has no points");
        if (index < 0 || index > n - 1)
            throw new ArgumentOutOfRangeException(null, $"{what} index must be 0..{n - 1} for a {n}-point track");
    }
```

In `FirstKeyIndex` and `LastKeyIndex`, replace each `throw new InvalidOperationException($"no timing key found for point {point}");` with:

```csharp
        throw new ArgumentException($"point {point} has no timing key");
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TrackEditing.cs tests/CinematicCam.Tests/Tracks/TrackEditingTests.cs
git commit -m "fix(core) refuse malformed tracks cleanly in track editing"
```

---

### Task 4: Ignore negative frame time in playback

**Files:**
- Modify: `src/CinematicCam.Core/Tracks/TrackPlayback.cs`
- Test: `tests/CinematicCam.Tests/Tracks/TrackPlaybackTests.cs`

**Interfaces:**
- Produces: `TrackPlayback.Advance(dt)` treats `dt < 0` as 0. Elapsed time never decreases except through `Loop` wrapping or `Restart`.

- [ ] **Step 1: Write the failing test**

Add to `TrackPlaybackTests` (its `StraightTrack(mode)` helper is 10 s long):

```csharp
    [Theory]
    [InlineData(PlaybackMode.Once)]
    [InlineData(PlaybackMode.Loop)]
    public void NegativeFrameTimeDoesNotRunTimeBackwards(PlaybackMode mode)
    {
        var fresh = new TrackPlayback(StraightTrack(mode));
        fresh.Advance(-1f);
        Assert.Equal(0.0, fresh.Elapsed);

        var playing = new TrackPlayback(StraightTrack(mode));
        playing.Advance(2f);
        playing.Advance(-1f);
        Assert.Equal(2.0, playing.Elapsed, 5);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj --filter "FullyQualifiedName~NegativeFrameTimeDoesNotRunTimeBackwards"`
Expected: FAIL (Loop gives -1; Once gives 1 for the second case).

- [ ] **Step 3: Implement**

In `Advance`, replace `var next = Elapsed + dt;` with:

```csharp
        var next = Elapsed + Math.Max(dt, 0f);
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Tracks/TrackPlayback.cs tests/CinematicCam.Tests/Tracks/TrackPlaybackTests.cs
git commit -m "fix(core) ignore negative frame time in playback"
```

---

### Task 5: The mode state machine in Core

**Files:**
- Create: `src/CinematicCam.Core/Session/CameraMode.cs`
- Create: `src/CinematicCam.Core/Session/SessionState.cs`
- Test: `tests/CinematicCam.Tests/Session/SessionStateTests.cs`

This task only adds Core types; the plugin keeps using its own `CameraMode` until Task 6.

**Interfaces:**
- Consumes: `Director`, `TrackShot`, `Track`, `TrackEditing`, `TrackEvaluator` from `CinematicCam.Core.Tracks`.
- Produces (namespace `CinematicCam.Core.Session`):

```csharp
public enum CameraMode { Off, Editing, Live }
public enum EditOutcome { Unchanged, FromOff, FromLive }
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff }

public sealed class SessionState
{
    public CameraMode Mode { get; }
    public Director Director { get; }
    public Track Track { get; }
    public bool LocksInput { get; }
    public EditOutcome Edit();
    public PlayOutcome Play();
    public PlayOutcome Restart();   // Refused, Started or StartedFromOff only
    public bool Stop();             // true when it paused
    public bool Release();          // false when already off
    public string? ChangeTrack(Func<Track, Track> change);
}
```

The behaviour copies today's `CameraSession` (`src/CinematicCam.Plugin/Session/CameraSession.cs`) exactly:
- `Edit`: Editing → `Unchanged`. Off → Editing, `FromOff`. Live → director offline, Editing, `FromLive`.
- `Play`: live, not paused, not finished → `ReHid` (nothing changes). Live, paused, not finished → director resumes, `Resumed`. Anything else → `Restart()`.
- `Restart`: no points → `Refused`, nothing changes. Otherwise the director goes live with the track from zero, Mode → Live, and it returns `StartedFromOff` if the mode was Off, else `Started`.
- `Stop`: Live only. Pauses the director and returns true.
- `Release`: Off → false. Otherwise the director goes offline, Mode → Off, returns true.
- `ChangeTrack`: not Editing → `"The track can only change while editing."`. Otherwise it applies the change and builds a `TrackEvaluator` on the result. On `ArgumentException` it returns the message and leaves the track unchanged; on success it stores the result and returns null.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Session;

public class SessionStateTests
{
    private static ControlPoint Point(float x)
        => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Two points, one 5 s leg.
    private static SessionState EditingWithTrack()
    {
        var state = new SessionState();
        state.Edit();
        state.ChangeTrack(t => TrackEditing.Append(TrackEditing.Append(t, Point(0f)), Point(10f)));
        return state;
    }

    private static SessionState Live()
    {
        var state = EditingWithTrack();
        state.Play();
        return state;
    }

    [Fact]
    public void StartsOffWithAnEmptyTrackAndNothingLocked()
    {
        var state = new SessionState();
        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.Empty(state.Track.Points);
        Assert.False(state.LocksInput);
    }

    [Fact]
    public void EditFromOffEntersEditingAndLocksInput()
    {
        var state = new SessionState();
        Assert.Equal(EditOutcome.FromOff, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void EditWhileEditingChangesNothing()
    {
        var state = EditingWithTrack();
        Assert.Equal(EditOutcome.Unchanged, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void EditFromLiveTakesTheDirectorOffline()
    {
        var state = Live();
        Assert.Equal(EditOutcome.FromLive, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.False(state.Director.IsLive);
    }

    [Fact]
    public void PlayWithNoPointsIsRefusedAndChangesNothing()
    {
        var off = new SessionState();
        Assert.Equal(PlayOutcome.Refused, off.Play());
        Assert.Equal(CameraMode.Off, off.Mode);

        var editing = new SessionState();
        editing.Edit();
        Assert.Equal(PlayOutcome.Refused, editing.Play());
        Assert.Equal(CameraMode.Editing, editing.Mode);
        Assert.False(editing.Director.IsLive);
    }

    [Fact]
    public void PlayFromEditingGoesLive()
    {
        var state = EditingWithTrack();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.Director.IsLive);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void PlayFromOffSaysItStartedFromOff()
    {
        var state = EditingWithTrack();
        state.Release();
        Assert.Equal(PlayOutcome.StartedFromOff, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void PlayWhilePlayingOnlyReHides()
    {
        var state = Live();
        state.Director.Tick(1f);
        Assert.Equal(PlayOutcome.ReHid, state.Play());
        Assert.Equal(1.0, state.Director.Elapsed, 5);
    }

    [Fact]
    public void PlayWhilePausedResumes()
    {
        var state = Live();
        state.Director.Tick(1f);
        state.Stop();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.False(state.Director.IsPaused);
        Assert.Equal(1.0, state.Director.Elapsed, 5);
    }

    [Fact]
    public void PlayWhenFinishedStartsAgainFromZero()
    {
        var state = Live();
        state.Director.Tick(6f);
        Assert.True(state.Director.IsFinished);
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.Director.Elapsed);
    }

    [Fact]
    public void PlayWhenPausedAndFinishedStartsAgainFromZero()
    {
        var state = Live();
        state.Director.Tick(6f);
        state.Stop();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.False(state.Director.IsPaused);
        Assert.Equal(0.0, state.Director.Elapsed);
    }

    [Fact]
    public void RestartWhileLiveStartsFromZero()
    {
        var state = Live();
        state.Director.Tick(2f);
        Assert.Equal(PlayOutcome.Started, state.Restart());
        Assert.Equal(0.0, state.Director.Elapsed);
    }

    [Fact]
    public void StopPausesOnlyWhileLive()
    {
        Assert.False(new SessionState().Stop());
        Assert.False(EditingWithTrack().Stop());

        var live = Live();
        Assert.True(live.Stop());
        Assert.True(live.Director.IsPaused);
        Assert.Equal(CameraMode.Live, live.Mode);
    }

    [Fact]
    public void ReleaseTurnsEverythingOff()
    {
        Assert.False(new SessionState().Release());

        var editing = EditingWithTrack();
        Assert.True(editing.Release());
        Assert.Equal(CameraMode.Off, editing.Mode);

        var live = Live();
        Assert.True(live.Release());
        Assert.Equal(CameraMode.Off, live.Mode);
        Assert.False(live.Director.IsLive);
        Assert.Null(live.Director.Tick(1f / 60f));
    }

    [Fact]
    public void ReleaseKeepsTheTrack()
    {
        var state = EditingWithTrack();
        state.Release();
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void TheTrackChangesOnlyWhileEditing()
    {
        const string refused = "The track can only change while editing.";

        var off = new SessionState();
        Assert.Equal(refused, off.ChangeTrack(t => TrackEditing.Append(t, Point(0f))));
        Assert.Empty(off.Track.Points);

        var live = Live();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
        Assert.Equal(2, live.Track.Points.Count);

        live.Stop();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
    }

    [Fact]
    public void ChangeTrackAppliesWhileEditing()
    {
        var state = EditingWithTrack();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLeg(t, 1, 8f)));
        Assert.Equal(8f, TrackEditing.LegSeconds(state.Track, 1));
    }

    [Fact]
    public void ChangeTrackReturnsTheRefusalAndKeepsTheTrack()
    {
        var state = EditingWithTrack();
        var before = state.Track;

        Assert.Equal("leg seconds must be > 0", state.ChangeTrack(t => TrackEditing.SetLeg(t, 1, 0f)));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void ChangeTrackRefusesATrackThatCannotBePlayed()
    {
        var state = EditingWithTrack();
        var before = state.Track;
        var backwards = new[]
        {
            new TimingKey(5f, 0f, TangentMode.Auto, 0f, 0f),
            new TimingKey(0f, 1f, TangentMode.Auto, 0f, 0f),
        };

        Assert.Equal("timing keys must have strictly increasing times", state.ChangeTrack(t => t with { Timing = backwards }));
        Assert.Same(before, state.Track);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: build error. `CinematicCam.Core.Session` does not exist.

- [ ] **Step 3: Implement**

`src/CinematicCam.Core/Session/CameraMode.cs`:

```csharp
namespace CinematicCam.Core.Session;

/// <summary>What the plugin is doing with the camera. Exactly one at a time.</summary>
public enum CameraMode
{
    /// <summary>The game has its camera; nothing is locked or blocked.</summary>
    Off,

    /// <summary>The user flies the camera to build tracks; the character is locked and flight keys and zoom are blocked.</summary>
    Editing,

    /// <summary>The Director drives the camera; the character is locked and flight keys and zoom are blocked.</summary>
    Live,
}
```

`src/CinematicCam.Core/Session/SessionState.cs`:

```csharp
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Session;

/// <summary>How <see cref="SessionState.Edit"/> changed the mode.</summary>
public enum EditOutcome { Unchanged, FromOff, FromLive }

/// <summary>What <see cref="SessionState.Play"/> or <see cref="SessionState.Restart"/> did.</summary>
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff }

/// <summary>The mode, the Director and the track, and the rules for moving between modes.</summary>
public sealed class SessionState
{
    public CameraMode Mode { get; private set; }

    public Director Director { get; } = new();

    /// <summary>The track Edit builds and Play plays. Changed only through <see cref="ChangeTrack"/>.</summary>
    public Track Track { get; private set; } = TrackEditing.Empty();

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => Mode != CameraMode.Off;

    /// <summary>Enters editing; from live, takes the Director offline.</summary>
    public EditOutcome Edit()
    {
        switch (Mode)
        {
            case CameraMode.Editing:
                return EditOutcome.Unchanged;
            case CameraMode.Live:
                Director.GoOffline();
                Mode = CameraMode.Editing;
                return EditOutcome.FromLive;
            default:
                Mode = CameraMode.Editing;
                return EditOutcome.FromOff;
        }
    }

    /// <summary>Resumes a paused shot, leaves a playing one alone, otherwise restarts.</summary>
    public PlayOutcome Play()
    {
        if (Mode == CameraMode.Live && !Director.IsFinished)
        {
            if (!Director.IsPaused) return PlayOutcome.ReHid;
            Director.Resume();
            return PlayOutcome.Resumed;
        }

        return Restart();
    }

    /// <summary>Goes live with the track from its start. Refused with no points.</summary>
    public PlayOutcome Restart()
    {
        if (Track.Points.Count == 0) return PlayOutcome.Refused;

        Director.GoLive(new TrackShot(Track));
        var fromOff = Mode == CameraMode.Off;
        Mode = CameraMode.Live;
        return fromOff ? PlayOutcome.StartedFromOff : PlayOutcome.Started;
    }

    /// <summary>Holds the current frame and stays live. Returns false unless live.</summary>
    public bool Stop()
    {
        if (Mode != CameraMode.Live) return false;
        Director.Pause();
        return true;
    }

    /// <summary>Turns off and takes the Director offline. Returns false if already off.</summary>
    public bool Release()
    {
        if (Mode == CameraMode.Off) return false;
        Director.GoOffline();
        Mode = CameraMode.Off;
        return true;
    }

    /// <summary>Applies <paramref name="change"/> if editing and the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change)
    {
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";

        try
        {
            var result = change(Track);
            _ = new TrackEvaluator(result);
            Track = result;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Core/Session tests/CinematicCam.Tests/Session
git commit -m "feat(core) move the mode state machine into core"
```

---

### Task 6: Drive the plugin from `SessionState`, remove the probe and debug commands

**Files:**
- Modify: `src/CinematicCam.Plugin/Session/CameraSession.cs`
- Delete: `src/CinematicCam.Plugin/Session/CameraMode.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs`
- Modify: `src/CinematicCam.Plugin/Game/CameraAccess.cs`
- Modify: `src/CinematicCam.Plugin/Ui/TestWindow.cs` (a `using` only)

**Interfaces:**
- Consumes: everything Task 5 produces.
- Produces: `CameraSession` keeps its public surface minus `TestState` and `Hold`: `Mode`, `Track`, `Director`, `OwnsCamera`, `LocksInput`, `Speed`, `Edit()`, `Play()`, `Restart()`, `Stop()`, `Release(string)`, `ChangeTrack(...)`, `CapturePoint()`, `Frame(float)`. `/ccam` opens the window, `/ccam release` releases, and every other verb logs "unknown verb".

There are no unit tests in the plugin. The gate is the build plus a behaviour-preserving review against the old `CameraSession`. Every log line, and the order of game-side effects, must match what it was.

- [ ] **Step 1: Rewrite `CameraSession`**

```csharp
using CinematicCam.Core.Camera;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin.Session;

/// <summary>Carries out the session's mode changes in game: free-cam, movement lock, camera ownership and UI.</summary>
internal sealed class CameraSession
{
    private readonly SessionState state = new();
    private readonly FreeCam freeCam = new();
    private readonly MovementLock movement;
    private readonly CameraOwnership ownership = new();
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;

    public CameraSession(MovementLock movement) => this.movement = movement;

    public CameraMode Mode => state.Mode;

    /// <summary>The track Edit builds and Play plays. Changed only through <see cref="ChangeTrack"/>.</summary>
    public Track Track => state.Track;

    /// <summary>Read-only view of playback state. Check IsLive before IsPaused or IsFinished.</summary>
    public Director Director => state.Director;

    /// <summary>True while the plugin writes the camera.</summary>
    public bool OwnsCamera => ownership.IsOwned;

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => state.LocksInput;

    /// <summary>The free-cam's speed setting.</summary>
    public FlySpeed Speed => freeCam.Speed;

    /// <summary>Starts free-cam: from Off at the game camera, from Live at the current frame. No-op while editing.</summary>
    public void Edit()
    {
        if (state.Mode == CameraMode.Editing) return;

        var start = state.Mode == CameraMode.Live ? lastFrame ?? CameraAccess.ReadState() : CameraAccess.ReadState();
        if (start is null) { Plugin.Log.Error("[ccam] cannot read camera state."); return; }

        switch (state.Edit())
        {
            case EditOutcome.FromOff:
                freeCam.Enable(start.Value.Position);
                movement.Hold();
                TakeCamera();
                break;
            case EditOutcome.FromLive:
                freeCam.Enable(start.Value.Position, start.Value.Roll);
                break;
            default:
                return;
        }

        GameUi.Restore();
        Plugin.Log.Information("[ccam] mode: editing");
    }

    /// <summary>Resumes a paused shot, re-hides the UI of a playing one, otherwise starts from the top.</summary>
    public void Play() => Apply(state.Play());

    /// <summary>Goes live with the current track from its start, taking the camera if off. Refused with no points.</summary>
    public void Restart() => Apply(state.Restart());

    /// <summary>Holds the current frame and stays live. No effect unless live.</summary>
    public void Stop()
    {
        if (state.Stop()) Plugin.Log.Information("[ccam] paused");
    }

    /// <summary>Turns the plugin off: stops playback and free-cam, unlocks, and hands the camera back.</summary>
    public void Release(string reason)
    {
        if (!state.Release()) return;

        freeCam.Disable();
        GameUi.Restore();
        movement.Release();
        lastFrame = null;
        ownership.Release(reason);

        // Without this the game carries on from our values rather than its own,
        // which leaves the camera wrong long after we stop writing.
        if (snapshotBeforeTakeover is { } snapshot)
        {
            CameraAccess.Restore(snapshot);
            snapshotBeforeTakeover = null;
        }

        Plugin.Log.Information("[ccam] camera released: {Reason}", reason);
    }

    /// <summary>Applies <paramref name="change"/> to the track if the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change) => state.ChangeTrack(change);

    /// <summary>Appends the current camera as a control point. Returns why it was refused, or null once appended.</summary>
    public string? CapturePoint()
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be captured while editing.";

        var camera = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (camera is null || angles is null) return "Cannot read the camera.";

        var s = camera.Value;
        var (yaw, pitch) = angles.Value;
        return state.ChangeTrack(track => TrackEditing.Append(track, new ControlPoint(s.Position, yaw, pitch, s.Fov, freeCam.Roll)));
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!ownership.IsOwned) return null;

        var frame = state.Mode switch
        {
            CameraMode.Editing => freeCam.Tick(dt),
            CameraMode.Live => state.Director.Tick(dt),
            _ => null,
        };

        lastFrame = frame;
        return frame;
    }

    /// <summary>Carries out a play or restart outcome in game.</summary>
    private void Apply(PlayOutcome outcome)
    {
        switch (outcome)
        {
            case PlayOutcome.Refused:
                Plugin.Log.Error("[ccam] cannot play a track with no points.");
                return;
            case PlayOutcome.ReHid:
                GameUi.Hide();
                return;
            case PlayOutcome.Resumed:
                GameUi.Hide();
                Plugin.Log.Information("[ccam] resumed");
                return;
            case PlayOutcome.StartedFromOff:
                movement.Hold();
                TakeCamera();
                break;
        }

        freeCam.Disable();
        GameUi.Hide();
        Plugin.Log.Information("[ccam] mode: live, {Count} points", state.Track.Points.Count);
    }

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        ownership.Take();
    }
}
```

Two differences from the old code are intended, and are consequences of removing `/ccam hold`: `Release` no longer checks `TestState`, and `Frame` returns null instead of `TestState` when off.

- [ ] **Step 2: Delete the plugin's `CameraMode`**

Delete `src/CinematicCam.Plugin/Session/CameraMode.cs`. Add `using CinematicCam.Core.Session;` to `Plugin.cs` and `Ui/TestWindow.cs`.

- [ ] **Step 3: Remove the probe and debug commands from `Plugin.cs`**

- Delete the fields `wasGPosing`, `probeCount`, `probeTime`, the `ProbeGPose()` call in `OnFrameworkUpdate`, and the `ProbeGPose` method with its doc comment.
- Change the help message to `"/ccam opens the test window | release"`.
- In `OnCommand`, delete the `reset`, `hold`, `push` and `nudge` cases. Keep `""`, `release` and `default`.
- Remove `using System.Numerics;` if nothing else in the file uses it.

- [ ] **Step 4: Remove unused camera access**

In `CameraAccess.cs`, delete `ActiveCamera()`, used only by the probe, and `ResetToDefaults()`, used only by `/ccam reset`, with their doc comments. Check first:

Run: `grep -rn "ActiveCamera()\|ResetToDefaults\|TestState\|\.Hold(" src --include='*.cs' | grep -v /obj/`
Expected: only `movement.Hold()` lines in `CameraSession.cs`.

- [ ] **Step 5: Build and test**

Run: `./build.sh`
Expected: `Build succeeded`, 0 errors. List any warnings in your report.

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add -A src/CinematicCam.Plugin
git commit -m "refactor(plugin) drive the session from core and remove debug commands"
```

---

### Task 7: Release on hook errors, and the test window minors

**Files:**
- Modify: `src/CinematicCam.Plugin/Game/CameraController.cs`
- Modify: `src/CinematicCam.Plugin/Plugin.cs` (`OnFrameworkUpdate`)
- Modify: `src/CinematicCam.Plugin/Ui/TestWindow.cs`

**Interfaces:**
- Produces: `CameraController.Faulted` (bool) and `CameraController.ClearFault()`.

Behaviour, as decided:
- **Hook error:** if anything after `Original` throws in the detour, log the exception, skip that frame's write, and write nothing more until cleared. The next framework update releases the camera with reason `"hook error"` and clears the fault. The release happens on the framework update because releasing touches the game UI, which must happen on the framework thread.
- **"1 points":** the status line reads "1 point" / "N points".
- **Stop** clears the window's error message.
- **Stale pending value:** the pending leg/hold edit is dropped on New track and whenever the mode changes.

- [ ] **Step 1: Guard the detour**

In `CameraController`, add:

```csharp
    /// <summary>True after the update hook caught an exception; it writes nothing until <see cref="ClearFault"/>.</summary>
    public bool Faulted { get; private set; }

    /// <summary>Lets the update hook write again.</summary>
    public void ClearFault() => Faulted = false;
```

Replace `UpdateDetour` with:

```csharp
    private void UpdateDetour(CameraBase* camera)
    {
        updateHook!.Original(camera);
        UpdateCount++;
        if (Faulted) return;

        try
        {
            var desired = stateSource();
            if (desired is null) return;

            CameraAccess.WriteState(desired.Value);
        }
        catch (Exception ex)
        {
            Faulted = true;
            Plugin.Log.Error(ex, "[camera] update hook failed; releasing on the next framework update.");
        }
    }
```

- [ ] **Step 2: Release on the framework update**

In `Plugin.OnFrameworkUpdate`, directly after `Input.SyncHookState();`:

```csharp
        if (Camera.Faulted)
        {
            Session.Release("hook error");
            Camera.ClearFault();
        }
```

- [ ] **Step 3: Test window minors**

In `TestWindow`, add a field `private CameraMode lastMode;` and make these changes:

At the top of `Draw()`:

```csharp
        if (session.Mode != lastMode)
        {
            pending = null;
            lastMode = session.Mode;
        }
```

The Stop button:

```csharp
        if (ImGui.Button("Stop")) { error = null; session.Stop(); }
```

The New track button:

```csharp
        if (ImGui.Button("New track")) { pending = null; error = session.ChangeTrack(_ => TrackEditing.Empty()); }
```

In `DrawStatus`, the first status line:

```csharp
        var count = track.Points.Count;
        var status = $"{count} point{(count == 1 ? "" : "s")} | total {total:0.0} s";
```

- [ ] **Step 4: Build and test**

Run: `./build.sh`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/CinematicCam.Plugin
git commit -m "fix(plugin) release on hook errors and tidy the test window"
```

---

### Task 8: In-game check (the user's)

The controller hands this list to the user after the final review. Claude cannot see the game. Read results from `~/Library/Application Support/XIV on Mac/logs/dalamud.log`, and check `dalamud.old.log` too.

Reload the dev plugin, then:

1. `/ccam` → the test window opens. Log: `CinematicCam loaded.`
2. **Edit** → you fly from the game camera's position. Log: `[ccam] mode: editing`.
3. Capture two points with the button → the status reads `1 point`, then `2 points`.
4. **Play** → the track plays and the game UI hides. Log: `[ccam] mode: live, 2 points`.
5. **Stop** mid-shot → the frame holds. Log: `[ccam] paused`. **Play** → it continues from there. Log: `[ccam] resumed`.
6. Escape while live → the UI returns, the shot keeps playing, and no system menu opens.
7. Let the shot finish, then **Play** → it starts again from the top.
8. **Edit** during playback → you fly from the current frame's position.
9. Type a leg value but don't press Enter, then **New track** → the leg field doesn't reappear with the old value.
10. **Release** → the normal game camera returns, behind the character. Log: `[ccam] camera released: window`.
11. `/ccam hold` → the log has an `unknown verb` line naming `hold`, and the camera is untouched.
12. Enter and leave GPose → no `[gpose]` lines in the log.

The hook-error path cannot be triggered in game. Review covers it.
