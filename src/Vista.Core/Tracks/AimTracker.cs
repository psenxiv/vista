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

    /// <summary>Starts the smoothing afresh and forgets the last good aim, so the next frame lands on the character.</summary>
    public void Reset()
    {
        smoother.Reset();
        lastAim = null;
    }

    private Vector3? Target(Track world, float dt) => world.Aim switch
    {
        AimMode.LookAt when world.LookAtPlaced => world.LookAt,
        AimMode.FollowTarget when CharacterAim(world, targets) is { } aim => smoother.Step(aim, dt, world.Smoothing),
        _ => null,
    };
}
