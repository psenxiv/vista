using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The switchboard: the Program and Next cards, ten slots, the toggles and Cut, and the Program shot's scrub bar; disabled in Off and View.</summary>
internal sealed class SwitchboardWindow : Window
{
    private const int SlotsPerRow = SwitchboardEditing.SlotCount / 2;
    private const float MinSlotWidth = 90f;
    private const float OutlineThickness = 2f;
    private const string EmptyLabel = "-";
    private const string SlotMenu = "slot-menu";

    private static readonly FontAwesomeIcon[] ToggleIcons =
    [
        FontAwesomeIcon.Bolt,
        FontAwesomeIcon.History,
        FontAwesomeIcon.StepForward,
    ];

    private readonly SessionState session;
    private readonly PendingEdit<float> fields;
    private readonly NamePrompt namePrompt = new("slot");
    private readonly ViewZoom<(CameraMode Mode, int Slot, Guid Target)> zoom = new();
    private CameraMode lastMode;
    private int menuSlot;

    public SwitchboardWindow(SessionState session, PendingEdit<float> fields)
        : base(
            "Switchboard###vista-switchboard",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        )
    {
        this.session = session;
        this.fields = fields;
        RespectCloseHotkey = false;
    }

    /// <summary>Opens the window as the mode changes to Live; Dalamud calls this every frame, open or not.</summary>
    public override void PreOpenCheck()
    {
        if (session.Mode == CameraMode.Live && lastMode != CameraMode.Live)
            IsOpen = true;
        lastMode = session.Mode;
    }

