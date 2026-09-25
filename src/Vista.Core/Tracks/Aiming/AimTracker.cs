using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks.Aiming;

/// <summary>Aims a track in the world each frame: at its Look At point or its eased character, keeping the last good aim when the target is on the camera, or rides a Follow track with its character.</summary>
public sealed class AimTracker
{
    private readonly NearbyCharacters? targets;
    private readonly AimSmoother smoother = new();
    private (float Yaw, float Pitch)? lastAim;
    private readonly AimSmoother positionSmoother = new();
    private float? heldFacing;
    private float? easedYaw;
    private CameraState? lastFollow;
    private (Vector3 Facing, Vector3 Up, float Way)? carried;

    /// <summary>A tracker finding watched or followed characters with <paramref name="targets"/>; with none, no character is ever found.</summary>
    public AimTracker(NearbyCharacters? targets) => this.targets = targets;

    /// <summary>The character a Watch or Follow track in the world names, found nearest its anchor; null unless named and found.</summary>
    public static LoadedCharacter? Character(Track world, NearbyCharacters? targets) =>
        world is { Aim: AimMode.WatchTarget or AimMode.FollowTarget, TargetName: { } name }
            ? targets?.FindCharacter(name, world.TargetWorld, world.Anchor.Position)
            : null;

    /// <summary>The aim point on the character a Watch track in the world watches, unsmoothed; null unless one is named and found.</summary>
    public static Vector3? CharacterAim(Track world, NearbyCharacters? targets) =>
        world.Aim == AimMode.WatchTarget ? TargetPoint(world, targets) : null;

    /// <summary>The aim point on the character a Watch or Follow track in the world names: their feet plus the aim height; null unless found.</summary>
    public static Vector3? TargetPoint(Track world, NearbyCharacters? targets) =>
        Character(world, targets) is { } character ? character.Position + (Vector3.UnitY * world.AimHeight) : null;

    /// <summary>Where a track in the world points its camera: its Look At point, or its character's aim point when it watches them or follows looking at them; null for a recorded or path aim.</summary>
    public static Vector3? AimPoint(Track world, NearbyCharacters? targets) =>
        world switch
        {
            _ when world.UsesLookAt => world.LookAt,
            { Aim: AimMode.WatchTarget } or { Aim: AimMode.FollowTarget, FollowLooks: true } => TargetPoint(
                world,
                targets
            ),
            _ => null,
        };

    /// <summary>True when a Watch or Follow track in the world names a character who isn't found.</summary>
    public static bool TargetLost(Track world, NearbyCharacters? targets) =>
        world is { Aim: AimMode.WatchTarget or AimMode.FollowTarget, TargetName: not null }
        && Character(world, targets) is null;

    /// <summary>The frame of <paramref name="world"/> at <paramref name="time"/>, <paramref name="dt"/> seconds after the last.</summary>
    public CameraState? Frame(TrackEvaluator evaluator, Track world, double time, float dt)
    {
        if (world.Aim == AimMode.FollowTarget && world.Points.Count == 1)
            return Follow(world, dt);
        var target = Target(world, dt);
        if (evaluator.Evaluate(time, target) is not { } frame)
            return null;

        // Lost, the smoother waits on the recorded aim, so finding the character again eases from it.
        if (world.Aim == AimMode.WatchTarget && target is null)
            smoother.Seed(frame.LookAt);
        if (target is not { } at)
            return Carry(frame, dt, settle: false);

        // A Look At track's up comes from the evaluator; a watched character moves live, so it's carried here.
        var watching = world.Aim == AimMode.WatchTarget;
        if (TrackAim.Toward(frame.Position, at) is not null)
        {
            lastAim = CameraRotation.YawPitch(frame.Forward);
            return Carry(frame, dt, watching);
        }

        return Carry(
            lastAim is { } aim
                ? CameraState.FromAngles(frame.Position, aim.Yaw, aim.Pitch, frame.Roll, frame.Fov)
                : frame,
            dt,
            watching
        );
    }

    /// <summary>Starts the smoothing afresh and forgets the last good aim, so the next frame lands on the character.</summary>
    public void Reset()
    {
        smoother.Reset();
        lastAim = null;
        carried = null;
        positionSmoother.Reset();
        heldFacing = null;
        easedYaw = null;
        lastFollow = null;
    }

    /// <summary>A Follow Target frame: the offset from the character, turned with them or held, looking at them or along its recorded aim.</summary>
    private CameraState Follow(Track world, float dt)
    {
        var offset = world.Anchor.ToLocal(world.Points[0]);
        if (Character(world, targets) is not { } character)
        {
            var fallback = world.Points[0];
            return lastFollow
                ?? CameraState.FromAngles(fallback.Position, fallback.Yaw, fallback.Pitch, offset.Roll, offset.Fov);
        }

        var facing = world.FollowTurns ? character.Facing : heldFacing ??= character.Facing;
        var at = new Anchor(character.Position, facing).ToWorld(offset);
        var position = positionSmoother.Step(at.Position, dt, world.Smoothing);
        var (yaw, pitch) = (EaseYaw(at.Yaw, dt, world.Smoothing), at.Pitch);
        if (
            world.FollowLooks
            && TrackAim.Toward(
                position,
                smoother.Step(character.Position + (Vector3.UnitY * world.AimHeight), dt, world.Smoothing)
            )
                is { } aim
        )
            (yaw, pitch) = aim;

        lastFollow = Carry(
            CameraState.FromAngles(position, yaw, pitch, offset.Roll, offset.Fov),
            dt,
            world.FollowLooks
        );
        return lastFollow.Value;
    }

    /// <summary>A watching frame's up carried on from the last frame and settled toward its own upright up, so passing under or over the character turns the picture round rather than flipping it; other frames pass through and are remembered.</summary>
    private CameraState Carry(CameraState frame, float dt, bool settle)
    {
        var facing = frame.Forward;
        var way = 1f;
        if (settle && carried is { } last)
        {
            (var up, way) = LiveUp.SettleToward(
                LiveUp.Carry(last.Facing, facing, last.Up),
                facing,
                frame.Up,
                dt,
                last.Way
            );
            frame = frame with { Up = up };
        }

        carried = (facing, frame.Up, way);
        return frame;
    }

    /// <summary>Eases the Follow yaw the short way round towards <paramref name="target"/>, with the smoother's time constant.</summary>
    private float EaseYaw(float target, float dt, float smoothing)
    {
        if (easedYaw is not { } from)
            return (easedYaw = target).Value;
        if (dt <= 0f)
            return from;
        var delta = Angles.Delta(from, target);
        var timeConstant = Math.Clamp(smoothing, 0f, 1f) * AimSmoother.SecondsPerSmoothing;
        var next = timeConstant <= 0f ? from + delta : from + (delta * (1f - MathF.Exp(-dt / timeConstant)));
        easedYaw = next;
        return next;
    }

    private Vector3? Target(Track world, float dt) =>
        world.Aim switch
        {
            AimMode.LookAt when world.LookAtPlaced => world.LookAt,
            AimMode.WatchTarget when CharacterAim(world, targets) is { } aim => smoother.Step(aim, dt, world.Smoothing),
            _ => null,
        };
}
