# Phase 3.e Playlist and Live Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The scene gets a playlist of entries (a track, an optional loop count, a Cut transition). Live plays the playlist, and snap points become single-point tracks. The Vista window gets a Playlist compartment on the right.

**Architecture:** Four tasks in three waves:
- **Wave 1, in parallel:**
  - Task 1 adds the playlist model and its pure edits. Deleting a track removes its entries.
  - Task 2 adds `PlaylistPlayback`, which plays entries in turn. The Director plays a `PlaylistShot`, and the snap shot and `SnapPoint` go.
- **Wave 2:** Task 3 makes the session go live with the playlist and pass playlist edits through. Existing tests that go live get the edited track added to the playlist first.
- **Wave 3:** Task 4 adds the Playlist compartment, the Hierarchy's "Add to playlist", the mode drop-down's Live state, and a scrub bar sized to the entry that's playing.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`.

**Spec:** `docs/superpowers/specs/2026-09-22-playlist-and-live-design.md`.

## Global Constraints

- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only, with no `until … sleep` loops.
- Doc comments are one line.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A` (leave the untracked `CHECKLIST-*.md` files alone). Check `git status` after committing. Do not push.
- Every playlist edit is one undo step and is refused unless editing.
- In a row loop that can edit the scene, read every row from a snapshot taken before the loop. In a self-sizing window, never align to the window's own width; measure icon buttons with `IconButton.Width`.

## Technical rulings (the cost if wrong is in brackets)

- **`Scene` gains `IReadOnlyList<PlaylistEntry> Playlist` as its third positional member,** before the defaulted anchor members. Only `SceneEditing.New` constructs a scene. [If wrong: reorder.]
- **`PlaylistPlayback` keeps its own clock per entry** and uses `PlaybackClock` and `TrackEvaluator` directly, not `TrackPlayback`. It needs the overshoot past an entry's end to carry time over, and to count loop passes. [If wrong: expose the clock on `TrackPlayback`.]
- **An entry's length:** with loop count N, N cycles; with no count, one cycle, or unending when its track loops. A zero-length entry (for example a single-point track with no hold) is shown for the frame it's reached on, then left on the next frame that has time. [If wrong: skip zero-length entries.]
- **The Director plays through an `IPlayback` interface** that `TrackPlayback` and `PlaylistPlayback` both implement. `TrackShot` stays for tests and the switchboard. [If wrong: drop `TrackShot`.]
- **Going live builds the playlist from the scene at that moment,** using each entry's track in the world. Nothing can be edited while live, so it can't go stale. [If wrong: none.]
- **Edit from Live:** the scrub head takes the playing entry's shot time when that entry plays the edited track, otherwise 0. The edited track doesn't change. The free-cam still starts from the last frame, as today. The spec doesn't settle this; it's reported as a deviation. [If wrong: switch the edited track to the playing one.]
- **Existing tests that go live** get `state.AddToPlaylist(state.EditedTrackId);` inserted at the earliest point after the edited track has its points, and before any live edit, undo or scrub the test is about. Nothing else in them changes. [If wrong: none; test plumbing.]
- **The loop cell is a drag-int field from 0 to 99, where 0 means "follow the track".** It shows "—" at 0, "∞" at 0 when the entry holds the playlist, and "×N" otherwise, amber when not "—". The value is committed when the field is let go. Ctrl-click types a number, as every ImGui drag field does. [If wrong: a popup editor.]
- **Dropping a Hierarchy row on the Playlist** reuses the Hierarchy's existing `VISTA_TRACK` drag payload (the track's index in the scene), so the Hierarchy needs no drag changes. [If wrong: a separate payload.]

---

### Task 1: The playlist model and edits

**Files:**
- Create: `src/Vista.Core/Scenes/PlaylistEntry.cs`, `src/Vista.Core/Scenes/PlaylistEditing.cs`
- Modify: `src/Vista.Core/Scenes/Scene.cs`, `src/Vista.Core/Scenes/SceneEditing.cs` (`New`, `Delete`)
- Test: `tests/Vista.Tests/Scenes/PlaylistEditingTests.cs` (new), `tests/Vista.Tests/Scenes/SceneEditingTests.cs`

**Interfaces:**
- Produces (namespace `Vista.Core.Scenes`):
  - `enum Transition { Cut }`
  - `record PlaylistEntry(Guid Id, Guid TrackId, int? Loops = null, Transition Transition = Transition.Cut)`
  - `record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden, IReadOnlyList<PlaylistEntry> Playlist, Anchor Anchor = default, bool AnchorPlaced = false)`
  - `static class PlaylistEditing`:
    - `const int MaxLoops = 99`
    - `(Scene Scene, Guid Added) Add(Scene scene, Guid trackId, int? index = null)`
    - `Scene Remove(Scene scene, Guid entryId)`
    - `Scene Move(Scene scene, int from, int to)`
    - `Scene SetLoops(Scene scene, Guid entryId, int? loops)`
    - `int IndexOf(Scene scene, Guid entryId)`
    - `bool HoldsPlaylist(Scene scene, PlaylistEntry entry)`
    - `bool CanPlay(Scene scene)`
  - `SceneEditing.Delete` also removes the deleted track's entries.

- [ ] **Step 1: Write the failing tests**

Create `tests/Vista.Tests/Scenes/PlaylistEditingTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Scenes;

