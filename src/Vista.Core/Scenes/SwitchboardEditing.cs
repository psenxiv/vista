using Vista.Core.Editing;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Edits a scene's switchboard: assign, rename and clear a slot, set a toggle, and keep Program, Next and resume points in step with the slots.</summary>
public static class SwitchboardEditing
{
    /// <summary>How many slots a switchboard has.</summary>
    public const int SlotCount = 10;

    /// <summary>Why a slot index can't be used: it is outside the switchboard.</summary>
    public const string NoSuchSlot = "There is no such slot.";

    /// <summary>Why an id can't be assigned: no track or playlist has it.</summary>
    public const string NoSuchTarget = "There is no such track or playlist.";

    /// <summary>Why an empty slot can't be renamed.</summary>
    public const string EmptySlot = "That slot is empty.";

    /// <summary>The hint on an empty slot.</summary>
    public const string AssignHint = "Right-click to assign";

    /// <summary>The hint on a slot holding something that can't play.</summary>
    public const string NothingToPlayHint = "Nothing to play yet";

    /// <summary>The hint on every slot while the scene doesn't use its switchboard.</summary>
    public const string OffHint = "Switchboard is off";

    /// <summary>The Use switchboard toggle's tooltip.</summary>
    public const string UseTooltip = "Use switchboard";

    /// <summary>The Use switchboard toggle's tooltip where it can't be changed.</summary>
    public const string UseTooltipOutsideEdit = "Use switchboard (toggle in Edit)";

    /// <summary>A switchboard with every slot empty, every toggle off, nothing on air, and not in use.</summary>
    public static Switchboard Empty() => new(new Slot?[SlotCount], false, false, false, EmptyAir());

    /// <summary>Nothing on Program or Next, at 0 s, and no resume points.</summary>
    public static OnAir EmptyAir() => new(null, null, 0.0, new double?[SlotCount]);

    /// <summary>Points slot <paramref name="slot"/> at the track or playlist <paramref name="id"/>, following its name.</summary>
    public static Scene Assign(Scene scene, int slot, Guid id)
    {
        RequireSlot(slot);
        var assigned =
            SceneEditing.TryGet(scene, id, out _) ? new Slot(null, id, null)
            : PlaylistEditing.TryGet(scene, id, out _) ? new Slot(null, null, id)
            : throw new ArgumentException(NoSuchTarget);
        return WithSlot(scene, slot, assigned);
    }

    /// <summary>Gives slot <paramref name="slot"/> the trimmed <paramref name="name"/> as its own, or has it follow its track or playlist when blank or the same as theirs; a too long name and an empty slot are refused.</summary>
    public static Scene Rename(Scene scene, int slot, string name)
    {
        RequireSlot(slot);
        if (SceneNames.TooLongRefusal(name) is { } refusal)
            throw new ArgumentException(refusal);
        var held = scene.Switchboard.Slots[slot] ?? throw new ArgumentException(EmptySlot);
        return WithSlot(scene, slot, held with { Name = OwnName(name, TargetName(scene, held)) });
    }

    /// <summary>The name a slot shows: its own, or its track or playlist's.</summary>
    public static string NameOf(Scene scene, Slot slot) => slot.Name ?? TargetName(scene, slot);

    /// <summary>The trimmed <paramref name="name"/> as a slot's own, or null when it is blank or <paramref name="targetName"/>, so the slot follows.</summary>
    private static string? OwnName(string name, string targetName)
    {
        var trimmed = name.Trim();
        return trimmed.Length == 0 || trimmed == targetName ? null : trimmed;
    }

    /// <summary>What to tell someone hovering slot <paramref name="slot"/>: to turn the switchboard on while it is off, how to fill an empty one, that a held one can't play, or null when it can.</summary>
    public static string? Hint(Scene scene, int slot) =>
        !scene.Switchboard.Enabled ? OffHint
        : scene.Switchboard.Slots[slot] is null ? AssignHint
        : CanPlay(scene, slot) ? null
        : NothingToPlayHint;

    /// <summary>Empties slot <paramref name="slot"/>.</summary>
    public static Scene Clear(Scene scene, int slot)
    {
        RequireSlot(slot);
        return WithSlot(scene, slot, null);
    }

