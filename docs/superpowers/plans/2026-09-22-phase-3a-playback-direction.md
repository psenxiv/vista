# Phase 3.a Playback Direction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Reverse and Ping-pong playback. A track's `PlaybackMode` becomes a Direction and a Loop toggle.

**Architecture:** Three tasks:
- **Task 1** adds `PlaybackClock`, a pure mapping from a playback clock to a shot time. It only creates new files.
- **Task 2** swaps `Track.Playback` for `Direction` and `Loop`, and gives the track editor its Direction drop-down and loop icon. Playback stays Forward-only here.
- **Task 3** runs `TrackPlayback` on the clock, renames `Elapsed` to `ShotTime` in Core, and adds the direction tests.

Tasks 1 and 2 run in parallel, in their own worktrees. Task 3 needs both.

`PlaybackDirection.cs` is committed with this plan, so both parallel tasks can use it:

```csharp
namespace Vista.Core.Tracks;

/// <summary>Which way a track plays: forward, backward, or out and back.</summary>
public enum PlaybackDirection { Forward, Reverse, PingPong }
```

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`.

**Spec:** `docs/superpowers/specs/2026-09-22-playback-direction-design.md`.

## Global Constraints

- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only, with no `until … sleep` loops.
- Doc comments are one line.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`. Check `git status` after committing. Do not push.
- In a self-sizing window, never align to the window's own width. Measure icon buttons with `IconButton.Width`.
- No backwards compatibility: the plugin is unreleased, and tracks aren't saved yet.
- In-game checks are the user's, and there is no `CHECKLIST.md` for this phase.

## Technical rulings (the cost if wrong is in brackets)

- **At the exact turnaround a Ping-pong clock counts as outward** (`clock ≤ L`). Both passes give the same shot time there, so it only decides which way a seek from that instant continues. [If wrong: flip one comparison in `OnReturnPass`.]
- **A seek clamps the shot time to 0 to `L` before it wraps.** A seek of 23 s on a 10 s Forward loop therefore lands on 0, where it used to land on 3. The scrub bar never sends values outside the shot. [If wrong: wrap before clamping in `TrackPlayback.Seek`.]
- **The loop tooltip names what a click does,** as the pin's does: "Play once" when lit, "Loop" when greyed (spec § UI). [If wrong: swap the two strings.]

---

### Task 1: The playback clock mapping

**Files:**
- Create: `src/Vista.Core/Tracks/PlaybackClock.cs`
- Test: `tests/Vista.Tests/Tracks/PlaybackClockTests.cs`