public class PlaylistEditingTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Two tracks; the first has two points, the second none.
    private static Scene TwoTracks()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Replace(scene, TrackEditing.Append(TrackEditing.Append(scene.Tracks[0], Point(0f)), Point(10f)));
        return SceneEditing.Add(scene).Scene;
    }

    [Fact]
    public void ANewSceneHasAnEmptyPlaylist() => Assert.Empty(SceneEditing.New().Playlist);

    [Fact]
    public void AddAppendsOrInsertsAnEntryWithNoLoopCount()
    {
        var scene = TwoTracks();
        var (one, first) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);
        var (two, second) = PlaylistEditing.Add(one, scene.Tracks[1].Id, 0);

        Assert.Equal(new[] { second, first }, two.Playlist.Select(e => e.Id));
        Assert.Equal(scene.Tracks[1].Id, two.Playlist[0].TrackId);
        Assert.Null(two.Playlist[1].Loops);
        Assert.Equal(Transition.Cut, two.Playlist[1].Transition);
    }

    [Fact]
    public void ATrackCanAppearTwiceAndAddRefusesAMissingTrack()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;
        scene = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;

        Assert.Equal(2, scene.Playlist.Count);
        Assert.NotEqual(scene.Playlist[0].Id, scene.Playlist[1].Id);
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Add(scene, Guid.NewGuid()));
    }

    [Fact]
    public void RemoveAndMoveChangeTheEntries()
    {
        var scene = TwoTracks();
        var (a, first) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);
        var (b, second) = PlaylistEditing.Add(a, scene.Tracks[1].Id);

        Assert.Equal(new[] { second, first }, PlaylistEditing.Move(b, 0, 1).Playlist.Select(e => e.Id));
        Assert.Same(b, PlaylistEditing.Move(b, 1, 1));
        Assert.Equal(new[] { second }, PlaylistEditing.Remove(b, first).Playlist.Select(e => e.Id));
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Remove(b, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Move(b, 0, 2));
    }

    [Fact]
    public void SetLoopsClampsAndClears()
    {
        var scene = TwoTracks();
        var (a, id) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);

        Assert.Equal(3, PlaylistEditing.SetLoops(a, id, 3).Playlist[0].Loops);
        Assert.Equal(PlaylistEditing.MaxLoops, PlaylistEditing.SetLoops(a, id, 500).Playlist[0].Loops);
        Assert.Equal(1, PlaylistEditing.SetLoops(a, id, 0).Playlist[0].Loops);
        Assert.Null(PlaylistEditing.SetLoops(PlaylistEditing.SetLoops(a, id, 4), id, null).Playlist[0].Loops);
        Assert.Same(a, PlaylistEditing.SetLoops(a, id, null));
    }

    [Fact]
    public void AnEntryHoldsThePlaylistOnlyWithNoCountAndALoopingTrack()
    {
        var scene = TwoTracks();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[0], true));
        var (a, id) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);

        Assert.True(PlaylistEditing.HoldsPlaylist(a, a.Playlist[0]));
        var counted = PlaylistEditing.SetLoops(a, id, 2);
        Assert.False(PlaylistEditing.HoldsPlaylist(counted, counted.Playlist[0]));
    }

    [Fact]
    public void ThePlaylistCanPlayOnlyWithAnEntryWhoseTrackHasPoints()
    {
        var scene = TwoTracks();
        Assert.False(PlaylistEditing.CanPlay(scene));
        Assert.False(PlaylistEditing.CanPlay(PlaylistEditing.Add(scene, scene.Tracks[1].Id).Scene));
        Assert.True(PlaylistEditing.CanPlay(PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene));
    }

    [Fact]
    public void DeletingATrackRemovesItsEntries()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, scene.Tracks[1].Id).Scene;
        scene = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;
        scene = PlaylistEditing.Add(scene, scene.Tracks[1].Id).Scene;

        var result = SceneEditing.Delete(scene, scene.Tracks[1].Id).Scene;

        Assert.Equal(new[] { scene.Tracks[0].Id }, result.Playlist.Select(e => e.TrackId));
    }

    [Fact]
    public void DuplicatingATrackAddsNoEntry()
    {
        var scene = TwoTracks();
        var withEntry = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;
        Assert.Single(SceneEditing.Duplicate(withEntry, scene.Tracks[0].Id).Scene.Playlist);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter PlaylistEditingTests`
Expected: the build fails because `PlaylistEntry`, `PlaylistEditing` and `Scene.Playlist` don't exist.

- [ ] **Step 3: Add the model**

`src/Vista.Core/Scenes/PlaylistEntry.cs`:

```csharp
namespace Vista.Core.Scenes;

/// <summary>How playback moves from one playlist entry to the next.</summary>
public enum Transition { Cut }

/// <summary>One playlist entry: the track it plays, how many times (null follows the track), and the transition into the next.</summary>
public sealed record PlaylistEntry(Guid Id, Guid TrackId, int? Loops = null, Transition Transition = Transition.Cut);
```

`Scene.cs`:

```csharp
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>The tracks being worked on, in Hierarchy order, which are hidden, the playlist Live plays, and the anchor they hang off.</summary>
public sealed record Scene(IReadOnlyList<Track> Tracks, IReadOnlySet<Guid> Hidden, IReadOnlyList<PlaylistEntry> Playlist, Anchor Anchor = default, bool AnchorPlaced = false);
```

In `SceneEditing.cs`, `New` becomes `new([TrackEditing.Empty()], new HashSet<Guid>(), [])`. `Delete` also drops the track's entries: in the `with` it returns, add `Playlist = scene.Playlist.Where(e => e.TrackId != id).ToArray()`. Update `Delete`'s summary to say so.

- [ ] **Step 4: Add the playlist edits**

`src/Vista.Core/Scenes/PlaylistEditing.cs`:

```csharp
namespace Vista.Core.Scenes;

/// <summary>Edits a scene's playlist: add, remove, move and loop counts, and what Live can play.</summary>
public static class PlaylistEditing
{
    /// <summary>The most times an entry can play its track.</summary>
    public const int MaxLoops = 99;

    /// <summary>Adds an entry for track <paramref name="trackId"/> at <paramref name="index"/>, or at the end.</summary>
    public static (Scene Scene, Guid Added) Add(Scene scene, Guid trackId, int? index = null)
    {
        SceneEditing.Get(scene, trackId);
        var entry = new PlaylistEntry(Guid.NewGuid(), trackId);
        var entries = scene.Playlist.ToList();
        entries.Insert(Math.Clamp(index ?? entries.Count, 0, entries.Count), entry);
        return (scene with { Playlist = entries }, entry.Id);
    }

    /// <summary>Removes entry <paramref name="entryId"/>.</summary>
    public static Scene Remove(Scene scene, Guid entryId)
    {
        var index = Require(scene, entryId);
        var entries = scene.Playlist.ToList();
        entries.RemoveAt(index);
        return scene with { Playlist = entries };
    }

    /// <summary>Moves the entry at <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Scene Move(Scene scene, int from, int to)
    {
        if (from < 0 || from >= scene.Playlist.Count || to < 0 || to >= scene.Playlist.Count)
            throw new ArgumentException("There is no such playlist entry to move.");
        if (from == to) return scene;
        var entries = scene.Playlist.ToList();
        var entry = entries[from];
        entries.RemoveAt(from);
        entries.Insert(to, entry);
        return scene with { Playlist = entries };
    }

    /// <summary>Sets how many times an entry plays, 1 to <see cref="MaxLoops"/>, or null to follow its track.</summary>
    public static Scene SetLoops(Scene scene, Guid entryId, int? loops)
    {
        var index = Require(scene, entryId);
        var clamped = loops is { } n ? Math.Clamp(n, 1, MaxLoops) : (int?)null;
        if (scene.Playlist[index].Loops == clamped) return scene;
        var entries = scene.Playlist.ToArray();
        entries[index] = entries[index] with { Loops = clamped };
        return scene with { Playlist = entries };
    }

    /// <summary>The index of entry <paramref name="entryId"/>, or −1.</summary>
    public static int IndexOf(Scene scene, Guid entryId)
    {
        for (var i = 0; i < scene.Playlist.Count; i++)
            if (scene.Playlist[i].Id == entryId) return i;
        return -1;
    }

    /// <summary>True when an entry holds the playlist for good: no loop count and a looping track.</summary>
    public static bool HoldsPlaylist(Scene scene, PlaylistEntry entry)
        => entry.Loops is null && SceneEditing.Get(scene, entry.TrackId).Loop;

    /// <summary>True when an entry's track has points, so Live has something to play.</summary>
    public static bool CanPlay(Scene scene) => scene.Playlist.Any(e => SceneEditing.Get(scene, e.TrackId).Points.Count > 0);

    private static int Require(Scene scene, Guid entryId)
        => IndexOf(scene, entryId) is var index and >= 0 ? index : throw new ArgumentException("There is no such playlist entry.");
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings. Also run `./build.sh`; the plugin must still build.

- [ ] **Step 6: Commit**

```bash
git add src/Vista.Core/Scenes/PlaylistEntry.cs src/Vista.Core/Scenes/PlaylistEditing.cs src/Vista.Core/Scenes/Scene.cs src/Vista.Core/Scenes/SceneEditing.cs tests/Vista.Tests/Scenes/PlaylistEditingTests.cs
git commit -m "feat(scenes) add a playlist of entries to the scene"
```

---

### Task 2: Playing a playlist

**Files:**
- Create: `src/Vista.Core/Tracks/IPlayback.cs`, `src/Vista.Core/Tracks/PlaylistPlayback.cs`
- Modify: `src/Vista.Core/Tracks/TrackPlayback.cs` (implements `IPlayback`, adds `ShotLength`), `src/Vista.Core/Tracks/Shot.cs`, `src/Vista.Core/Tracks/Director.cs`
- Delete: `src/Vista.Core/Tracks/SnapPoint.cs`
- Test: `tests/Vista.Tests/Tracks/PlaylistPlaybackTests.cs` (new), `tests/Vista.Tests/Tracks/DirectorTests.cs`

**Interfaces:**
- Produces (namespace `Vista.Core.Tracks`):
  - `interface IPlayback { double ShotTime { get; } double ShotLength { get; } bool IsFinished { get; } CameraState? Advance(float dt); void Seek(double time); void Restart(); }`
  - `record PlaylistItem(Guid EntryId, Track Track, int? Loops)`: one playable entry, with its track in the world.
  - `class PlaylistPlayback : IPlayback`, constructed from `IReadOnlyList<PlaylistItem>` (throws `ArgumentException` when empty), with `int Index`, `Guid EntryId`.
  - `record PlaylistShot(IReadOnlyList<PlaylistItem> Items) : Shot`; `SnapShot` and `SnapPoint` removed.
  - `Director.ShotLength` (`double`) and `Director.Playlist` (`PlaylistPlayback?`).

- [ ] **Step 1: Write the failing tests**

Create `tests/Vista.Tests/Tracks/PlaylistPlaybackTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class PlaylistPlaybackTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Points at x = 0, 5, 10 in two 5 s legs: a 10 s track.
    private static Track Ten(bool loop = false, PlaybackDirection direction = PlaybackDirection.Forward)
    {
        var track = TrackEditing.SetDirection(TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop), direction);
        foreach (var x in new[] { 0f, 5f, 10f }) track = TrackEditing.Append(track, Point(x));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }

    // A single point at x held for the given seconds.
    private static Track Snap(float x, float hold, bool loop = false)
        => TrackEditing.SetHold(TrackEditing.SetLoop(TrackEditing.Append(TrackEditing.Empty(), Point(x)), loop), 0, hold);

    private static PlaylistItem Item(Track track, int? loops = null) => new(Guid.NewGuid(), track, loops);

    [Fact]
    public void AnEmptyPlaylistIsRefused() => Assert.Throws<ArgumentException>(() => new PlaylistPlayback([]));

    [Fact]
    public void EntriesPlayInTurnCarryingTimeOver()
    {
        var items = new[] { Item(Ten()), Item(Ten()) };
        var playback = new PlaylistPlayback(items);

        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(items[1].EntryId, playback.EntryId);
        Assert.Equal(2.0, playback.ShotTime, 4);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ALongFramePassesThroughShortEntries()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Snap(50f, 1f)), Item(Ten())]);
        playback.Advance(12f);

        Assert.Equal(2, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void TheLastFrameHoldsAtTheEnd()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())]);
        var atEnd = playback.Advance(25f);

        Assert.True(playback.IsFinished);
        Assert.Equal(1, playback.Index);
        Assert.Equal(10.0, playback.ShotTime, 4);
        Assert.Equal(atEnd, playback.Advance(5f));
    }

    [Fact]
    public void ALoopCountPlaysTheTrackThatManyTimes()
    {
        var playback = new PlaylistPlayback([Item(Ten(), 3), Item(Ten())]);
        playback.Advance(25f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);

        playback.Advance(10f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);
    }

    [Fact]
    public void APingPongLoopIsOneRoundTrip()
    {
        var playback = new PlaylistPlayback([Item(Ten(direction: PlaybackDirection.PingPong), 1), Item(Ten())]);
        playback.Advance(15f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);

        playback.Advance(10f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(5.0, playback.ShotTime, 4);
    }

    [Fact]
    public void ALoopingTrackWithNoCountHoldsThePlaylist()
    {
        var playback = new PlaylistPlayback([Item(Ten(loop: true)), Item(Ten())]);
        playback.Advance(1003f);

        Assert.Equal(0, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 3);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ACountOverridesTheTracksLoop()
    {
        var playback = new PlaylistPlayback([Item(Ten(loop: true), 1), Item(Ten())]);
        playback.Advance(12f);

        Assert.Equal(1, playback.Index);
        Assert.Equal(2.0, playback.ShotTime, 4);
    }

    [Fact]
    public void AZeroLengthEntryIsShownForOneFrame()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Snap(50f, 0f)), Item(Ten())]);

        var reached = playback.Advance(10f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(50f, reached!.Value.Position.X, 4);

        playback.Advance(0.5f);
        Assert.Equal(2, playback.Index);
        Assert.Equal(0.5, playback.ShotTime, 4);
    }

    [Fact]
    public void ASnapPointHoldsForItsHoldThenMovesOn()
    {
        var playback = new PlaylistPlayback([Item(Snap(50f, 3f)), Item(Ten())]);

        var held = playback.Advance(2f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(50f, held!.Value.Position.X, 4);

        playback.Advance(2f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(1.0, playback.ShotTime, 4);
    }

    [Fact]
    public void ALoopingSnapPointHoldsThePlaylist()
    {
        var playback = new PlaylistPlayback([Item(Snap(50f, 0f, loop: true)), Item(Ten())]);
        playback.Advance(100f);
        Assert.Equal(0, playback.Index);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SeekingStaysInTheCurrentLoopPass()
    {
        var playback = new PlaylistPlayback([Item(Ten(), 2), Item(Ten())]);
        playback.Advance(13f);

        playback.Seek(8.0);
        Assert.Equal(8.0, playback.ShotTime, 4);

        playback.Advance(1f);
        Assert.Equal(0, playback.Index);
        Assert.Equal(9.0, playback.ShotTime, 4);

        playback.Advance(1f);
        Assert.Equal(1, playback.Index);
        Assert.Equal(0.0, playback.ShotTime, 4);
    }

    [Fact]
    public void SeekingAFinishedPlaylistBackUnfinishesIt()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())]);
        playback.Advance(25f);

        playback.Seek(3.0);

        Assert.False(playback.IsFinished);
        Assert.Equal(1, playback.Index);
        Assert.Equal(3.0, playback.ShotTime, 4);
    }

    [Fact]
    public void RestartGoesBackToTheFirstEntry()
    {
        var playback = new PlaylistPlayback([Item(Ten()), Item(Ten())]);
        playback.Advance(25f);

        playback.Restart();

        Assert.Equal(0, playback.Index);
        Assert.Equal(0.0, playback.ShotTime);
        Assert.False(playback.IsFinished);
        Assert.Equal(10.0, playback.ShotLength, 4);
    }
}
```

In `DirectorTests.cs`:
- Delete `Snap()` and the snap tests: `TickOnASnapShotReturnsItsPoseEveryFrame`, `IsFinishedIsFalseForNonTrackShots`, `ShotTimeIsZeroForNonTrackShots`, `ASnapShotCarriesItsRoll`, and `SeekDoesNothingOfflineOrForSnapShots`. If a deleted test also checked something about track shots or going offline, keep that part: move its track-shot assertions into a renamed test (for example `SeekDoesNothingOffline`) and list it in the report.
- In the test that throws on a broken track (around line 276), replace the `SnapShot` it goes live with first with `new GameCameraShot()`.
- Add:

```csharp
    [Fact]
    public void APlaylistShotPlaysItsEntriesInTurn()
    {
        var items = new[] { new PlaylistItem(Guid.NewGuid(), StraightTrack(), null), new PlaylistItem(Guid.NewGuid(), StraightTrack(), null) };
        var director = new Director();
        director.GoLive(new PlaylistShot(items));

        director.Tick(12f);

        Assert.Equal(items[1].EntryId, director.Playlist!.EntryId);
        Assert.Equal(2.0, director.ShotTime, 4);
        Assert.Equal(10.0, director.ShotLength, 4);
        director.Seek(5.0);
        Assert.Equal(5.0, director.ShotTime, 4);
        director.Tick(10f);
        Assert.True(director.IsFinished);
    }

    [Fact]
    public void ATrackShotHasNoPlaylist()
    {
        var director = new Director();
        director.GoLive(new TrackShot(StraightTrack()));
        Assert.Null(director.Playlist);
        Assert.Equal(10.0, director.ShotLength, 4);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: the build fails because `PlaylistPlayback`, `PlaylistItem`, `PlaylistShot`, `Director.Playlist` and `ShotLength` don't exist.

- [ ] **Step 3: The playback interface and `TrackPlayback`**

`src/Vista.Core/Tracks/IPlayback.cs`:

```csharp
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>A shot the Director plays frame by frame: where it is, how long the current shot is, and moving through it.</summary>
public interface IPlayback
{
    /// <summary>Where the camera is in the current shot, in seconds.</summary>
    double ShotTime { get; }

    /// <summary>The current shot's length in seconds.</summary>
    double ShotLength { get; }

    /// <summary>True once playback has reached its end and holds its last frame.</summary>
    bool IsFinished { get; }

    /// <summary>Moves on by <paramref name="dt"/> seconds and returns the frame.</summary>
    CameraState? Advance(float dt);

    /// <summary>Jumps to <paramref name="time"/> within the current shot.</summary>
    void Seek(double time);

    /// <summary>Goes back to the start.</summary>
    void Restart();
}
```

In `TrackPlayback.cs`, declare `: IPlayback` and add:

```csharp
    /// <summary>The track's length in seconds.</summary>
    public double ShotLength => _evaluator.Duration;
```

- [ ] **Step 4: `PlaylistPlayback`**

`src/Vista.Core/Tracks/PlaylistPlayback.cs`:

```csharp
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>One playlist entry ready to play: its entry, its track in the world, and how many times (null follows the track).</summary>
public sealed record PlaylistItem(Guid EntryId, Track Track, int? Loops);

/// <summary>Plays playlist entries in turn with a cut between them, carrying time over, and holds the last frame at the end.</summary>
public sealed class PlaylistPlayback : IPlayback
{
    private readonly IReadOnlyList<PlaylistItem> items;
    private readonly TrackEvaluator[] evaluators;
    private double clock;

    /// <summary>Plays <paramref name="items"/> from the first; refused when empty.</summary>
    public PlaylistPlayback(IReadOnlyList<PlaylistItem> items)
    {
        if (items.Count == 0) throw new ArgumentException("A playlist needs an entry to play.");
        this.items = items;
        evaluators = items.Select(i => new TrackEvaluator(i.Track)).ToArray();
    }

    /// <summary>The index of the entry playing, among the items.</summary>
    public int Index { get; private set; }

    /// <summary>The Id of the entry playing.</summary>
    public Guid EntryId => items[Index].EntryId;

    /// <summary>True once the last entry has finished; its last frame holds.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>The playing entry's track length in seconds.</summary>
    public double ShotLength => evaluators[Index].Duration;

    /// <summary>Where the camera is in the playing entry's track.</summary>
    public double ShotTime => PlaybackClock.ShotTime(Direction, ShotLength, PassClock);

    /// <summary>Moves on by <paramref name="dt"/>, cutting to later entries as earlier ones finish, and returns the frame.</summary>
    public CameraState? Advance(float dt)
    {
        if (!IsFinished) clock += Math.Max(dt, 0f);

        while (!IsFinished && clock >= Total && (Total > 0 || clock > 0))
        {
            if (Index == items.Count - 1)
            {
                clock = Total;
                IsFinished = true;
                break;
            }

            clock -= Total;
            Index++;

            // A zero-length entry is shown for the frame it's reached on.
            if (Total == 0)
            {
                clock = 0;
                break;
            }
        }

        return evaluators[Index].Evaluate(ShotTime);
    }

    /// <summary>Jumps to <paramref name="time"/> in the playing entry, staying in the loop pass it is on.</summary>
    public void Seek(double time)
    {
        var pass = PassClock;
        var onReturn = PlaybackClock.OnReturnPass(Direction, ShotLength, pass);
        clock = clock - pass + PlaybackClock.ClockFor(Direction, ShotLength, time, onReturn);
        IsFinished = Index == items.Count - 1 && clock >= Total;
    }

    /// <summary>Goes back to the first entry's start.</summary>
    public void Restart()
    {
        Index = 0;
        clock = 0;
        IsFinished = false;
    }

    private PlaybackDirection Direction => items[Index].Track.Direction;

    private double Cycle => PlaybackClock.CycleLength(Direction, ShotLength);

    /// <summary>How long the playing entry plays: N cycles, one cycle, or for good when its track loops with no count.</summary>
    private double Total => items[Index].Loops is { } n ? n * Cycle : items[Index].Track.Loop ? double.PositiveInfinity : Cycle;

    /// <summary>The clock within the loop pass the playing entry is on; a finished entry sits at the end of its last pass.</summary>
    private double PassClock
    {
        get
        {
            var cycle = Cycle;
            if (cycle <= 0) return 0;
            if (!double.IsInfinity(Total) && clock >= Total) return cycle;
            return clock % cycle;
        }
    }
}
```

- [ ] **Step 5: Shots and the Director**

`Shot.cs`:

```csharp
namespace Vista.Core.Tracks;

/// <summary>Something the Director can put on program: a track, a playlist, or the game's own camera.</summary>
public abstract record Shot;

/// <summary>A track shot: play <see cref="Track"/> through its timing curve.</summary>
public sealed record TrackShot(Track Track) : Shot;

/// <summary>A playlist shot: play its entries in turn.</summary>
public sealed record PlaylistShot(IReadOnlyList<PlaylistItem> Items) : Shot;

/// <summary>Hands the camera back to the game.</summary>
public sealed record GameCameraShot : Shot;
```

Delete `SnapPoint.cs` (`git rm src/Vista.Core/Tracks/SnapPoint.cs`).

In `Director.cs`:
- `_playback` becomes `private IPlayback? _playback;`.
- `GoLive` builds it: `var playback = shot switch { TrackShot t => (IPlayback)new TrackPlayback(t.Track), PlaylistShot p => new PlaylistPlayback(p.Items), _ => null };`
- `Tick`'s switch becomes `TrackShot or PlaylistShot => _playback!.Advance(IsPaused ? 0f : dt), _ => null`. The snap case goes, and `FreeCamMotion` may no longer be needed.
- Add:

```csharp
    /// <summary>The current shot's length in seconds; 0 for the game camera or before going live.</summary>
    public double ShotLength => _playback?.ShotLength ?? 0.0;

    /// <summary>The playlist being played, or null for other shots.</summary>
    public PlaylistPlayback? Playlist => _playback as PlaylistPlayback;
```

- Update the summaries that name `TrackShot` alone (`IsFinished`, `ShotTime`, `Seek`) to say "the current track or playlist".

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings. Then `./build.sh`: the plugin must still build (it never used `SnapShot`).

- [ ] **Step 7: Commit**

```bash
git add src/Vista.Core/Tracks/IPlayback.cs src/Vista.Core/Tracks/PlaylistPlayback.cs src/Vista.Core/Tracks/TrackPlayback.cs src/Vista.Core/Tracks/Shot.cs src/Vista.Core/Tracks/Director.cs tests/Vista.Tests/Tracks/PlaylistPlaybackTests.cs tests/Vista.Tests/Tracks/DirectorTests.cs
git commit -m "feat(tracks) play playlists and drop snap shots"
```

(`git rm` has already staged the deletion of `SnapPoint.cs`.)

---

### Task 3: Live plays the playlist

Starts once Tasks 1 and 2 are on `main`.

**Files:**
- Modify: `src/Vista.Core/Session/SessionState.cs`, `src/Vista.Plugin/Session/CameraSession.cs`
- Test: `tests/Vista.Tests/Session/SessionPlaylistTests.cs` (new); existing session tests that go live

**Interfaces:**
- Consumes: `PlaylistEditing.*`, `Scene.Playlist`, `PlaylistEntry` (Task 1); `PlaylistItem`, `PlaylistShot`, `Director.Playlist`, `Director.ShotLength` (Task 2).
- Produces:
  - On `SessionState`:
    - `string? AddToPlaylist(Guid trackId, int? index = null)`, `string? RemoveFromPlaylist(Guid entryId)`, `string? MovePlaylistEntry(int from, int to)`, `string? SetEntryLoops(Guid entryId, int? loops)`
    - `bool CanGoLive`
    - `double ScrubLength`: the playing entry's length while live, otherwise the edited track's duration.
    - `PlaylistEntry? PlayingEntry`: the entry playing while live, else null.
  - Going live (Cue, Play from Off, Restart and Play-after-finish in Live) plays the playlist. It's `Refused` when nothing can play.
  - `CameraSession` passes the same through.

- [ ] **Step 1: Write the failing tests**

Create `tests/Vista.Tests/Session/SessionPlaylistTests.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionPlaylistTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing; Track 1 has points at x = 0, 10, 20 (a 10 s shot at 2 yalms per second); Track 2 has points at x = 0, 4 (2 s).
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddTrack();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));
        return state;
    }

    private static Guid First(SessionState state) => state.Scene.Tracks[0].Id;

    private static Guid Second(SessionState state) => state.Scene.Tracks[1].Id;

    [Fact]
    public void PlaylistEditsAreUndoSteps()
    {
        var state = Editing();
        Assert.Null(state.AddToPlaylist(First(state)));
        Assert.Null(state.AddToPlaylist(Second(state), 0));
        var entry = state.Scene.Playlist[1].Id;
        Assert.Null(state.SetEntryLoops(entry, 2));
        Assert.Null(state.MovePlaylistEntry(1, 0));
        Assert.Null(state.RemoveFromPlaylist(entry));

        Assert.Single(state.Scene.Playlist);
        state.Undo();
        state.Undo();
        Assert.Equal(2, state.Scene.Playlist[1].Loops);
        state.Undo();
        Assert.Null(state.Scene.Playlist[1].Loops);
    }

    [Fact]
    public void PlaylistEditsAreRefusedUnlessEditing()
    {
        var state = Editing();
        state.AddToPlaylist(First(state));
        state.Cue();

        Assert.NotNull(state.AddToPlaylist(First(state)));
        Assert.NotNull(state.RemoveFromPlaylist(state.Scene.Playlist[0].Id));
        Assert.NotNull(state.SetEntryLoops(state.Scene.Playlist[0].Id, 3));
        Assert.Single(state.Scene.Playlist);
    }

    [Fact]
    public void DeletingATrackRemovesItsEntriesInOneStep()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));

        state.DeleteTrack(Second(state));
        Assert.Single(state.Scene.Playlist);

        state.Undo();
        Assert.Equal(2, state.Scene.Playlist.Count);
    }

    [Fact]
    public void LiveIsRefusedWhenNothingCanPlay()
    {
        var state = Editing();
        Assert.False(state.CanGoLive);
        Assert.Equal(PlayOutcome.Refused, state.Cue());

        state.AddTrack();
        state.AddToPlaylist(state.EditedTrackId);
        Assert.False(state.CanGoLive);

        state.AddToPlaylist(First(state));
        Assert.True(state.CanGoLive);
    }

    [Fact]
    public void LiveCuesThePlaylistAtItsFirstPlayableEntryAndPlaysItInTurn()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist(state.EditedTrackId);
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));

        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(2.0, state.ScrubLength, 4);

        state.Play();
        state.Director.Tick(3f);
        Assert.Equal(state.Scene.Playlist[2].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.ScrubHead, 4);
        Assert.Equal(10.0, state.ScrubLength, 4);
    }

    [Fact]
    public void ScrubbingLiveSeeksWithinThePlayingEntry()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(3f);

        state.BeginScrub();
        state.ScrubTo(7.0);
        state.EndScrub();

        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(7.0, state.ScrubHead, 4);
    }

    [Fact]
    public void TheEndHoldsAndPlayStartsAgain()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.Cue();
        state.Play();
        state.Director.Tick(5f);

        Assert.True(state.Director.IsFinished);
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.Equal(2.0, state.ScrubHead, 4);

        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void RestartInLiveGoesBackToTheFirstEntry()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(5f);

        state.Restart();

        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void EditFromLiveTakesTheShotTimeOnlyWhenTheEditedTrackIsPlaying()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(1f);

        state.Edit();
        Assert.Equal(1.0, state.ScrubHead, 4);

        state.SwitchTrack(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(1f);
        state.Edit();
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void PlayingEntryIsNullUnlessLive()
    {
        var state = Editing();
        state.AddToPlaylist(First(state));
        Assert.Null(state.PlayingEntry);
    }
}
```

In `EditFromLiveTakesTheShotTimeOnlyWhenTheEditedTrackIsPlaying`, the edited track is Track 2 after `Editing()`, which is entry 0. After one second it's still playing, so the scrub head takes 1.0. After switching to Track 1, entry 0 (Track 2) plays for one second while Track 1 is the edited track, so the scrub head is 0.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj --filter SessionPlaylistTests`
Expected: the build fails because the playlist members don't exist on `SessionState`.

