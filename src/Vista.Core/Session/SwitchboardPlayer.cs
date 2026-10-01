using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Tracks.Playback;

namespace Vista.Core.Session;

/// <summary>Cuts one Director between the scene's switchboard slots: Next, Cut, where a shot starts and what happens at its end.</summary>
public sealed class SwitchboardPlayer : IPlayingShot
{
    private readonly SessionState session;
    private readonly Director director;
    private readonly bool remembers;
    private readonly Func<OnAir> readAir;
    private readonly Action<OnAir> writeAir;
    private readonly Action? onCut;

    // The slot the Director plays and the track or playlist it held then; null while it plays nothing.
    private (int Slot, Guid Target)? playing;

    /// <summary>A player for <paramref name="session"/>'s scene on <paramref name="director"/>, keeping its place through <paramref name="readAir"/> and <paramref name="writeAir"/>; <paramref name="remembers"/> records resume points on each cut for Keep rolling to use, and <paramref name="onCut"/> runs as each cut starts.</summary>
    internal SwitchboardPlayer(
        SessionState session,
        Director director,
        bool remembers,
        Func<OnAir> readAir,
        Action<OnAir> writeAir,
        Action? onCut = null
    )
    {
        this.session = session;
        this.director = director;
        this.remembers = remembers;
        this.readAir = readAir;
        this.writeAir = writeAir;
        this.onCut = onCut;
    }

    /// <summary>The slot on Program, or null.</summary>
    public int? Program => readAir().Program;

    /// <summary>The slot that goes live on Cut, or null.</summary>
    public int? Next => readAir().Next;

    /// <summary>The slot whose shot is playing, or null when nothing is.</summary>
    public Slot? ProgramSlot => playing is { } p && OnProgram ? session.Scene.Switchboard.Slots[p.Slot] : null;

    /// <summary>True while a shot is on Program.</summary>
    public bool HasProgram => OnProgram;

    /// <summary>True while the Program shot is paused.</summary>
    public bool IsPaused => OnProgram && director.IsPaused;

    /// <summary>True once the Program shot has reached its end and holds its last frame.</summary>
    public bool IsFinished => OnProgram && director.IsFinished;

    /// <summary>True while the Program shot runs, neither paused nor finished.</summary>
    public bool IsPlaying => OnProgram && director.IsPlaying;

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing => OnProgram && director.Scrubbing;

    /// <summary>The Program shot laid end to end, or null when nothing is on Program.</summary>
    public PlaylistTimeline? Timeline => OnProgram ? director.Timeline : null;

    /// <summary>Seconds through the Program shot; 0 when nothing is on Program.</summary>
    public double Head => OnProgram ? director.Head : 0.0;

    /// <summary>The playing entry's index among the Program shot's segments; 0 when nothing is on Program.</summary>
    public int EntryIndex => OnProgram ? director.EntryIndex : 0;

    /// <summary>True when slot <paramref name="slot"/> holds a track with points, or a playlist with an entry whose track has points.</summary>
    public bool CanPlay(int slot) => SwitchboardEditing.CanPlay(session.Scene, slot);

    /// <summary>With Direct cut, cuts to slot <paramref name="slot"/>; without, makes it Next, or empties Next when it already is; any other slot that is empty or can't play does nothing.</summary>
    public void Click(int slot)
    {
        Sync();
        var directCut = session.Scene.Switchboard.DirectCut;
        if (!directCut && readAir().Next == slot)
            writeAir(readAir() with { Next = null });
        else if (!CanPlay(slot))
            return;
        else if (directCut)
            CutTo(slot, emptyNext: false);
        else
            writeAir(readAir() with { Next = slot });
    }

    /// <summary>Puts Next on Program, empties Next and plays it; nothing happens with Next empty or unable to play.</summary>
    public void Cut()
    {
        Sync();
        if (readAir().Next is { } next && CanPlay(next))
            CutTo(next, emptyNext: true);
    }

    /// <summary>Advances the Program shot by <paramref name="dt"/>, cutting to Next with Auto Next as this tick plays it to its end; returns its frame, or null with nothing on Program.</summary>
    public CameraState? Tick(float dt)
    {
        Sync();
        if (playing is null)
            return null;
        var wasFinished = director.IsFinished;
        var frame = director.Tick(dt);
        if (
            !wasFinished
            && director.IsFinished
            && session.Scene.Switchboard.AutoNext
            && readAir().Next is { } next
            && CanPlay(next)
        )
            CutTo(next, emptyNext: true);
        return frame;
    }