    /// <summary>Turns <paramref name="toggle"/> on or off.</summary>
    public static Scene SetToggle(Scene scene, SwitchboardToggle toggle, bool on)
    {
        var board = scene.Switchboard;
        var changed = toggle switch
        {
            SwitchboardToggle.DirectCut => board with { DirectCut = on },
            SwitchboardToggle.KeepRolling => board with { KeepRolling = on },
            SwitchboardToggle.AutoNext => board with { AutoNext = on },
            _ => throw new ArgumentOutOfRangeException(nameof(toggle), toggle, null),
        };
        return changed == board ? scene : scene with { Switchboard = changed };
    }

    /// <summary>Sets whether the scene uses its switchboard.</summary>
    public static Scene SetEnabled(Scene scene, bool on) =>
        scene.Switchboard.Enabled == on ? scene : scene with { Switchboard = scene.Switchboard with { Enabled = on } };

    /// <summary>Empties every slot pointing at the track or playlist <paramref name="id"/>.</summary>
    public static Scene Forget(Scene scene, Guid id) =>
        WithSlots(scene, [.. scene.Switchboard.Slots.Select(s => Target(s) == id ? null : s)]);

    /// <summary>Live after the slots change from <paramref name="before"/> to <paramref name="after"/>: a slot emptied loses Program, Next and its resume point; one on another target loses Program and its resume point.</summary>
    public static OnAir Follow(OnAir onAir, IReadOnlyList<Slot?> before, IReadOnlyList<Slot?> after)
    {
        var (program, next, time) = (onAir.Program, onAir.Next, onAir.ProgramTime);
        var resume = onAir.Resume.ToArray();
        for (var i = 0; i < SlotCount; i++)
        {
            if (Target(before[i]) == Target(after[i]))
                continue;
            resume[i] = null;
            if (program == i)
                (program, time) = (null, 0.0);
            if (after[i] is null && next == i)
                next = null;
        }

        return program != onAir.Program || next != onAir.Next || !resume.SequenceEqual(onAir.Resume)
            ? new OnAir(program, next, time, resume)
            : onAir;
    }

    /// <summary>True when slot <paramref name="slot"/> holds a track with points, or a playlist with an entry whose track has points.</summary>
    public static bool CanPlay(Scene scene, int slot) =>
        slot is >= 0 and < SlotCount
        && scene.Switchboard.Slots[slot] is { } held
        && (
            held.TrackId is { } trackId
                ? TrackEditing.CanPlay(SceneEditing.Get(scene, trackId))
                : PlaylistEditing.CanPlay(scene, PlaylistEditing.Get(scene, held.PlaylistId!.Value))
        );

    /// <summary>True when slot <paramref name="slot"/> holds a track with Loop on, or a playlist with Loop playlist on.</summary>
    public static bool Loops(Scene scene, int slot) =>
        slot is >= 0 and < SlotCount
        && scene.Switchboard.Slots[slot] is { } held
        && (
            held.TrackId is { } trackId
                ? SceneEditing.Get(scene, trackId).Loop
                : PlaylistEditing.Get(scene, held.PlaylistId!.Value).Loops
        );

    /// <summary>The track or playlist a slot points at, or null for an empty one.</summary>
    public static Guid? Target(Slot? slot) => slot?.TrackId ?? slot?.PlaylistId;

    /// <summary>The name of the track or playlist a slot points at.</summary>
    private static string TargetName(Scene scene, Slot slot) =>
        slot.TrackId is { } trackId
            ? SceneEditing.Get(scene, trackId).Name
            : PlaylistEditing.Get(scene, slot.PlaylistId!.Value).Name;

    /// <summary>Refuses a slot index outside the switchboard.</summary>
    private static void RequireSlot(int slot)
    {
        if (slot is < 0 or >= SlotCount)
            throw new ArgumentException(NoSuchSlot);
    }

    /// <summary>The scene with <paramref name="slot"/> holding <paramref name="held"/>.</summary>
    private static Scene WithSlot(Scene scene, int slot, Slot? held) =>
        WithSlots(scene, ListEdit.Replace(scene.Switchboard.Slots, slot, held));

    /// <summary>The scene with <paramref name="slots"/>, and Live following them; the same scene when nothing changed.</summary>
    private static Scene WithSlots(Scene scene, IReadOnlyList<Slot?> slots)
    {
        var board = scene.Switchboard;
        return slots.SequenceEqual(board.Slots)
            ? scene
            : scene with
            {
                Switchboard = board with { Slots = slots, Live = Follow(board.Live, board.Slots, slots) },
            };
    }
}