- [ ] **Step 3: Implement**

In `src/Vista.Core/Session/SessionState.cs`:

1. **`SameValues`** also compares the playlist. Add `|| !a.Playlist.SequenceEqual(b.Playlist)` to its first `return false` condition. Without this, a playlist edit would look like a no-op to `CommitScene` and record no undo step.

2. **Playlist edits:**

```csharp
    /// <summary>Adds an entry for a track at <paramref name="index"/>, or at the end. Returns why it was refused, or null.</summary>
    public string? AddToPlaylist(Guid trackId, int? index = null) => CommitScene(scene => (PlaylistEditing.Add(scene, trackId, index).Scene, EditedTrackId));

    /// <summary>Removes a playlist entry. Returns why it was refused, or null.</summary>
    public string? RemoveFromPlaylist(Guid entryId) => CommitScene(scene => (PlaylistEditing.Remove(scene, entryId), EditedTrackId));

    /// <summary>Moves a playlist entry. Returns why it was refused, or null.</summary>
    public string? MovePlaylistEntry(int from, int to) => CommitScene(scene => (PlaylistEditing.Move(scene, from, to), EditedTrackId));

    /// <summary>Sets how many times an entry plays, or null to follow its track. Returns why it was refused, or null.</summary>
    public string? SetEntryLoops(Guid entryId, int? loops) => CommitScene(scene => (PlaylistEditing.SetLoops(scene, entryId, loops), EditedTrackId));

    /// <summary>True when the playlist has an entry whose track has points.</summary>
    public bool CanGoLive => PlaylistEditing.CanPlay(Scene);

    /// <summary>The entry playing while live, or null.</summary>
    public PlaylistEntry? PlayingEntry
        => Mode == CameraMode.Live && Director.Playlist is { } playing
            ? Scene.Playlist.FirstOrDefault(e => e.Id == playing.EntryId)
            : null;

    /// <summary>The scrub bar's length: the playing entry's while live, otherwise the edited track's.</summary>
    public double ScrubLength => Mode == CameraMode.Live ? Director.ShotLength : Duration;
```

