using System.Numerics;
using CinematicCam.Core.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 3: reads our keys from ImGui, hides them from the game, and captures clicks over a test spot.</summary>
internal sealed unsafe class InputProbe
{
    private const float SpotRadius = 40f;

    private static readonly (ImGuiKey Key, VirtualKey Vk, bool NeedsCtrl)[] Keys =
    [
        (ImGuiKey.C, VirtualKey.C, false),
        (ImGuiKey.R, VirtualKey.R, false),
        (ImGuiKey.GraveAccent, VirtualKey.OEM_3, false),
        (ImGuiKey.Z, VirtualKey.Z, true),
        (ImGuiKey.Y, VirtualKey.Y, true),
    ];

    private readonly bool[] held = new bool[Keys.Length];
    private bool enabled;
    private bool ctrl;
    private int rawFramesLogged;
    private bool overSpot;

    public void Toggle()
    {
        enabled = !enabled;
        Plugin.Log.Information("[probe] input {State}", enabled ? "on" : "off");
    }

    /// <summary>Reads keys from ImGui and draws the click spot. Call from UiBuilder.Draw.</summary>
    public void Draw(CameraMode mode)
    {
        if (!enabled || mode != CameraMode.Editing) { Array.Clear(held); return; }

        var io = ImGui.GetIO();
        ctrl = io.KeyCtrl;
        for (var i = 0; i < Keys.Length; i++)
        {
            var down = ImGui.IsKeyDown(Keys[i].Key);
            if (down && !held[i])
                Plugin.Log.Information("[probe] key {Key} down, alt {Alt}, ctrl {Ctrl}, shift {Shift}", Keys[i].Key, io.KeyAlt, io.KeyCtrl, io.KeyShift);
            held[i] = down;
        }

        DrawSpot(io);
    }

    /// <summary>Clears our held keys from the game's buffer. Call from Framework.Update.</summary>
    public void Update(CameraMode mode)
    {
        if (!enabled || mode != CameraMode.Editing || IsTyping()) return;

        for (var i = 0; i < Keys.Length; i++)
        {
            if (!held[i] || (Keys[i].NeedsCtrl && !ctrl)) continue;

            if (Keys[i].Vk == VirtualKey.C && rawFramesLogged < 40)
            {
                Plugin.Log.Information("[probe] C raw before clear {Raw}", Plugin.KeyState[VirtualKey.C]);
                rawFramesLogged++;
            }

            Plugin.KeyState[Keys[i].Vk] = false;
        }

        if (!held[0]) rawFramesLogged = 0;
    }

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
