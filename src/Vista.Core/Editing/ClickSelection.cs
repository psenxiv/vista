using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Editing;

/// <summary>Tells a click from a drag and turns clicks on markers or empty space into selection changes.</summary>
public sealed class ClickSelection
{
    /// <summary>Pixels the cursor may move between press and release and still count as a click.</summary>
    public const float MaxTravel = 4f;

    /// <summary>Radians the camera may turn between press and release and still count as a click; the game locks the cursor while it turns.</summary>
    public const float MaxTurn = 0.01f;

    private bool down;
    private bool ignored;
    private bool travelled;
    private Vector2 start;
    private (float Yaw, float Pitch) startLook;
    private int? marker;

    /// <summary>True while a press that started on a marker is held.</summary>
    public bool HoldingMarker => down && marker is not null;

    /// <summary>Feeds one frame of mouse state and camera look; returns the outcome on the frame a click is released.</summary>
    public ClickOutcome Update(
        bool mouseDown,
        Vector2 cursor,
        (float Yaw, float Pitch) look,
        bool overUi,
        bool overGizmo,
        int? marker
    )
    {
        if (mouseDown && !down)
        {
            down = true;
            start = cursor;
            startLook = look;
            travelled = false;
            ignored = overUi || overGizmo;
            this.marker = marker;
            return default;
        }

        if (mouseDown)
        {
            if (Vector2.Distance(cursor, start) > MaxTravel || Turned(look))
                travelled = true;
            return default;
        }

        if (!down)
            return default;
        down = false;
        if (ignored || travelled)
            return default;
        return this.marker is { } m ? new ClickOutcome(ClickKind.Select, m) : new ClickOutcome(ClickKind.Deselect);
    }

    /// <summary>Forgets a press in progress.</summary>
    public void Reset() => down = false;

    private bool Turned((float Yaw, float Pitch) look) =>
        MathF.Abs(Angles.Delta(startLook.Yaw, look.Yaw)) > MaxTurn || MathF.Abs(look.Pitch - startLook.Pitch) > MaxTurn;
}