3. **`GoLive`** plays the playlist. Its refusal check and `GoLive` call become:

```csharp
        var items = Scene.Playlist
            .Select(entry => (Entry: entry, Track: SceneEditing.Get(Scene, entry.TrackId)))
            .Where(x => x.Track.Points.Count > 0)
            .Select(x => new PlaylistItem(x.Entry.Id, WorldOf(x.Track), x.Entry.Loops))
            .ToList();
        if (items.Count == 0) return PlayOutcome.Refused;
```

   with `Director.GoLive(new PlaylistShot(items));` in place of the `TrackShot`. Update `GoLive`'s summary ("Goes live with the playlist from its start. Refused when nothing can play."), and `Cue`'s, `Restart`'s and `Play`'s where they say "the track".

4. **`ScrubTo`** clamps to `ScrubLength`: `scrubTime = Math.Clamp(time, 0.0, ScrubLength);`.

5. **`Edit` from Live** (the `CameraMode.Live` case): the scrub head takes the shot time only when the playing entry plays the edited track:

```csharp
                scrubTime = PlayingEntry?.TrackId == EditedTrackId ? Math.Clamp(Director.ShotTime, 0.0, Duration) : 0.0;
```

   This line must run before `Director.GoOffline()` and before `Mode` changes, since `PlayingEntry` needs Live.

