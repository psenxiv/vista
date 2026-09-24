using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Session;

/// <summary>The session's tracks as they stand in the world: placed by their anchors, Follow points at their characters, and the edited track's evaluator.</summary>
public sealed class WorldView
{
    private readonly SessionState session;
    private readonly NearbyCharacters? aimTargets;
    private readonly AimTracker scrubAim;
    private readonly Dictionary<Guid, (Track Local, Anchor Scene, Track World)> worlds = new();
    private readonly Dictionary<Guid, (Track World, Anchor Frame, Track Shown)> shown = new();
    private Track? evaluatedTrack;
    private TrackEvaluator? evaluator;

    internal WorldView(SessionState session, NearbyCharacters? aimTargets)
    {
        this.session = session;
        this.aimTargets = aimTargets;
        scrubAim = new AimTracker(aimTargets);
    }

    /// <summary>A scene track in the world; the same instance until the track or the scene anchor changes.</summary>
    public Track WorldOf(Track local)
    {
        var scene = session.Scene;
        if (worlds.TryGetValue(local.Id, out var cached) && ReferenceEquals(cached.Local, local) && cached.Scene == scene.Anchor) return cached.World;
        var world = SceneGeometry.InWorld(scene, local);
        worlds[local.Id] = (local, scene.Anchor, world);
        return world;
    }

    /// <summary>A scene track as the editor shows it: a Follow track's point at its character where they stand now; the same instance while neither changes.</summary>
    public Track Shown(Track local)
    {
        var world = WorldOf(local);
        if (local.Points.Count != 1 || FollowFrame(local) is not { } frame) return world;
        if (shown.TryGetValue(local.Id, out var cached) && ReferenceEquals(cached.World, world) && cached.Frame == frame) return cached.Shown;
        var result = world with { Points = [frame.ToWorld(local.Points[0])] };
        shown[local.Id] = (world, frame, result);
        return result;
    }

    /// <summary>The evaluator for the edited track, rebuilt when the track changes.</summary>
    public TrackEvaluator Evaluator
    {
        get
        {
            var world = WorldOf(session.StoredTrack);
            if (!ReferenceEquals(evaluatedTrack, world))
            {
                evaluator = new TrackEvaluator(world);
                evaluatedTrack = world;
            }

            return evaluator!;
        }
    }

    /// <summary>The edited track's frame at <paramref name="time"/> seconds, aimed at its target where it is now, or null with no points.</summary>
    public CameraState? FrameAt(double time)
    {
        var local = session.StoredTrack;
        if (local.Points.Count == 0) return null;
        scrubAim.Reset();
        return scrubAim.Frame(Evaluator, WorldOf(local), time, 0f);
    }

    /// <summary>True when a track in the world watches or follows a named character who isn't found.</summary>
    public bool TargetLost(Track world) => AimTracker.TargetLost(world, aimTargets);

    /// <summary>The aim point on the character a Watch or Follow track in the world names, or null unless found.</summary>
    public Vector3? TargetPoint(Track world) => AimTracker.TargetPoint(world, aimTargets);

    /// <summary>Where a track in the world points its camera, or null for a recorded or path aim.</summary>
    public Vector3? AimPoint(Track world) => AimTracker.AimPoint(world, aimTargets);

    /// <summary>The frame a Follow track's point is stored in: its character where they stand, or null when not a one-point Follow track or not found.</summary>
    internal Anchor? FollowFrame(Track local) => FollowFrame(local, WorldOf(local));

    /// <summary>The frame a Follow track's point is stored in, for a track in <paramref name="scene"/>.</summary>
    internal Anchor? FollowFrame(Scene scene, Track local) => FollowFrame(local, SceneGeometry.InWorld(scene, local));

    /// <summary>Forgets every cached world and shown track, as opening a scene does.</summary>
    internal void Clear()
    {
        worlds.Clear();
        shown.Clear();
    }

    /// <summary>The frame a Follow track's point is stored in, finding its character near <paramref name="world"/>'s anchor.</summary>
    private Anchor? FollowFrame(Track local, Track world)
        => local is { Aim: AimMode.FollowTarget, Points.Count: <= 1 } && AimTracker.Character(world, aimTargets) is { } c ? new Anchor(c.Position, c.Facing) : null;
}