**Interfaces:**
- Consumes: `PlaybackDirection` (committed with this plan).
- Produces, all `public static` on `Vista.Core.Tracks.PlaybackClock`:
  - `double CycleLength(PlaybackDirection direction, double length)`
  - `double ShotTime(PlaybackDirection direction, double length, double clock)`
  - `double ClockFor(PlaybackDirection direction, double length, double shotTime, bool onReturn)`
  - `bool OnReturnPass(PlaybackDirection direction, double length, double clock)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class PlaybackClockTests
{
    private const double L = 10.0;

    [Theory]
    [InlineData(PlaybackDirection.Forward, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 10.0)]
    [InlineData(PlaybackDirection.PingPong, 20.0)]
    public void ACycleIsTheShotOrTwiceItForPingPong(PlaybackDirection direction, double expected)
        => Assert.Equal(expected, PlaybackClock.CycleLength(direction, L));

    [Theory]
    [InlineData(PlaybackDirection.Forward)]
    [InlineData(PlaybackDirection.Reverse)]
    [InlineData(PlaybackDirection.PingPong)]
    public void AZeroLengthShotHasAZeroCycleAndStaysAtZero(PlaybackDirection direction)
    {
        Assert.Equal(0.0, PlaybackClock.CycleLength(direction, 0.0));
        Assert.Equal(0.0, PlaybackClock.ShotTime(direction, 0.0, 5.0));
        Assert.Equal(0.0, PlaybackClock.ClockFor(direction, 0.0, 5.0, true));
    }

    [Theory]
    [InlineData(PlaybackDirection.Forward, 0.0, 0.0)]
    [InlineData(PlaybackDirection.Forward, 4.0, 4.0)]
    [InlineData(PlaybackDirection.Forward, 10.0, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 0.0, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 4.0, 6.0)]
    [InlineData(PlaybackDirection.Reverse, 10.0, 0.0)]
    [InlineData(PlaybackDirection.PingPong, 4.0, 4.0)]
    [InlineData(PlaybackDirection.PingPong, 10.0, 10.0)]
    [InlineData(PlaybackDirection.PingPong, 14.0, 6.0)]
    [InlineData(PlaybackDirection.PingPong, 20.0, 0.0)]
    public void ShotTimeFollowsTheDirection(PlaybackDirection direction, double clock, double expected)
        => Assert.Equal(expected, PlaybackClock.ShotTime(direction, L, clock), 9);

    [Theory]
    [InlineData(PlaybackDirection.Forward, -1.0, 0.0)]
    [InlineData(PlaybackDirection.Forward, 12.0, 10.0)]
    [InlineData(PlaybackDirection.Reverse, 12.0, 0.0)]
    [InlineData(PlaybackDirection.PingPong, 25.0, 0.0)]
    public void ShotTimeClampsTheClockToTheCycle(PlaybackDirection direction, double clock, double expected)
        => Assert.Equal(expected, PlaybackClock.ShotTime(direction, L, clock), 9);

    [Theory]
    [InlineData(PlaybackDirection.Forward, 4.0, false, 4.0)]
    [InlineData(PlaybackDirection.Forward, 4.0, true, 4.0)]
    [InlineData(PlaybackDirection.Reverse, 4.0, false, 6.0)]
    [InlineData(PlaybackDirection.PingPong, 4.0, false, 4.0)]
    [InlineData(PlaybackDirection.PingPong, 4.0, true, 16.0)]
    [InlineData(PlaybackDirection.Forward, 12.0, false, 10.0)]
    [InlineData(PlaybackDirection.Reverse, -1.0, false, 10.0)]
    public void ClockForFindsTheClockGivingAShotTime(PlaybackDirection direction, double shotTime, bool onReturn, double expected)
        => Assert.Equal(expected, PlaybackClock.ClockFor(direction, L, shotTime, onReturn), 9);

    [Theory]
    [InlineData(PlaybackDirection.PingPong, 9.0, false)]
    [InlineData(PlaybackDirection.PingPong, 10.0, false)]
    [InlineData(PlaybackDirection.PingPong, 10.5, true)]
    [InlineData(PlaybackDirection.PingPong, 20.0, true)]
    [InlineData(PlaybackDirection.Reverse, 15.0, false)]
    [InlineData(PlaybackDirection.Forward, 15.0, false)]
    public void OnlyPingPongPastTheShotIsOnItsReturnPass(PlaybackDirection direction, double clock, bool expected)
        => Assert.Equal(expected, PlaybackClock.OnReturnPass(direction, L, clock));

    [Theory]
    [InlineData(PlaybackDirection.Forward)]
    [InlineData(PlaybackDirection.Reverse)]
    [InlineData(PlaybackDirection.PingPong)]
    public void ClockForUndoesShotTimeAcrossTheCycle(PlaybackDirection direction)
    {
        var cycle = PlaybackClock.CycleLength(direction, L);
        for (var clock = 0.0; clock <= cycle; clock += 0.5)
        {
            var shot = PlaybackClock.ShotTime(direction, L, clock);
            var onReturn = PlaybackClock.OnReturnPass(direction, L, clock);
            Assert.Equal(clock, PlaybackClock.ClockFor(direction, L, shot, onReturn), 9);
        }
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter PlaybackClockTests`
Expected: the build fails because `PlaybackClock` doesn't exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace Vista.Core.Tracks;

/// <summary>Maps a playback clock, the seconds since a cycle began, to a time in the shot.</summary>
public static class PlaybackClock
{
    /// <summary>One run of the shot: its length, or twice it for Ping-pong.</summary>
    public static double CycleLength(PlaybackDirection direction, double length)
        => direction == PlaybackDirection.PingPong ? 2.0 * Math.Max(length, 0.0) : Math.Max(length, 0.0);

    /// <summary>Where the camera is in the shot at <paramref name="clock"/>, which is clamped to the cycle.</summary>
    public static double ShotTime(PlaybackDirection direction, double length, double clock)
    {
        if (length <= 0.0) return 0.0;
        var c = Math.Clamp(clock, 0.0, CycleLength(direction, length));
        return direction switch
        {
            PlaybackDirection.Reverse => length - c,
            PlaybackDirection.PingPong => c <= length ? c : (2.0 * length) - c,
            _ => c,
        };
    }