6. **Existing tests that go live** now need a playlist. Run the suite. For each failing test that goes live (`Cue()`, `Play()` from Off, or `Restart()`/`Play()` in Live), insert `state.AddToPlaylist(state.EditedTrackId);` at the earliest point after the edited track has its points and before any live edit, undo or scrub the test is about. Shared helpers that go live (such as `Live()` in `SessionStateTests`) take it inside the helper. Change nothing else in those tests. If a test's undo count shifts because of the added step, move the insertion earlier rather than changing an assertion. List every test you change.

Run: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`
Expected: every test passes, 0 warnings.

- [ ] **Step 4: Pass it through `CameraSession`**

In `src/Vista.Plugin/Session/CameraSession.cs`, add:

```csharp
    /// <summary>Adds a playlist entry for a track. Returns why it was refused, or null.</summary>
    public string? AddToPlaylist(Guid trackId, int? index = null) => state.AddToPlaylist(trackId, index);

    /// <summary>Removes a playlist entry. Returns why it was refused, or null.</summary>
    public string? RemoveFromPlaylist(Guid entryId) => state.RemoveFromPlaylist(entryId);

    /// <summary>Moves a playlist entry. Returns why it was refused, or null.</summary>
    public string? MovePlaylistEntry(int from, int to) => state.MovePlaylistEntry(from, to);

    /// <summary>Sets an entry's loop count, or null to follow its track. Returns why it was refused, or null.</summary>
    public string? SetEntryLoops(Guid entryId, int? loops) => state.SetEntryLoops(entryId, loops);

    /// <summary>True when the playlist has something to play.</summary>
    public bool CanGoLive => state.CanGoLive;

    /// <summary>The entry playing while live, or null.</summary>
    public PlaylistEntry? PlayingEntry => state.PlayingEntry;

    /// <summary>The scrub bar's length in seconds.</summary>
    public double ScrubLength => state.ScrubLength;