    /// <summary>Plays the Program shot from its start.</summary>
    public void Restart()
    {
        Sync();
        director.Restart();
    }

    /// <summary>Plays the Program shot: on from a pause, or from its start once it has finished.</summary>
    public void Play()
    {
        Sync();
        director.Play();
    }

    /// <summary>Holds the Program shot's frame.</summary>
    public void Pause()
    {
        Sync();
        director.Pause();
    }

    /// <summary>Carries on playing a paused Program shot.</summary>
    public void Resume()
    {
        Sync();
        if (playing is not null)
            director.Resume();
    }

    /// <summary>Starts dragging the head; the Program shot holds until <see cref="EndScrub"/>.</summary>
    public void BeginScrub()
    {
        Sync();
        director.BeginScrub();
    }

    /// <summary>Moves the head to <paramref name="time"/> through the Program shot, clamped to it.</summary>
    public void ScrubTo(double time)
    {
        Sync();
        director.ScrubTo(time);
    }

    /// <summary>Stops dragging the head; the Program shot carries on as it was.</summary>
    public void EndScrub()
    {
        Sync();
        director.EndScrub();
    }

    /// <summary>Puts the Program shot back where it had got to, paused, or at its start if it had finished; a Program that can't play empties.</summary>
    internal void Restore()
    {
        Stop();
        var air = readAir();
        if (air.Program is not { } program)
            return;
        if (ShotOf(program) is not { } found)
        {
            writeAir(air with { Program = null, ProgramTime = 0.0 });
            return;
        }

        Play(program, found);
        director.ScrubTo(air.ProgramTime);
        if (director.IsFinished)
            director.Restart();
        director.Pause();
    }

    /// <summary>Records where the Program shot had got to, then stops it.</summary>
    internal void Store()
    {
        Sync();
        var air = readAir();
        var time = Head;
        if (time != air.ProgramTime)
            writeAir(air with { ProgramTime = time });
        Stop();
    }

    /// <summary>True while the Director plays the slot on Program and that slot still holds what it held when cut to.</summary>
    private bool OnProgram =>
        playing is { } p
        && readAir().Program == p.Slot
        && SwitchboardEditing.Target(session.Scene.Switchboard.Slots[p.Slot]) == p.Target;

    /// <summary>Stops the Director once Program has left the slot it plays.</summary>
    private void Sync()
    {
        if (playing is not null && !OnProgram)
            Stop();
    }

    private void Stop()
    {
        director.GoOffline();
        playing = null;
    }

    /// <summary>Cuts to slot <paramref name="slot"/>, which can play: records the outgoing shot's resume point and, with Keep rolling, starts the incoming one from its own.</summary>
    private void CutTo(int slot, bool emptyNext)
    {
        var found = ShotOf(slot)!.Value;
        var air = readAir();
        var resume = air.Resume.ToArray();
        if (remembers && playing is { } outgoing)
            resume[outgoing.Slot] = director.IsFinished ? null : director.Head;

        onCut?.Invoke();
        Play(slot, found);
        if (remembers && session.Scene.Switchboard.KeepRolling && resume[slot] is { } from)
        {
            director.ScrubTo(from);
            if (director.IsFinished)
                director.Restart();
        }

        writeAir(new OnAir(slot, emptyNext ? null : air.Next, director.Head, resume));
    }

    /// <summary>Puts <paramref name="found"/>'s shot on the Director from its start, unpaused, as slot <paramref name="slot"/>'s.</summary>
    private void Play(int slot, (PlaylistShot Shot, Guid Target) found)
    {
        director.GoLive(found.Shot);
        playing = (slot, found.Target);
    }

    /// <summary>Slot <paramref name="slot"/>'s shot in the world and the track or playlist it plays, or null when it can't play.</summary>
    private (PlaylistShot Shot, Guid Target)? ShotOf(int slot)
    {
        var scene = session.Scene;
        if (!SwitchboardEditing.CanPlay(scene, slot))
            return null;
        var held = scene.Switchboard.Slots[slot]!;
        if (held.TrackId is { } trackId)
        {
            var track = SceneEditing.Get(scene, trackId);
            return (new PlaylistShot([new PlaylistItem(trackId, session.World.WorldOf(track), null)]), trackId);
        }

        var playlist = PlaylistEditing.Get(scene, held.PlaylistId!.Value);
        return (new PlaylistShot(session.PlaylistItems(playlist), playlist.Loops), playlist.Id);
    }
}