    /// <summary>The clock that gives <paramref name="shotTime"/>, clamped to the shot; Ping-pong uses the return pass when <paramref name="onReturn"/>.</summary>
    public static double ClockFor(PlaybackDirection direction, double length, double shotTime, bool onReturn)
    {
        if (length <= 0.0) return 0.0;
        var t = Math.Clamp(shotTime, 0.0, length);
        return direction switch
        {
            PlaybackDirection.Reverse => length - t,
            PlaybackDirection.PingPong => onReturn ? (2.0 * length) - t : t,
            _ => t,
        };
    }

    /// <summary>True when a Ping-pong clock is past the turnaround, heading back to the start.</summary>
    public static bool OnReturnPass(PlaybackDirection direction, double length, double clock)
        => direction == PlaybackDirection.PingPong && clock > length;
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Core/Tracks/PlaybackClock.cs tests/Vista.Tests/Tracks/PlaybackClockTests.cs
git commit -m "feat(tracks) map a playback clock to shot time by direction"
```

---

### Task 2: Direction and Loop on the track, and their controls

**Files:**
- Delete: `src/Vista.Core/Tracks/PlaybackMode.cs`
- Modify: `src/Vista.Core/Tracks/Track.cs`, `src/Vista.Core/Tracks/TrackEditing.cs` (`Empty`, `SetPlayback`), `src/Vista.Core/Tracks/TrackPlayback.cs` (the two `PlaybackMode.Loop` checks), `src/Vista.Plugin/Ui/TrackEditorWindow.cs`
- Test: `tests/Vista.Tests/Tracks/TrackEditingTests.cs`, `TrackPlaybackTests.cs`, `DirectorTests.cs`

**Interfaces:**
- Consumes: `PlaybackDirection` (committed with this plan).
- Produces:
  - `record Track(IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop)`
  - `TrackEditing.SetDirection(Track track, PlaybackDirection direction)` and `TrackEditing.SetLoop(Track track, bool loop)`. Each returns the same instance when nothing changes.
  - `TrackEditing.Empty` gives Forward, with Loop off.
  - `PlaybackMode` and `TrackEditing.SetPlayback` no longer exist.

- [ ] **Step 1: Write the failing tests in `TrackEditingTests.cs`**

Change `EmptyHasNoPointsNoKeysAndPlaysOnce`: replace `Assert.Equal(PlaybackMode.Once, track.Playback);` with:

```csharp
        Assert.Equal(PlaybackDirection.Forward, track.Direction);
        Assert.False(track.Loop);
```

Replace `SetPlaybackChangesModeWithoutTouchingPointsOrKeys` with:

```csharp
    [Fact]
    public void SetDirectionChangesItWithoutTouchingPointsOrTiming()
    {
        var track = Build3PointTrack();
        var updated = TrackEditing.SetDirection(track, PlaybackDirection.PingPong);

        Assert.Equal(PlaybackDirection.PingPong, updated.Direction);
        Assert.Equal(track.Points, updated.Points);
        Assert.Equal(track.Timing, updated.Timing);
        Assert.Same(updated, TrackEditing.SetDirection(updated, PlaybackDirection.PingPong));
    }

    [Fact]
    public void SetLoopChangesItWithoutTouchingPointsOrTiming()
    {
        var track = Build3PointTrack();
        var updated = TrackEditing.SetLoop(track, true);

        Assert.True(updated.Loop);
        Assert.Equal(track.Points, updated.Points);
        Assert.Equal(track.Timing, updated.Timing);
        Assert.Same(updated, TrackEditing.SetLoop(updated, true));
    }
```

Keep that test's remaining asserts, if any follow `Assert.Equal(track.Timing, updated.Timing);`, by moving them into both new tests.

In `DeletingTheOnlyPointEmptiesTheTrackButKeepsItsModesAndSpeed`:
- replace `TrackEditing.SetPlayback(track, PlaybackMode.Loop)` with `TrackEditing.SetLoop(TrackEditing.SetDirection(track, PlaybackDirection.Reverse), true)`;
- replace `Assert.Equal(PlaybackMode.Loop, result.Playback);` with `Assert.Equal(PlaybackDirection.Reverse, result.Direction);` and `Assert.True(result.Loop);`.

- [ ] **Step 2: Move the other tests off `PlaybackMode`**

In `TrackPlaybackTests.cs`, change the helper to take the loop flag:

```csharp
    private static Track StraightTrack(bool loop)
    {
        var track = TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x, 0f, 0f));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }
