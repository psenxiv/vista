using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Editor;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The selected point's, anchor's or Look At point's number fields and gizmo mode; shown only while one is selected in editing mode.</summary>
internal sealed class PointWindow : Window
{
    private readonly GameSession game;
    private readonly SessionState session;
    private readonly PointGizmo gizmo;
    private (int? Point, AnchorKind? Anchor, Guid Track) shown;
    private float gridWidth;
    private ControlPoint? copied;
    private bool dragging;
    private bool openedLastFrame;

    public PointWindow(GameSession game, PointGizmo gizmo)
        : base("Point###vista-point", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.game = game;
        session = game.State;
        this.gizmo = gizmo;
        RespectCloseHotkey = false;
    }

    /// <summary>Opens while a point or an anchor is selected in editing mode, and applies an unfinished edit when the selection moves.</summary>
    public override void PreOpenCheck()
    {
        var editing = session.Mode == CameraMode.Editing;
        var now = (
            editing ? session.Selection.Point : null,
            editing ? session.Selection.Anchor : null,
            session.EditedTrackId
        );

        // Only the close button clears IsOpen behind our back: open last frame, same selection, now shut.
        if (openedLastFrame && !IsOpen && now == shown)
        {
            session.Selection.Select(null);
            now = (null, null, now.Item3);
        }

        if (now != shown)
            session.EndLiveEdit();
        shown = now;
        IsOpen = now.Item1 is not null || now.Item2 is not null;
        openedLastFrame = IsOpen;
        WindowName = now switch
        {
            (_, AnchorKind.Scene, _) => "Scene anchor###vista-point",
            (_, AnchorKind.Track, _) => "Track anchor###vista-point",
            (_, AnchorKind.LookAt, _) => "Look At point###vista-point",
            ({ } index, _, _) => $"Point {index + 1}###vista-point",
            _ => WindowName,
        };
    }

    /// <summary>Ends a drag in progress, since a closed window never reports the field letting go.</summary>
    public override void OnClose() => LiveDrag.End(session, ref dragging);

    public override void Draw()
    {
        Anchor? anchor = null;
        Vector3? lookAt = null;
        var index = -1;
        if (session.Selection.Anchor == AnchorKind.LookAt)
        {
            if (session.Selection.LookAtInWorld is not { } point)
                return;
            lookAt = point;
        }
        else if (session.Selection.Anchor is not null)
        {
            if (session.Selection.AnchorInWorld is not { } a)
                return;
            anchor = a;
        }
        else if (session.Selection.Point is { } i && i < session.Track.Points.Count)
            index = i;
        else
            return;

        using var style = PoseGrid.Style();
        if (!DrawHeader(index >= 0 ? index : null, rotates: lookAt is null))
            return;
        if (!PoseGrid.BeginGrid())
            return;

        if (lookAt is { } shownLookAt)
            DrawLookAtRows(shownLookAt);
        else if (anchor is { } shownAnchor)
            DrawAnchorRows(shownAnchor);
        else
            DrawPointRows(index, session.Track.Points[index]);

        gridWidth = PoseGrid.EndGrid();
    }

    /// <summary>The shared header, whose copy, paste and delete act only on a point; false once the point is deleted.</summary>
    private bool DrawHeader(int? pointIndex, bool rotates)
    {
        var point = pointIndex is { } i ? session.Track.Points[i] : null;
        var clip = PoseGrid.Header(
            gizmo.Mode,
            gizmo.SetMode,
            rotates,
            gridWidth,
            canCopy: point is not null,
            canPaste: point is not null && copied is not null,
            canDelete: point is not null
        );
        switch (clip)
        {
            case PoseGrid.Clip.Copy when point is { } source:
                copied = source;
                break;
            case PoseGrid.Clip.Paste when copied is { } c && pointIndex is { } target && point is not null:
                Report(session.ReplacePoint(target, c));
                break;
            case PoseGrid.Clip.Delete when pointIndex is not null:
                Report(session.DeleteSelected());
                return false;
        }

        return true;
    }

