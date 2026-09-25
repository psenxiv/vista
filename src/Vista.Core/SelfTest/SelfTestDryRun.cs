#if DEBUG
using System.Globalization;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Tracks.Playback;

namespace Vista.Core.SelfTest;

/// <summary>The scene dry run: plays <paramref name="scene"/>'s playlist once through, checking each frame written was well-formed and read back exactly.</summary>
public sealed class SelfTestDryRun(Scene scene)
{
    /// <summary>How many times faster than real time the dry run plays; provisional.</summary>
    public const float Speed = 10f;

    private const string Name = "scene dry run";

    /// <summary>The result when nothing in the playlist can play.</summary>
    public static SelfTestResult Skipped { get; } = SelfTestResult.Skip(Name, "nothing in the playlist can play");

    /// <summary>What the dry run plays: each of <paramref name="items"/> once for one pass, the playlist not looping; null when there are none.</summary>
    public static PlaylistShot? Shot(IReadOnlyList<PlaylistItem> items) =>
        items.Count == 0 ? null : new PlaylistShot([.. items.Select(item => item with { Loops = 1 })]);

    /// <summary>How many frames have been checked.</summary>
    public int Frames { get; private set; }

    /// <summary>The first failing frame's entry, time and broken rule, or null while none has failed.</summary>
    public string? FirstFailure { get; private set; }

    /// <summary>Checks a frame of entry <paramref name="entryId"/> at <paramref name="time"/> seconds into its track: well-formed, and <paramref name="read"/> back as <paramref name="written"/>.</summary>
    public void Check(Guid entryId, double time, CameraState written, CameraState read)
    {
        Frames++;
        if (FirstFailure is not null)
            return;
        if ((WellFormed.FirstBroken(written) ?? SelfTestRules.ReadBackMismatch(written, read)) is { } rule)
            FirstFailure = string.Create(
                CultureInfo.InvariantCulture,
                $"{EntryLabel(entryId)} at {time:0.00} s: {rule}"
            );
    }

    /// <summary>The check's result; <paramref name="finished"/> says the playlist played to its end.</summary>
    public SelfTestResult Result(bool finished) =>
        FirstFailure is { } failure ? SelfTestResult.Fail(Name, $"{Counted} checked; first failure at {failure}")
        : !finished ? SelfTestResult.Fail(Name, $"{Counted} checked; the camera hook stopped before the end")
        : Frames == 0 ? SelfTestResult.Fail(Name, "no frames checked")
        : SelfTestResult.Pass(Name, $"{Counted} well-formed and read back exactly");

    private string Counted => Frames == 1 ? "1 frame" : $"{Frames} frames";

    /// <summary>Entry <paramref name="entryId"/> as the player sees it: its place in the playlist and its track's name.</summary>
    private string EntryLabel(Guid entryId)
    {
        var index = PlaylistEditing.IndexOf(scene, entryId);
        return $"entry {index + 1} ({SceneEditing.Get(scene, scene.Playlist[index].TrackId).Name})";
    }
}
#endif