```

Then, throughout the file:
- `StraightTrack(PlaybackMode.Once)` becomes `StraightTrack(false)`, and `StraightTrack(PlaybackMode.Loop)` becomes `StraightTrack(true)`.
- `[InlineData(PlaybackMode.Once)]` becomes `[InlineData(false)]`, and `[InlineData(PlaybackMode.Loop)]` becomes `[InlineData(true)]`.
- The theory parameters `PlaybackMode mode` become `bool loop`.
- In `ZeroDurationKeepsElapsedAtZero`, `TrackEditing.SetPlayback(TrackEditing.Empty(AimMode.PathTangent), mode)` becomes `TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop)`.

In `DirectorTests.cs`, change the helper the same way:

```csharp
    private static Track StraightTrack(bool loop = false)
    {
        var track = TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x, 0f, 0f));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }
```

Also change `StraightTrack(PlaybackMode.Once)` to `StraightTrack()`.

Then check that nothing else uses the old names:
`grep -rn "PlaybackMode\|SetPlayback\|\.Playback\b" src tests` should print only `src/Vista.Core/Tracks/PlaybackMode.cs`, `Track.cs`, `TrackEditing.cs`, `TrackPlayback.cs` and `TrackEditorWindow.cs`, which Steps 4 and 5 change.

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: the build fails because `SetDirection`, `SetLoop`, `Track.Direction` and `Track.Loop` don't exist.

- [ ] **Step 4: Change Core**

Delete `src/Vista.Core/Tracks/PlaybackMode.cs`: `git rm src/Vista.Core/Tracks/PlaybackMode.cs`.

In `Track.cs`:

```csharp
/// <summary>A camera move: a path through control points, their timing, the track's speed, how it aims and how it plays.</summary>
public sealed record Track(IReadOnlyList<ControlPoint> Points, IReadOnlyList<PointTiming> Timing, float Speed, AimMode Aim, PlaybackDirection Direction, bool Loop);
```

In `TrackEditing.cs`, replace `Empty`'s summary and body:

```csharp
    /// <summary>A track with no points at the default speed, playing forward once.</summary>
    public static Track Empty(AimMode aim = AimMode.AimKeys)
        => new([], [], DefaultSpeed, aim, PlaybackDirection.Forward, false);
```

Replace `SetPlayback` with:

```csharp
    /// <summary>Sets which way the track plays; never touches points or timing.</summary>
    public static Track SetDirection(Track track, PlaybackDirection direction)
        => track.Direction == direction ? track : track with { Direction = direction };

    /// <summary>Sets whether the track loops; never touches points or timing.</summary>
    public static Track SetLoop(Track track, bool loop)
        => track.Loop == loop ? track : track with { Loop = loop };
```

In `TrackPlayback.cs`, replace both `_track.Playback == PlaybackMode.Loop` checks with `_track.Loop`. Playback stays Forward-only until Task 3. Change the three doc comments that mention `Once` and `Loop`. `Elapsed`'s becomes:

```csharp
    /// <summary>Seconds into the track. Clamps at <see cref="TrackEvaluator.Duration"/>, or wraps modulo it when the track loops.</summary>
```

`IsFinished`'s becomes:

```csharp
    /// <summary>True once a track that doesn't loop has reached its duration; never true for one that loops.</summary>
```

`Seek`'s becomes:

```csharp
    /// <summary>Jumps to <paramref name="time"/>: clamps to the track and finishes at its end, or wraps when the track loops.</summary>
```

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes.

- [ ] **Step 5: Change the track editor**

In `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, replace the fields:

```csharp
    private static readonly string[] PlaybackNames = ["Once", "Loop"];
```

with:

```csharp
    private static readonly string[] DirectionNames = ["Forward", "Reverse", "Ping-pong"];
    private static readonly PlaybackDirection[] Directions = [PlaybackDirection.Forward, PlaybackDirection.Reverse, PlaybackDirection.PingPong];
```

Also replace `private const float PlaybackWidth = 140f;` with `private const float DirectionWidth = 180f;`.