    /// <summary>The point's position, rotation and FoV rows; pitch and yaw are disabled under Direction of travel and Look At.</summary>
    private void DrawPointRows(int index, ControlPoint point)
    {
        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.ArrowsAlt, "Position");
        for (var axis = 0; axis < PoseGrid.Axes.Length; axis++)
        {
            var (id, name, border) = PoseGrid.Axes[axis];
            var at = axis;
            PointField(
                $"{id}{index}",
                name,
                border,
                index,
                point.Position[at],
                PoseGrid.PositionSpeed,
                "%.2f",
                (p, v) => p with { Position = EditLimits.Coordinate(p.Position, at, v) }
            );
        }

        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.SyncAlt, "Rotation");
        ImGui.BeginDisabled(!TrackAim.UsesPointAim(session.Track.Aim));
        PointField(
            $"pitch{index}",
            "Pitch",
            EditorColours.AxisX,
            index,
            Angles.Degrees(point.Pitch),
            PoseGrid.AngleSpeed,
            "%.1f°",
            (p, v) => p with { Pitch = EditLimits.Pitch(Angles.Radians(v)) }
        );
        PointField(
            $"yaw{index}",
            "Yaw",
            EditorColours.AxisY,
            index,
            Angles.Degrees(EditLimits.Angle(point.Yaw)),
            PoseGrid.AngleSpeed,
            "%.1f°",
            (p, v) => p with { Yaw = EditLimits.Angle(Angles.Radians(v)) }
        );
        ImGui.EndDisabled();
        PointField(
            $"roll{index}",
            "Roll",
            EditorColours.AxisZ,
            index,
            Angles.Degrees(EditLimits.Angle(point.Roll)),
            PoseGrid.AngleSpeed,
            "%.1f°",
            (p, v) => p with { Roll = EditLimits.Angle(Angles.Radians(v)) }
        );

        ImGui.TableNextRow();
        // One live edit, so it lands as a single undo step like a drag on the field would.
        if (PoseGrid.Button("reset-fov", FontAwesomeIcon.History, "Reset to the camera's field of view", enabled: true))
        {
            session.BeginLiveEdit();
            _ = session.PreviewPoint(index, session.Track.Points[index] with { Fov = EditLimits.Fov(game.CameraFov) });
            session.EndLiveEdit();
        }

        PointField(
            $"fov{index}",
            "FoV",
            null,
            index,
            Angles.Degrees(point.Fov),
            PoseGrid.FovSpeed,
            "%.1f°",
            (p, v) => p with { Fov = EditLimits.Fov(Angles.Radians(v)) }
        );
    }

    /// <summary>The anchor's rows: X, Y, Z and Yaw edit it, and the fields it lacks are disabled.</summary>
    private void DrawAnchorRows(Anchor anchor)
    {
        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.ArrowsAlt, "Position");
        for (var axis = 0; axis < PoseGrid.Axes.Length; axis++)
        {
            var (id, name, border) = PoseGrid.Axes[axis];
            var at = axis;
            AnchorField(
                $"anchor-{id}",
                name,
                border,
                anchor.Position[at],
                PoseGrid.PositionSpeed,
                "%.2f",
                (a, v) => a with { Position = EditLimits.Coordinate(a.Position, at, v) }
            );
        }

        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.SyncAlt, "Rotation");
        PoseGrid.Missing("anchor-pitch", "Pitch", EditorColours.AxisX);
        AnchorField(
            "anchor-yaw",
            "Yaw",
            EditorColours.AxisY,
            Angles.Degrees(EditLimits.Angle(anchor.Yaw)),
            PoseGrid.AngleSpeed,
            "%.1f°",
            (a, v) => a with { Yaw = EditLimits.Angle(Angles.Radians(v)) }
        );
        PoseGrid.Missing("anchor-roll", "Roll", EditorColours.AxisZ);

        ImGui.TableNextRow();
        _ = PoseGrid.Button(
            "reset-fov",
            FontAwesomeIcon.History,
            "Reset to the camera's field of view",
            enabled: false
        );
        PoseGrid.Missing("anchor-fov", "FoV", null);
    }

    /// <summary>The Look At point's rows: X, Y and Z move it, and rotation and FoV show "—".</summary>
    private void DrawLookAtRows(Vector3 lookAt)
    {
        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.ArrowsAlt, "Position");
        for (var axis = 0; axis < PoseGrid.Axes.Length; axis++)
        {
            var (id, name, border) = PoseGrid.Axes[axis];
            var at = axis;
            LookAtField($"look-{id}", name, border, lookAt[at], (p, v) => EditLimits.Coordinate(p, at, v));
        }

        ImGui.TableNextRow();
        PoseGrid.Label(FontAwesomeIcon.SyncAlt, "Rotation");
        PoseGrid.Missing("look-pitch", "Pitch", EditorColours.AxisX);
        PoseGrid.Missing("look-yaw", "Yaw", EditorColours.AxisY);
        PoseGrid.Missing("look-roll", "Roll", EditorColours.AxisZ);

        ImGui.TableNextRow();
        _ = PoseGrid.Button(
            "reset-fov",
            FontAwesomeIcon.History,
            "Reset to the camera's field of view",
            enabled: false
        );
        PoseGrid.Missing("look-fov", "FoV", null);
    }

    /// <summary>A point's field: dragging moves the point live, and each drag is one undo step.</summary>
    private void PointField(
        string id,
        string name,
        uint? border,
        int index,
        float value,
        float speed,
        string format,
        Func<ControlPoint, float, ControlPoint> set
    )
    {
        var edited = value;
        var changed = PoseGrid.Field(id, name, border, ref edited, speed, format);
        // Refused once an undo mid-drag has ended the edit; the rest of that drag does nothing.
        LiveDrag.Handle(
            session,
            changed,
            () =>
            {
                if (index < session.Track.Points.Count)
                    _ = session.PreviewPoint(index, set(session.Track.Points[index], edited));
            },
            ref dragging
        );
    }

    /// <summary>An anchor's field: dragging moves it live, carrying what hangs off it, and each drag is one undo step.</summary>
    private void AnchorField(
        string id,
        string name,
        uint border,
        float value,
        float speed,
        string format,
        Func<Anchor, float, Anchor> set
    )
    {
        var edited = value;
        var changed = PoseGrid.Field(id, name, border, ref edited, speed, format);
        LiveDrag.Handle(
            session,
            changed,
            () =>
            {
                if (session.Selection.AnchorInWorld is { } current)
                    _ = session.PreviewAnchor(set(current, edited), carry: true);
            },
            ref dragging
        );
    }

    /// <summary>A Look At point's field: dragging moves it live, and each drag is one undo step.</summary>
    private void LookAtField(string id, string name, uint border, float value, Func<Vector3, float, Vector3> set)
    {
        var edited = value;
        var changed = PoseGrid.Field(id, name, border, ref edited, PoseGrid.PositionSpeed, "%.2f");
        LiveDrag.Handle(
            session,
            changed,
            () =>
            {
                if (session.Selection.LookAtInWorld is { } current)
                    _ = session.PreviewLookAt(set(current, edited));
            },
            ref dragging
        );
    }
}