```

In `Apply(PlayOutcome)`, the `Refused` log becomes `"[vista] nothing to play: add a track with points to the playlist."`. It can come from Live, Play from Off, or a preview of an empty track, so check the preview path's wording still fits. If it doesn't, log per mode, and note the choice in the report.

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Core/Session/SessionState.cs src/Vista.Plugin/Session/CameraSession.cs tests/Vista.Tests/Session/SessionPlaylistTests.cs
git add <each existing test file you changed>
git commit -m "feat(session) play the playlist live"
```

---

### Task 4: The Playlist compartment

Starts once Task 3 is on `main`.

**Files:**
- Create: `src/Vista.Plugin/Ui/PlaylistPanel.cs`
- Modify: `src/Vista.Plugin/Ui/TrackEditorWindow.cs`, `src/Vista.Plugin/Ui/HierarchyPanel.cs`

**Interfaces:**
- Consumes: `CameraSession.AddToPlaylist`, `RemoveFromPlaylist`, `MovePlaylistEntry`, `SetEntryLoops`, `CanGoLive`, `PlayingEntry`, `ScrubLength`, `Scene` (Task 3); `PlaylistEditing.HoldsPlaylist`, `PlaylistEditing.IndexOf`, `SceneEditing.Get` (Task 1).
- Produces: `PlaylistPanel(CameraSession)`, `PlaylistPanel.Width` (`const float` 240), `PlaylistPanel.Draw(bool editing)`.