In `DrawTrackRow`, change its summary to `/// <summary>Aim and direction drop-downs, the loop toggle, track Speed and Duration, and Clear track as a trash icon at the right end.</summary>`. Then replace the whole playback block, from `var playback = session.Track.Playback == PlaybackMode.Once ? 0 : 1;` through the `EndCombo()` that closes it, with:

```csharp
        var direction = Array.IndexOf(Directions, session.Track.Direction);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(DirectionWidth);
        if (ImGui.BeginCombo("##direction", $"Direction: {DirectionNames[direction]}"))
        {
            for (var i = 0; i < DirectionNames.Length; i++)
            {
                if (!ImGui.Selectable(DirectionNames[i], i == direction) || i == direction) continue;
                var chosen = Directions[i];
                Report(session.ChangeTrack(t => TrackEditing.SetDirection(t, chosen)));
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        DrawLoop();
```

Add this method after `DrawTrackRow`:

```csharp
    /// <summary>The loop toggle: lit when the track loops, dimmed when it plays once.</summary>
    private void DrawLoop()
    {
        var loop = session.Track.Loop;
        var colour = loop ? (uint?)null : ImGui.GetColorU32(ImGuiCol.Text, 0.4f);
        if (IconButton.Draw("loop", FontAwesomeIcon.Repeat, loop ? "Play once" : "Loop", colour))
            Report(session.ChangeTrack(t => TrackEditing.SetLoop(t, !loop)));
    }
```

The track row is already disabled while not editing (`ImGui.BeginDisabled(!editing)` around `DrawTrackRow` in `Draw`), which covers both controls while live.

Replace `TrackRowWidth` so it counts the loop icon and its extra gap:

```csharp
    /// <summary>The track row's full width: its items, the seven gaps between them, and the window padding.</summary>
    private static float TrackRowWidth()
    {
        var style = ImGui.GetStyle();
        var items = AimWidth + DirectionWidth + IconButton.Width(FontAwesomeIcon.Repeat) + ImGui.CalcTextSize("Speed").X
            + ImGui.CalcTextSize("Duration").X + (FieldWidth * 2f) + IconButton.Width(FontAwesomeIcon.Trash);
        return items + (Spacing.X * 7f) + (style.WindowPadding.X * 2f);
    }
```

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors.

Run: `grep -rn "PlaybackMode\|SetPlayback\|PlaybackNames\|PlaybackWidth" src tests`
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Core/Tracks/Track.cs src/Vista.Core/Tracks/TrackEditing.cs src/Vista.Core/Tracks/TrackPlayback.cs src/Vista.Plugin/Ui/TrackEditorWindow.cs tests/Vista.Tests/Tracks/TrackEditingTests.cs tests/Vista.Tests/Tracks/TrackPlaybackTests.cs tests/Vista.Tests/Tracks/DirectorTests.cs
git commit -m "feat(tracks) replace playback mode with a direction and a loop toggle"
```

The `git rm` in Step 4 has already staged the deletion of `PlaybackMode.cs`.

---

### Task 3: Play in every direction

Starts once Tasks 1 and 2 are on `main`.

**Files:**
- Modify: `src/Vista.Core/Tracks/TrackPlayback.cs`, `src/Vista.Core/Tracks/Director.cs`, `src/Vista.Core/Session/SessionState.cs` (the two `Director.Elapsed` reads)
- Test: `tests/Vista.Tests/Tracks/TrackPlaybackTests.cs`, `DirectorTests.cs`, `tests/Vista.Tests/Session/SessionStateTests.cs`, `SessionEditingTests.cs`

**Interfaces:**
- Consumes: `PlaybackClock.CycleLength`, `ShotTime`, `ClockFor` and `OnReturnPass` (Task 1); `Track.Direction`, `Track.Loop`, `TrackEditing.SetDirection` and `TrackEditing.SetLoop` (Task 2).
- Produces:
  - `TrackPlayback.ShotTime` (`double`) in place of `Elapsed`.
  - `Director.ShotTime` (`double`) in place of `Elapsed`.
  - `IsFinished`, `Advance`, `Restart` and `Seek` keep their signatures.

- [ ] **Step 1: Rename `Elapsed` to `ShotTime` in the tests**

In `TrackPlaybackTests.cs`, `DirectorTests.cs` and `SessionStateTests.cs`, replace every `.Elapsed` with `.ShotTime`, and `Elapsed` with `ShotTime` in test names (for example `ElapsedFollowsATrackShotsPlayback` becomes `ShotTimeFollowsATrackShotsPlayback`). Check with `grep -rn "Elapsed" tests`, which should print nothing.

- [ ] **Step 2: Replace the loop seek test and add the direction tests in `TrackPlaybackTests.cs`**

Change the helper to take a direction too:

```csharp
    private static Track StraightTrack(bool loop, PlaybackDirection direction = PlaybackDirection.Forward)
    {
        var track = TrackEditing.SetDirection(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop), direction);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x, 0f, 0f));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }
