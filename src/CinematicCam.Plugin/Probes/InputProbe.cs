using System.Numerics;
using System.Runtime.InteropServices;
using CinematicCam.Core.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 3: reads our keys' physical state, hides them from the game, and captures clicks over a test spot.</summary>
internal sealed unsafe class InputProbe
{
    private const float SpotRadius = 40f;

    private static readonly (VirtualKey Vk, bool NeedsCtrl)[] Keys =
    [
        (VirtualKey.C, false),
        (VirtualKey.R, false),
        (VirtualKey.OEM_3, false),
        (VirtualKey.Z, true),
        (VirtualKey.Y, true),
    ];

    private readonly bool[] held = new bool[Keys.Length];
    private bool enabled;
    private bool overSpot;
    private int cFramesHeld;
    private int cFramesInBuffer;

    public void Toggle()
    {
        enabled = !enabled;
        Plugin.Log.Information("[probe] input {State}", enabled ? "on" : "off");
    }

    /// <summary>Draws the click spot. Call from UiBuilder.Draw.</summary>
    public void Draw(CameraMode mode)
    {
        if (!enabled || mode != CameraMode.Editing) return;
        DrawSpot(ImGui.GetIO());
    }

    /// <summary>Reads our keys' physical state and clears held ones from the game's buffer. Call from Framework.Update.</summary>
    public void Update(CameraMode mode)
    {
        if (!enabled || mode != CameraMode.Editing || IsTyping()) { Array.Clear(held); return; }

        var alt = IsPhysicallyDown(VirtualKey.MENU);
        var ctrl = IsPhysicallyDown(VirtualKey.CONTROL);

        for (var i = 0; i < Keys.Length; i++)
        {
            var (vk, needsCtrl) = Keys[i];
            var down = IsPhysicallyDown(vk);
            if (down && !held[i])
                Plugin.Log.Information("[probe] key {Key} down, alt {Alt}, ctrl {Ctrl}", vk, alt, ctrl);

            if (vk == VirtualKey.C) CountC(down, held[i]);
            held[i] = down;

            if (down && (!needsCtrl || ctrl)) Plugin.KeyState[vk] = false;
        }
    }

    /// <summary>Counts, per C hold, frames physically held against frames the game's buffer showed it, logging on release.</summary>
    private void CountC(bool down, bool wasDown)
    {
        if (down)
        {
            cFramesHeld++;
            if (Plugin.KeyState[VirtualKey.C]) cFramesInBuffer++;
            return;
        }

        if (!wasDown) return;
        Plugin.Log.Information("[probe] C released: physically held {Held} frames, in game buffer {Buffer}", cFramesHeld, cFramesInBuffer);
        cFramesHeld = 0;
        cFramesInBuffer = 0;
    }

    private static bool IsPhysicallyDown(VirtualKey vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void DrawSpot(ImGuiIOPtr io)
    {
        var viewport = ImGuiHelpers.MainViewport;
        var centre = viewport.Pos + (viewport.Size / 2f);
        overSpot = Vector2.Distance(io.MousePos, centre) <= SpotRadius;

        var flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove;
        if (!overSpot) flags |= ImGuiWindowFlags.NoInputs;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        if (ImGui.Begin("##ccam-probe-input", flags))
        {
            ImGui.GetWindowDrawList().AddCircle(centre, SpotRadius, overSpot ? 0xFF00FFFF : 0xFFFFFFFF, 0, 2f);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                Plugin.Log.Information("[probe] click, over spot {Over}, imgui wants mouse {Want}", overSpot, io.WantCaptureMouse);
        }

        ImGui.End();
    }

    private static bool IsTyping()
    {
        var module = RaptureAtkModule.Instance();
        return module != null && module->IsTextInputActive();
    }
}