    /// <summary>Fixes the height to the content and keeps the slots wide enough for the toggles.</summary>
    public override void PreDraw()
    {
        // Measured with the spacing Draw pushes, so the height fits what it draws.
        using var spacing = WindowStyle.Push();
        var style = ImGui.GetStyle();
        var slot = MathF.Max(MinSlotWidth * ImGuiHelpers.GlobalScale, IconButton.RowWidth(ToggleIcons));
        var width = (slot * (SlotsPerRow + 1)) + (style.ItemSpacing.X * SlotsPerRow) + (style.WindowPadding.X * 2f);
        var height =
            ImGui.GetFrameHeight()
            + (style.WindowPadding.Y * 2f)
            + CardHeight()
            + (SlotHeight() * 2f)
            + ImGui.GetFrameHeight()
            + (style.ItemSpacing.Y * 3f);
        // Dalamud multiplies the constraints by the global scale; these are already on-screen sizes.
        var scale = ImGuiHelpers.GlobalScale;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(width, height) / scale,
            MaximumSize = new Vector2(float.MaxValue, height / scale),
        };
    }

    /// <summary>Ends a scrub of the Program shot, since a closed window never reports the bar let go.</summary>
    public override void OnClose() => session.Board?.EndScrub();

    public override void Draw()
    {
        using (WindowStyle.Push())
        {
            var board = session.Board;
            var scene = session.Scene;
            var (program, next) = session.ShownAir;
            var width = ImGui.GetContentRegionAvail().X;
            DrawCards(board, scene, program, next, width);
            using (ImRaii.Disabled(board is null))
            {
                DrawGrid(board, scene, program, next, width);
                DrawBar(board, scene);
            }

            namePrompt.Draw();
        }
    }

    /// <summary>A card's height: its heading, the slot's number and name, and a third line, with padding.</summary>
    private static float CardHeight()
    {
        var style = ImGui.GetStyle();
        return (style.FramePadding.Y * 4f) + (ImGui.GetTextLineHeight() * 3f) + style.ItemSpacing.Y;
    }

    /// <summary>A slot's height: its number and icon over its name, with padding.</summary>
    private static float SlotHeight()
    {
        var style = ImGui.GetStyle();
        return (style.FramePadding.Y * 4f) + (ImGui.GetTextLineHeight() * 2f) + (style.ItemSpacing.Y / 2f);
    }

    /// <summary>The Program and Next cards side by side.</summary>
    private static void DrawCards(SwitchboardPlayer? board, Scene scene, int? program, int? next, float width)
    {
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var size = new Vector2((width - gap) / 2f, CardHeight());
        var start = ImGui.GetCursorScreenPos();

        var programLine = DrawCard("program", "Program", UiColours.Red, scene, program, start, size);
        if (board?.Timeline is { } timeline && program is not null)
            DrawProgress(board.Head, timeline.Total, programLine, size.X);

        var nextStart = start with { X = start.X + size.X + gap };
        var nextLine = DrawCard("next", "Next", UiColours.Green, scene, next, nextStart, size);
        if (Held(scene, next) is not null)
            ImGui.GetWindowDrawList().AddText(nextLine, ImGui.GetColorU32(UiColours.Muted()), "Goes live on Cut");

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(size with { X = width });
    }

    /// <summary>What slot <paramref name="slot"/> holds, or null when it's empty or none is given.</summary>
    private static Slot? Held(Scene scene, int? slot) => slot is { } i ? scene.Switchboard.Slots[i] : null;

    /// <summary>A card's frame, heading, and the slot's number and name or a dash; returns where its third line starts.</summary>
    private static Vector2 DrawCard(
        string id,
        string heading,
        uint colour,
        Scene scene,
        int? slot,
        Vector2 min,
        Vector2 size
    )
    {
        var style = ImGui.GetStyle();
        var list = ImGui.GetWindowDrawList();
        var max = min + size;
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton($"card-{id}", size);
        list.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), style.FrameRounding);

        var inset = style.FramePadding * 2f;
        var line = ImGui.GetTextLineHeight() + (style.ItemSpacing.Y / 2f);
        list.AddText(min + inset, ImGui.GetColorU32(colour), heading);

        var nameTop = min.Y + inset.Y + line;
        if (Held(scene, slot) is { } held)
            DrawNumberedName(("card", id), slot!.Value, held.Name, min with { Y = nameTop }, max.X);
        else
            list.AddText(new Vector2(min.X + inset.X, nameTop), ImGui.GetColorU32(UiColours.Muted()), EmptyLabel);
        return new Vector2(min.X + inset.X, nameTop + line);
    }

    /// <summary>A slot's number, then its name cut to fit and scrolled while the item just drawn is hovered, on one line from <paramref name="min"/> to <paramref name="right"/>.</summary>
    private static void DrawNumberedName(object row, int slot, string name, Vector2 min, float right)
    {
        var inset = ImGui.GetStyle().FramePadding.X * 2f;
        var number = $"{slot + 1}";
        ImGui.GetWindowDrawList().AddText(min with { X = min.X + inset }, ImGui.GetColorU32(UiColours.Muted()), number);
        // RowText insets its text by the same padding, so the name starts one gap after the number.
        var nameLeft = min.X + ImGui.CalcTextSize(number).X + ImGui.GetStyle().ItemSpacing.X;
        var nameMin = min with { X = nameLeft };
        var max = new Vector2(right, min.Y + ImGui.GetTextLineHeight());
        RowText.Draw(row, name, nameMin, max, right - nameLeft);
    }

    /// <summary>The Program shot's progress as a thin bar, then its time through the shot, on the card's third line.</summary>
    private static void DrawProgress(double head, double total, Vector2 at, float cardWidth)
    {
        var style = ImGui.GetStyle();
        var list = ImGui.GetWindowDrawList();
        var time = Units.ClockOf(head, total);
        var timeWidth = ImGui.CalcTextSize(time).X;
        var right = at.X + cardWidth - (style.FramePadding.X * 4f);
        var barRight = right - timeWidth - style.ItemSpacing.X;
        var lineHeight = ImGui.GetTextLineHeight();
        var barHeight = MathF.Max(2f, lineHeight / 3f);
        var top = at.Y + ((lineHeight - barHeight) / 2f);
        var min = at with { Y = top };
        var max = new Vector2(MathF.Max(barRight, at.X), top + barHeight);
        list.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBgActive), barHeight / 2f);
        var done = Fraction.Between((float)head, 0f, (float)total, 0f);
        if (done > 0f)
            list.AddRectFilled(
                min,
                max with
                {
                    X = float.Lerp(min.X, max.X, done),
                },
                ImGui.GetColorU32(UiColours.Accent),
                barHeight / 2f
            );
        list.AddText(new Vector2(right - timeWidth, at.Y), ImGui.GetColorU32(ImGuiCol.Text), time);
    }

    /// <summary>The ten slots in two rows of five, then the toggles and Cut in a sixth column.</summary>
    private void DrawGrid(SwitchboardPlayer? board, Scene scene, int? program, int? next, float width)
    {
        var spacing = ImGui.GetStyle().ItemSpacing;
        var cell = new Vector2((width - (spacing.X * SlotsPerRow)) / (SlotsPerRow + 1), SlotHeight());
        var origin = ImGui.GetCursorScreenPos();
        for (var slot = 0; slot < SwitchboardEditing.SlotCount; slot++)
        {
            var at =
                origin
                + new Vector2(slot % SlotsPerRow * (cell.X + spacing.X), slot / SlotsPerRow * (cell.Y + spacing.Y));
            DrawSlot(board, scene, slot, at, cell, program == slot, next == slot);
        }

        var side = origin with { X = origin.X + (SlotsPerRow * (cell.X + spacing.X)) };
        DrawToggles(scene.Switchboard, side, cell);
        ImGui.SetCursorScreenPos(side with { Y = side.Y + cell.Y + spacing.Y });
        using (ImRaii.Disabled(next is null))
        {
            if (ImGui.Button("Cut", cell))
                board?.Cut();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, (cell.Y * 2f) + spacing.Y));
        DrawSlotMenu(scene);
    }

    /// <summary>One slot: a click makes it Next (or cuts to it with Direct cut) and a right-click opens its menu; outlined red on Program and green as Next, and dimmed when it can't play.</summary>
    private void DrawSlot(
        SwitchboardPlayer? board,
        Scene scene,
        int slot,
        Vector2 min,
        Vector2 size,
        bool onProgram,
        bool isNext
    )
    {
        ImGui.SetCursorScreenPos(min);
        if (ImGui.InvisibleButton($"slot-{slot}", size))
            board?.Click(slot);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            menuSlot = slot;
            ImGui.OpenPopup(SlotMenu);
        }

        var style = ImGui.GetStyle();
        var list = ImGui.GetWindowDrawList();
        var max = min + size;
        var held = scene.Switchboard.Slots[slot];
        var fill =
            ImGui.IsItemActive() ? ImGuiCol.FrameBgActive
            : ImGui.IsItemHovered() ? ImGuiCol.FrameBgHovered
            : ImGuiCol.FrameBg;
        if (held is not null || fill != ImGuiCol.FrameBg)
            list.AddRectFilled(min, max, ImGui.GetColorU32(fill), style.FrameRounding);
        if (held is null)
            list.AddRect(min, max, ImGui.GetColorU32(UiColours.Dim()), style.FrameRounding);

        var inset = style.FramePadding * 2f;
        var nameTop = min.Y + inset.Y + ImGui.GetTextLineHeight() + (style.ItemSpacing.Y / 2f);
        using (
            ImRaii.PushStyle(
                ImGuiStyleVar.Alpha,
                style.Alpha * UiColours.DimAlpha,
                held is not null && !SwitchboardEditing.CanPlay(scene, slot)
            )
        )
        {
            var number = $"{slot + 1}";
            list.AddText(min + inset, ImGui.GetColorU32(UiColours.Muted()), number);
            if (held is not null)
            {
                var icon = held.TrackId is not null ? FontAwesomeIcon.Route : FontAwesomeIcon.ListOl;
                var iconAt = min + inset + new Vector2(ImGui.CalcTextSize(number).X + style.ItemSpacing.X, 0f);
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    list.AddText(iconAt, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
                var nameMax = max with { Y = nameTop + ImGui.GetTextLineHeight() };
                RowText.Draw(("slot", slot), held.Name, min with { Y = nameTop }, nameMax, size.X);
            }
            else
            {
                list.AddText(new Vector2(min.X + inset.X, nameTop), ImGui.GetColorU32(UiColours.Muted()), EmptyLabel);
            }
        }

        if (onProgram)
            list.AddRect(
                min,
                max,
                ImGui.GetColorU32(UiColours.Red),
                style.FrameRounding,
                ImDrawFlags.None,
                OutlineThickness
            );
        if (isNext)
        {
            // Inside Program's outline when a slot is both.
            var gap = onProgram ? new Vector2(OutlineThickness * 1.5f) : Vector2.Zero;
            list.AddRect(
                min + gap,
                max - gap,
                ImGui.GetColorU32(UiColours.Green),
                style.FrameRounding,
                ImDrawFlags.None,
                OutlineThickness
            );
        }
    }

    /// <summary>Direct cut, Keep rolling and Auto Next, centred in the sixth column's top cell and lit while on.</summary>
    private void DrawToggles(Switchboard board, Vector2 min, Vector2 cell)
    {
        var row = IconButton.RowWidth(ToggleIcons);
        ImGui.SetCursorScreenPos(
            min + new Vector2(MathF.Max(0f, (cell.X - row) / 2f), MathF.Max(0f, (cell.Y - ImGui.GetFrameHeight()) / 2f))
        );
        Toggle("direct-cut", ToggleIcons[0], SwitchboardToggle.DirectCut, board.DirectCut, "Direct cut");
        ImGui.SameLine();
        Toggle("keep-rolling", ToggleIcons[1], SwitchboardToggle.KeepRolling, board.KeepRolling, "Keep rolling");
        ImGui.SameLine();
        Toggle("auto-next", ToggleIcons[2], SwitchboardToggle.AutoNext, board.AutoNext, "Auto Next");
    }

    private void Toggle(string id, FontAwesomeIcon icon, SwitchboardToggle toggle, bool on, string tooltip)
    {
        if (IconButton.Toggle(id, icon, on, tooltip))
            Report(session.SetSwitchboardToggle(toggle, !on));
    }

    /// <summary>The right-clicked slot's menu: Assign (playlists, then tracks), Rename and Clear.</summary>
    private void DrawSlotMenu(Scene scene)
    {
        if (!ImGui.BeginPopup(SlotMenu))
            return;
        var slot = menuSlot;
        var held = scene.Switchboard.Slots[slot];
        using (var assign = ImRaii.Menu("Assign"))
        {
            if (assign)
            {
                for (var i = 0; i < scene.Playlists.Count; i++)
                    AssignItem(
                        $"playlist-{i}",
                        FontAwesomeIcon.ListOl,
                        scene.Playlists[i].Name,
                        slot,
                        scene.Playlists[i].Id
                    );
                if (scene.Playlists.Count > 0 && scene.Tracks.Count > 0)
                    ImGui.Separator();
                for (var i = 0; i < scene.Tracks.Count; i++)
                    AssignItem($"track-{i}", FontAwesomeIcon.Route, scene.Tracks[i].Name, slot, scene.Tracks[i].Id);
            }
        }

        if (Menu.Item("Rename", held is not null) && held is not null)
            namePrompt.Ask(
                "Rename slot",
                held.Name,
                text => (SceneNames.LengthRefusal(text), null),
                name => Report(session.RenameSlot(slot, name))
            );
        if (Menu.Item("Clear", held is not null))
            Report(session.ClearSlot(slot));
        ImGui.EndPopup();
    }

    /// <summary>An Assign entry: the icon, then the track or playlist's name, hovered and chosen as one item across the menu's width.</summary>
    private void AssignItem(string id, FontAwesomeIcon icon, string name, int slot, Guid target)
    {
        var iconWidth = IconButton.GlyphWidth(icon);
        var gap = ImGui.GetStyle().ItemInnerSpacing.X;
        var width = iconWidth + gap + ImGui.CalcTextSize(name).X;
        // Sized to its content so the menu fits it, but hovered across the menu's width, as ImGui's own menu items are.
        var span = (ImGuiSelectableFlags)ImGuiSelectableFlagsPrivate.SpanAvailWidth;
        if (ImGui.Selectable($"##{id}", false, span, new Vector2(width, 0f)))
            Report(session.AssignSlot(slot, target));

        var at = ImGui.GetItemRectMin();
        var list = ImGui.GetWindowDrawList();
        var colour = ImGui.GetColorU32(ImGuiCol.Text);
        using (ImRaii.PushFont(UiBuilder.IconFont))
            list.AddText(at, colour, icon.ToIconString());
        list.AddText(at with { X = at.X + iconWidth + gap }, colour, name);
    }

    /// <summary>The Program shot's scrub bar and its time, or an empty disabled bar with nothing on Program.</summary>
    private void DrawBar(SwitchboardPlayer? board, Scene scene)
    {
        var timeline = board?.Timeline;
        var total = timeline?.Total ?? 0.0;
        var head = timeline is null ? 0.0 : board!.Head;
        var key = (session.Mode, board?.Program ?? -1, SwitchboardEditing.Target(board?.ProgramSlot) ?? Guid.Empty);
        var view = zoom.View(key, (float)total);

        // The time sits right of the bar, sized for its longest reading so the bar doesn't shift as it counts.
        var timeWidth = ImGui.CalcTextSize(Units.ClockOf(total, total)).X;
        var width = ImGui.GetContentRegionAvail().X - timeWidth - ImGui.GetStyle().ItemSpacing.X;
        using (ImRaii.PushStyle(ImGuiStyleVar.GrabMinSize, ImGui.GetStyle().GrabMinSize * ScrubBar.GrabScale))
        {
            if (board is not null && PlaylistBar.Draw(board, scene, fields.Commit, width, view))
                ScrubBar.Zoom(zoom, view, (float)total, board.Scrubbing);
            else
                DrawEmptyBar(width);
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Units.ClockOf(head, total));
    }

    /// <summary>A disabled bar with nothing in it, the size of the scrub bar.</summary>
    private static void DrawEmptyBar(float width)
    {
        using var disabled = ImRaii.Disabled();
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(width, 1f), ImGui.GetFrameHeight());
        ImGui.Dummy(size);
        ImGui
            .GetWindowDrawList()
            .AddRectFilled(min, min + size, ImGui.GetColorU32(ImGuiCol.FrameBg), ImGui.GetStyle().FrameRounding);
    }
}