```

Replace `SeekLoopWraps` with the following, since a seek now clamps to the shot before it wraps (see Technical rulings):

```csharp
    [Fact]
    public void SeekingALoopToItsEndShowsTheFirstFrame()
    {
        var playback = new TrackPlayback(StraightTrack(true));
        playback.Seek(10.0);
        Assert.Equal(0.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);

        playback.Seek(23.0);
        Assert.Equal(0.0, playback.ShotTime, 5);

        playback.Seek(-1.0);
        Assert.Equal(0.0, playback.ShotTime, 5);
    }
```

Change `NegativeFrameTimeDoesNotRunTimeBackwards` and `ZeroDurationKeepsElapsedAtZero` (by now `ZeroDurationKeepsShotTimeAtZero`) to cover every direction. Give each an extra `PlaybackDirection direction` parameter, pass `direction` to `StraightTrack` or `SetDirection`, and replace their `InlineData` with:

```csharp
    [InlineData(false, PlaybackDirection.Forward)]
    [InlineData(true, PlaybackDirection.Forward)]
    [InlineData(false, PlaybackDirection.Reverse)]
    [InlineData(true, PlaybackDirection.Reverse)]
    [InlineData(false, PlaybackDirection.PingPong)]
    [InlineData(true, PlaybackDirection.PingPong)]
```

In the negative frame time test, the expected values become the shot time the clock gives. The fresh playback's shot time is `PlaybackClock.ShotTime(direction, 10.0, 0.0)`, and the one played for 2 s is `PlaybackClock.ShotTime(direction, 10.0, 2.0)`:

```csharp
    public void NegativeFrameTimeDoesNotRunTimeBackwards(bool loop, PlaybackDirection direction)
    {
        var fresh = new TrackPlayback(StraightTrack(loop, direction));
        fresh.Advance(-1f);
        Assert.Equal(PlaybackClock.ShotTime(direction, 10.0, 0.0), fresh.ShotTime, 5);

        var playing = new TrackPlayback(StraightTrack(loop, direction));
        playing.Advance(2f);
        playing.Advance(-1f);
        Assert.Equal(PlaybackClock.ShotTime(direction, 10.0, 2.0), playing.ShotTime, 5);
    }
