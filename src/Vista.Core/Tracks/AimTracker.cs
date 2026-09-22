using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Aims a track in the world each frame: at its Look At point or its eased character, keeping the last good aim when the target is on the camera, or rides a Follow track with its character.</summary>
public sealed class AimTracker
{
    private readonly IAimTargets? targets;
    private readonly AimSmoother smoother = new();
    private (float Yaw, float Pitch)? lastAim;
    private readonly AimSmoother positionSmoother = new();
    private float? heldFacing;
    private CameraState? lastFollow;

    /// <summary>A tracker finding followed characters with <paramref name="targets"/>; with none, no character is ever found.</summary>
    public AimTracker(IAimTargets? targets) => this.targets = targets;

    /// <summary>The character a Watch or Follow track in the world names, found nearest its anchor; null unless named and found.</summary>
    public static LoadedCharacter? Character(Track world, IAimTargets? targets)
        => world is { Aim: AimMode.WatchTarget or AimMode.FollowTarget, TargetName: { } name } ? targets?.FindCharacter(name, world.TargetWorld, world.Anchor.Position) : null;

    /// <summary>The aim point on the character a Watch track in the world watches, unsmoothed; null unless one is named and found.</summary>
    public static Vector3? CharacterAim(Track world, IAimTargets? targets)
        => world.Aim == AimMode.WatchTarget && Character(world, targets) is { } character
            ? character.Position + (Vector3.UnitY * world.AimHeight)
            : null;

    /// <summary>True when a Watch or Follow track in the world names a character who isn't found.</summary>
    public static bool TargetLost(Track world, IAimTargets? targets)
        => world is { Aim: AimMode.WatchTarget or AimMode.FollowTarget, TargetName: not null } && Character(world, targets) is null;

    /// <summary>The frame of <paramref name="world"/> at <paramref name="time"/>, <paramref name="dt"/> seconds after the last.</summary>
    public CameraState? Frame(TrackEvaluator evaluator, Track world, double time, float dt)
    {
        if (world.Aim == AimMode.FollowTarget && world.Points.Count == 1) return Follow(world, dt);
        var target = Target(world, dt);
        if (evaluator.Evaluate(time, target) is not { } frame) return null;

        // Lost, the smoother waits on the recorded aim, so finding the character again eases from it.
        if (world.Aim == AimMode.WatchTarget && target is null) smoother.Seed(frame.LookAt);
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
        positionSmoother.Reset();
        heldFacing = null;
        lastFollow = null;
    }

    /// <summary>A Follow Target frame: the offset from the character, turned with them or held, looking at them or along its recorded aim.</summary>
    private CameraState Follow(Track world, float dt)
    {
        var offset = world.Anchor.ToLocal(world.Points[0]);
        if (Character(world, targets) is not { } character)
        {
            var fallback = world.Points[0];
            return lastFollow ?? new CameraState(fallback.Position, FreeCamMotion.LookAtFrom(fallback.Position, fallback.Yaw, fallback.Pitch), offset.Fov, offset.Roll);
        }

        var facing = world.FollowTurns ? character.Facing : heldFacing ??= character.Facing;
        var at = new Anchor(character.Position, facing).ToWorld(offset);
        var position = positionSmoother.Step(at.Position, dt, world.Smoothing);
        var look = FreeCamMotion.LookAtFrom(position, at.Yaw, at.Pitch);
        if (world.FollowLooks && TrackAim.Toward(position, smoother.Step(character.Position + (Vector3.UnitY * world.AimHeight), dt, world.Smoothing)) is { } aim)
            look = FreeCamMotion.LookAtFrom(position, aim.Yaw, aim.Pitch);

        lastFollow = new CameraState(position, look, offset.Fov, offset.Roll);
        return lastFollow.Value;
    }

    private Vector3? Target(Track world, float dt) => world.Aim switch
    {
        AimMode.LookAt when world.LookAtPlaced => world.LookAt,
        AimMode.WatchTarget when CharacterAim(world, targets) is { } aim => smoother.Step(aim, dt, world.Smoothing),
        _ => null,
    };
}