No Core changes, so no new tests; the build and the checklist cover it.

- [ ] **Step 1: The panel**

`src/Vista.Plugin/Ui/PlaylistPanel.cs`:

```csharp
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui;

/// <summary>The playlist Live plays: add, reorder, remove, set loop counts, and see what's playing.</summary>
internal sealed unsafe class PlaylistPanel
{
    /// <summary>The compartment's width.</summary>
    public const float Width = 240f;

    private const string EntryPayload = "VISTA_ENTRY";

    // The Hierarchy's row payload: the dragged track's index in the scene.
    private const string TrackPayload = "VISTA_TRACK";

    private const uint Amber = 0xFF40C0FF;
    private const float LoopWidth = 44f;

    private readonly CameraSession session;

    public PlaylistPanel(CameraSession session) => this.session = session;

    /// <summary>The header, one row per entry, and + Add; editing is disabled unless in Edit mode.</summary>
    public void Draw(bool editing)
    {
        // Rows can remove or reorder entries, so every row reads this snapshot.
        var scene = session.Scene;
        var playing = session.PlayingEntry;

        ImGui.AlignTextToFramePadding();
        if (playing is { } now)
            ImGui.TextUnformatted($"{PlaylistEditing.IndexOf(scene, now.Id) + 1} / {scene.Playlist.Count} — {SceneEditing.Get(scene, now.TrackId).Name}");
        else
            ImGui.TextUnformatted("Playlist");
        ImGui.Separator();

        ImGui.BeginDisabled(!editing);
        var footer = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("entries", new Vector2(0f, -footer)))
        {
            var held = false;
            for (var i = 0; i < scene.Playlist.Count; i++)
            {
                DrawRow(scene, scene.Playlist[i], i, held, playing?.Id, editing);
                held |= PlaylistEditing.HoldsPlaylist(scene, scene.Playlist[i]);
            }

            // The space under the rows takes a dropped track at the end.
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(ImGui.GetContentRegionAvail().Y, ImGui.GetFrameHeight())));
            DropTarget(scene, scene.Playlist.Count, editing);
        }

        ImGui.EndChild();

        if (ImGui.Button("+ Add")) ImGui.OpenPopup("add-entry");
        if (ImGui.BeginPopup("add-entry"))
        {
            foreach (var track in scene.Tracks)
            {
                using var id = ImRaii.PushId(track.Id.ToString());
                if (ImGui.Selectable(track.Name)) Report(session.AddToPlaylist(track.Id));
            }

            ImGui.EndPopup();
        }

        ImGui.EndDisabled();
    }

    /// <summary>One entry: its number and track, drag to reorder or drop a track on it, its loop cell and its remove button; greyed when never reached.</summary>
    private void DrawRow(Scene scene, PlaylistEntry entry, int index, bool unreachable, Guid? playing, bool editing)
    {
        using var id = ImRaii.PushId(entry.Id.ToString());
        using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f, unreachable);

        var remove = IconButton.Width(FontAwesomeIcon.Times);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var nameWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - LoopWidth - remove - (gap * 2f));
        var name = SceneEditing.Get(scene, entry.TrackId).Name;
        ImGui.Selectable($"{index + 1}  {name}", entry.Id == playing, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(nameWidth, ImGui.GetFrameHeight()));

        if (editing && ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(EntryPayload, new ReadOnlySpan<byte>(&index, sizeof(int)));
            ImGui.TextUnformatted(name);
            ImGui.EndDragDropSource();
        }

        DropTarget(scene, index, editing);

        ImGui.SameLine();
        DrawLoops(scene, entry);

        ImGui.SameLine();
        if (IconButton.Draw("remove", FontAwesomeIcon.Times, "Remove from playlist")) Report(session.RemoveFromPlaylist(entry.Id));
    }

    /// <summary>The loop count: 0 follows the track (— or ∞ when it holds the playlist), otherwise ×N; set when let go.</summary>
    private void DrawLoops(Scene scene, PlaylistEntry entry)
    {
        var value = entry.Loops ?? 0;
        var holds = PlaylistEditing.HoldsPlaylist(scene, entry);
        var format = value > 0 ? "×%d" : holds ? "∞" : "—";
        ImGui.SetNextItemWidth(LoopWidth);
        using (ImRaii.PushColor(ImGuiCol.Text, Amber, value > 0 || holds))
            ImGui.DragInt("##loops", ref value, 0.05f, 0, PlaylistEditing.MaxLoops, format);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Times to play; — follows the track (∞ when the track loops)");
        if (ImGui.IsItemDeactivatedAfterEdit()) Report(session.SetEntryLoops(entry.Id, value == 0 ? null : value));
    }

    /// <summary>Accepts an entry (to reorder) or a Hierarchy track (to add) dropped on the last item, placing it at <paramref name="index"/>.</summary>
    private void DropTarget(Scene scene, int index, bool editing)
    {
        if (!editing || !ImGui.BeginDragDropTarget()) return;

        var entry = ImGui.AcceptDragDropPayload(EntryPayload);
        if (!entry.IsNull && *(int*)entry.Handle->Data is var from && from != index)
            Report(session.MovePlaylistEntry(from, Math.Min(index, scene.Playlist.Count - 1)));

        var track = ImGui.AcceptDragDropPayload(TrackPayload);
        if (!track.IsNull && *(int*)track.Handle->Data is var t && t >= 0 && t < scene.Tracks.Count)
            Report(session.AddToPlaylist(scene.Tracks[t].Id, index));

        ImGui.EndDragDropTarget();
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
```