```

In the zero-duration test, build the track with `TrackEditing.SetDirection(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop), direction)`, and after `Advance(5f)` also assert `Assert.Equal(!loop, playback.IsFinished);`.

Turn `PositionAtFiveSecondsMatchesUnder60FpsAnd30FpsDeltaSequences` into a theory over the three directions, with Loop off:
- add `[Theory]`, `[InlineData(PlaybackDirection.Forward)]`, `[InlineData(PlaybackDirection.Reverse)]` and `[InlineData(PlaybackDirection.PingPong)]`, in place of `[Fact]`;
- add the parameter `PlaybackDirection direction`;
- build both playbacks from `StraightTrack(false, direction)`;
- expect `PlaybackClock.ShotTime(direction, 10.0, 5.0)` in place of `5.0` in the two shot time asserts.

Add these tests:

```csharp
    [Fact]
    public void ReverseStartsAtTheEndAndWalksBack()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.Reverse));
        Assert.Equal(10.0, playback.ShotTime, 5);

        playback.Advance(3f);
        Assert.Equal(7.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);
    }

    [Theory]
    [InlineData(PlaybackDirection.Reverse, 15f)]
    [InlineData(PlaybackDirection.PingPong, 25f)]
    public void ReverseAndPingPongFinishHoldingTheFirstFrame(PlaybackDirection direction, float past)
    {
        var track = StraightTrack(false, direction);
        var playback = new TrackPlayback(track);

        var atEnd = playback.Advance(past);
        Assert.True(playback.IsFinished);
        Assert.Equal(0.0, playback.ShotTime, 5);
        Assert.Equal(new TrackEvaluator(track).Evaluate(0.0), atEnd);
        Assert.Equal(atEnd, playback.Advance(5f));
    }

    [Fact]
    public void ReverseLoopCutsBackToTheEnd()
    {
        var playback = new TrackPlayback(StraightTrack(true, PlaybackDirection.Reverse));
        playback.Advance(12f);
        Assert.Equal(8.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void PingPongGoesOutAndComesBack()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(4f);
        Assert.Equal(4.0, playback.ShotTime, 5);

        playback.Advance(10f);
        Assert.Equal(6.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void PingPongOnceFinishesAfterTwiceTheShot()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(19.5f);
        Assert.False(playback.IsFinished);
        Assert.Equal(0.5, playback.ShotTime, 3);

        playback.Advance(1f);
        Assert.True(playback.IsFinished);
        Assert.Equal(0.0, playback.ShotTime, 5);
    }

    [Fact]
    public void PingPongLoopTurnsBackAtTheStartWithoutACut()
    {
        var playback = new TrackPlayback(StraightTrack(true, PlaybackDirection.PingPong));
        playback.Advance(19f);
        Assert.Equal(1.0, playback.ShotTime, 3);

        playback.Advance(2f);
        Assert.Equal(1.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void APingPongTurnaroundStaysForTwiceTheHold()
    {
        // The last point holds 2 s, so the shot is 12 s and the camera sits at x = 10 from clock 10 to 14.
        var track = TrackEditing.SetHold(StraightTrack(false, PlaybackDirection.PingPong), 2, 2f);

        float XAt(float clock)
        {
            var playback = new TrackPlayback(track);
            return playback.Advance(clock)!.Value.Position.X;
        }

        Assert.Equal(10f, XAt(10.5f), 3);
        Assert.Equal(10f, XAt(12f), 3);
        Assert.Equal(10f, XAt(13.5f), 3);
        Assert.True(XAt(9.5f) < 9.99f);
        Assert.True(XAt(14.5f) < 9.99f);
    }

    [Fact]
    public void SeekInReverseSetsTheShotTimeAndFinishesAtTheStart()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.Reverse));
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);

        playback.Advance(1f);
        Assert.Equal(3.0, playback.ShotTime, 5);

        playback.Seek(0.0);
        Assert.True(playback.IsFinished);
    }

    [Fact]
    public void SeekInPingPongKeepsTheOutwardPass()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(2f);
        playback.Seek(6.0);
        playback.Advance(1f);
        Assert.Equal(7.0, playback.ShotTime, 5);
    }

    [Fact]
    public void SeekInPingPongKeepsTheReturnPass()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(13f);
        playback.Seek(4.0);
        Assert.Equal(4.0, playback.ShotTime, 5);

        playback.Advance(1f);
        Assert.Equal(3.0, playback.ShotTime, 5);
    }

    [Fact]
    public void SeekingAFinishedPingPongBackCarriesOnHome()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.PingPong));
        playback.Advance(25f);
        Assert.True(playback.IsFinished);

        playback.Seek(3.0);
        Assert.False(playback.IsFinished);
        playback.Advance(1f);
        Assert.Equal(2.0, playback.ShotTime, 5);
    }

    [Fact]
    public void RestartPutsAReverseShotBackAtItsEnd()
    {
        var playback = new TrackPlayback(StraightTrack(false, PlaybackDirection.Reverse));
        playback.Advance(20f);
        playback.Restart();
        Assert.Equal(10.0, playback.ShotTime, 5);
        Assert.False(playback.IsFinished);
    }
```

- [ ] **Step 3: Add the session tests**

In `SessionEditingTests.cs` (three points at x = 0, 10 and 20 at the default 2 yalms per second: two 5 s legs, so a 10 s shot):

```csharp
    [Fact]
    public void CueingAReverseShotPutsTheScrubHeadAtTheEnd()
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.Reverse));
        state.Cue();
        Assert.Equal(10.0, state.ScrubHead, 5);
    }

    [Fact]
    public void EditFromALiveReverseShotTakesItsShotTime()
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.Reverse));
        state.Play();
        state.Director.Tick(2f);
        state.Edit();
        Assert.Equal(8.0, state.ScrubHead, 5);
    }

    [Fact]
    public void ScrubbingALivePingPongShotOnItsWayBackKeepsItGoingBack()
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.PingPong));
        state.Play();
        state.Director.Tick(13f);
        Assert.Equal(7.0, state.ScrubHead, 3);

        state.BeginScrub();
        state.ScrubTo(4.0);
        state.EndScrub();
        state.Director.Tick(1f);
        Assert.Equal(3.0, state.ScrubHead, 3);
    }