If a binding overload differs (for example `ImRaii.PushStyle` or `PushColor` with a condition, `DragInt`, `IsItemDeactivatedAfterEdit`, `Dummy`), use the one already used in `src/Vista.Plugin/Ui/` or in the read-only Dalamud clone at `~/code/Dalamud`, and note it in the report. Confirm the Hierarchy's drag payload name and contents in `HierarchyPanel.cs` match `TrackPayload` (the track's index as an `int`).

- [ ] **Step 2: The Hierarchy's "Add to playlist"**

In `HierarchyPanel.cs`, in the track's right-click menu, before Delete, add:

```csharp
            if (ImGui.MenuItem("Add to playlist", string.Empty, ref ticked)) Report(session.AddToPlaylist(track.Id));
```

- [ ] **Step 3: The compartment in the Vista window**

In `TrackEditorWindow.cs`:

1. Add fields `private readonly PlaylistPanel playlist;` and `private bool showPlaylist = true;`, and construct it with `playlist = new PlaylistPanel(session);`.
2. `CompartmentsWidth()` adds `(showPlaylist ? PlaylistPanel.Width + Spacing.X : 0f)`.
3. In `DrawTopRow`, after the Hierarchy's toggle icon, add the Playlist's in the same style:

```csharp
        var listColour = showPlaylist ? (uint?)null : ImGui.GetColorU32(ImGuiCol.Text, 0.4f);
        if (IconButton.Draw("playlist", FontAwesomeIcon.ListOl, showPlaylist ? "Hide playlist" : "Show playlist", listColour))
        {
            showPlaylist = !showPlaylist;
            pendingWidth += showPlaylist ? PlaylistPanel.Width + Spacing.X : -(PlaylistPanel.Width + Spacing.X);
        }

        ImGui.SameLine();
```

4. In `Draw`, the track-editor child leaves room for the Playlist on its right, then the Playlist draws:

```csharp
        var editorWidth = showPlaylist ? -(PlaylistPanel.Width + Spacing.X) : 0f;
        if (ImGui.BeginChild("track-editor", new Vector2(editorWidth, 0f)))
        {
            // …as now…
        }

        ImGui.EndChild();

        if (showPlaylist)
        {
            ImGui.SameLine();
            if (ImGui.BeginChild("playlist", new Vector2(PlaylistPanel.Width, 0f), true)) playlist.Draw(editing);
            ImGui.EndChild();
        }
```

   (A negative width in `BeginChild` means "all but this much".)
5. **The mode drop-down:** Live is disabled when `!session.CanGoLive`, with a tooltip shown even while disabled: "Add a track with points to the playlist". Replace the current `session.Track.Points.Count == 0` condition around the Live item.
6. **The transport:** Play is disabled in Edit mode when the edited track has no points, and in Off or Live when `!session.CanGoLive`:

```csharp
        ImGui.BeginDisabled(session.Mode == CameraMode.Editing ? session.Track.Points.Count == 0 : !session.CanGoLive);
```

7. **The scrub bar** uses `session.ScrubLength` in place of `session.Duration` for its range and its label.
8. Update the doc comments of `DrawModeCombo` and the class for the Playlist and Live playing it.

- [ ] **Step 4: Build**

Run: `./build.sh`
Expected: `Build succeeded`, 0 warnings, 0 errors. Run `dotnet test tests/Vista.Tests/Vista.Tests.csproj`: every test passes.

- [ ] **Step 5: Commit**

```bash
git add src/Vista.Plugin/Ui/PlaylistPanel.cs src/Vista.Plugin/Ui/TrackEditorWindow.cs src/Vista.Plugin/Ui/HierarchyPanel.cs
git commit -m "feat(ui) add the playlist compartment and play it live"
```

---

### After the tasks: the checklist

The controller writes `CHECKLIST-3e.md` (untracked) after the final review, with falsifiable pass conditions, a Notes line each, and keys as words.