```

- [ ] **Step 4: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: the build fails because `ShotTime` doesn't exist on `TrackPlayback` or `Director`.

- [ ] **Step 5: Run `TrackPlayback` on the clock**

Replace the body of `src/Vista.Core/Tracks/TrackPlayback.cs` with:

```csharp
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Advances a track's playback clock frame by frame, by its direction and loop setting.</summary>
public sealed class TrackPlayback
{
    private readonly Track _track;
    private readonly TrackEvaluator _evaluator;
    private double _clock;

    /// <summary>Where the camera is in the shot, from 0 to <see cref="TrackEvaluator.Duration"/>.</summary>
    public double ShotTime => PlaybackClock.ShotTime(_track.Direction, _evaluator.Duration, _clock);

    /// <summary>True once a track that doesn't loop has reached the end of its cycle; never true for one that loops.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Starts <paramref name="track"/> at the start of its cycle.</summary>
    public TrackPlayback(Track track)
    {
        _track = track;
        _evaluator = new TrackEvaluator(track);
    }

    /// <summary>Adds <paramref name="dt"/> to the clock, stops or wraps it at the end of the cycle, and evaluates the shot time.</summary>
    public CameraState? Advance(float dt)
    {
        var cycle = Cycle;
        var next = _clock + Math.Max(dt, 0f);

        if (_track.Loop)
        {
            _clock = cycle > 0.0 ? next % cycle : 0.0;
        }
        else if (next >= cycle)
        {
            _clock = cycle;
            IsFinished = true;
        }
        else
        {
            _clock = next;
        }

        return _evaluator.Evaluate(ShotTime);
    }

    /// <summary>Puts the clock back to the start of the cycle and clears <see cref="IsFinished"/>.</summary>
    public void Restart()
    {
        _clock = 0.0;
        IsFinished = false;
    }

    /// <summary>Jumps to shot time <paramref name="time"/>, clamped to the shot, keeping a Ping-pong shot's pass.</summary>
    public void Seek(double time)
    {
        var length = _evaluator.Duration;
        var cycle = Cycle;
        var onReturn = PlaybackClock.OnReturnPass(_track.Direction, length, _clock);
        var clock = PlaybackClock.ClockFor(_track.Direction, length, time, onReturn);

        if (_track.Loop)
        {
            _clock = cycle > 0.0 ? clock % cycle : 0.0;
            return;
        }

        _clock = clock;
        IsFinished = _clock >= cycle;
    }

    private double Cycle => PlaybackClock.CycleLength(_track.Direction, _evaluator.Duration);
}
```

- [ ] **Step 6: Rename `Elapsed` in the Director and the session**

In `src/Vista.Core/Tracks/Director.cs`, replace the `Elapsed` property with:

```csharp
    /// <summary>Where the current <see cref="TrackShot"/>'s camera is in the shot; 0 for other shots or before going live.</summary>
    public double ShotTime => _playback?.ShotTime ?? 0.0;
```

In `src/Vista.Core/Session/SessionState.cs`, replace `Director.Elapsed` with `Director.ShotTime` in `Edit` and `ScrubHead` (two places).

Run: `grep -rn "Elapsed" src tests`
Expected: no output.

- [ ] **Step 7: Run the tests and the build**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings.

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors. The plugin doesn't read `Elapsed`, so nothing there changes.

- [ ] **Step 8: Commit**

```bash
git add src/Vista.Core/Tracks/TrackPlayback.cs src/Vista.Core/Tracks/Director.cs src/Vista.Core/Session/SessionState.cs tests/Vista.Tests/Tracks/TrackPlaybackTests.cs tests/Vista.Tests/Tracks/DirectorTests.cs tests/Vista.Tests/Session/SessionStateTests.cs tests/Vista.Tests/Session/SessionEditingTests.cs
git commit -m "feat(tracks) play tracks in reverse and ping-pong"
```
