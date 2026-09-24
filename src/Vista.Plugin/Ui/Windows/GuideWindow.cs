using System.Numerics;
using System.Text;
using Vista.Core.Guide;
using Vista.Plugin.Ui.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The User Guide: a topic tree on the left and the chosen page on the right, drawn from Markdown embedded in the plugin.</summary>
internal sealed class GuideWindow : Window, IDisposable
{
    private const float TreeWidth = 210f;
    private const string Resources = "Vista.Guide.";

    private static readonly float[] HeadingScales = [1.5f, 1.25f, 1.1f];

    // Fractions of a text line.
    private const float BlockGap = 0.35f;
    private const float HeadingGap = 0.5f;
    private const float DividerAbove = 0.6f;
    private const float DividerBelow = 0.3f;

    // A table cell's padding in pixels, room enough round a keycap.
    private static readonly Vector2 CellPadding = new(8f, 5f);

    // A keycap's inner padding in pixels, and its corner rounding.
    private static readonly Vector2 KeyPadding = new(4f, 1f);
    private const float KeyRounding = 3f;

    private readonly IFontHandle[] headings;
    private readonly Dictionary<string, IReadOnlyList<Block>> pages = [];
    private IReadOnlyList<GuideTopic> topics = [];
    private readonly HashSet<string> files = [];
    private bool indexRead;
    private string? shown;
    private string? scrolledFor;
    private string? followed;

    public GuideWindow(IFontAtlas atlas)
        : base("User Guide###vista-guide")
    {
        Size = new Vector2(760f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480f, 300f), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        // The same font as the body, built larger, so headings stay crisp rather than scaled.
        headings = HeadingScales.Select(scale => atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * scale)))).ToArray();
    }

    public void Dispose()
    {
        foreach (var font in headings) font.Dispose();
    }

    public override void Draw()
    {
        if (!indexRead)
        {
            indexRead = true;
            if (Read("index.md") is { } index) topics = GuideIndex.Parse(index);
            AddFiles(topics);
            shown = topics.FirstOrDefault()?.File;
        }

        if (ImGui.BeginChild("guide-topics", new Vector2(TreeWidth, 0f), true)) DrawTopics(topics);
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("guide-page", Vector2.Zero, true) && shown is not null)
        {
            if (scrolledFor != shown) { ImGui.SetScrollY(0f); scrolledFor = shown; }
            DrawPage(Page(shown));
        }

        ImGui.EndChild();

        // A link clicked this frame shows its page from the next, so this frame's page finishes drawing.
        if (followed is { } target && files.Contains(target)) shown = target;
        followed = null;
    }

    private void AddFiles(IReadOnlyList<GuideTopic> list)
    {
        foreach (var topic in list)
        {
            files.Add(topic.File);
            AddFiles(topic.Children);
        }
    }

    /// <summary>The tree, open by default; clicking an entry, not its arrow, shows its page.</summary>
    private void DrawTopics(IReadOnlyList<GuideTopic> list)
    {
        foreach (var topic in list)
        {
            var leaf = topic.Children.Count == 0;
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen;
            if (topic.File == shown) flags |= ImGuiTreeNodeFlags.Selected;
            if (leaf) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

            var open = ImGui.TreeNodeEx($"{topic.Title}###{topic.File}", flags);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) shown = topic.File;
            if (open && !leaf)
            {
                DrawTopics(topic.Children);
                ImGui.TreePop();
            }
        }
    }

    private void DrawPage(IReadOnlyList<Block> blocks)
    {
        var line = ImGui.GetTextLineHeight();
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case Heading heading:
                    if (i > 0 && blocks[i - 1] is not Divider) ImGui.Dummy(new Vector2(0f, line * HeadingGap));
                    using (headings[Math.Clamp(heading.Level, 1, headings.Length) - 1].Push()) Flow(heading.Runs);
                    break;
                case Paragraph paragraph:
                    Flow(paragraph.Runs);
                    break;
                case BulletList list:
                    foreach (var item in list.Items) { ImGui.Bullet(); ImGui.SameLine(); Flow(item); }
                    break;
                case NumberedList list:
                    for (var n = 0; n < list.Items.Count; n++) { ImGui.TextUnformatted($"{n + 1}."); ImGui.SameLine(); Flow(list.Items[n]); }
                    break;
                case Table table:
                    DrawTable(table, $"table{i}");
                    break;
                case Divider:
                    DrawDivider(line);
                    continue;
            }

            ImGui.Dummy(new Vector2(0f, line * BlockGap));
        }
    }

    /// <summary>A faint full-width line with space above and below.</summary>
    private static void DrawDivider(float line)
    {
        ImGui.Dummy(new Vector2(0f, line * DividerAbove));
        var at = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(at, at + new Vector2(ImGui.GetContentRegionAvail().X, 0f), UiColours.Faint());
        ImGui.Dummy(new Vector2(0f, line * DividerBelow));
    }

    private void DrawTable(Table table, string id)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count));
        if (columns == 0) return;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, CellPadding);
        if (!ImGui.BeginTable(id, columns, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var c = 0; c < columns; c++)
        {
            ImGui.TableNextColumn();
            if (c < table.Header.Count) Flow(table.Header[c]);
        }

        foreach (var row in table.Rows)
        {
            ImGui.TableNextRow();
            for (var c = 0; c < columns; c++)
            {
                ImGui.TableNextColumn();
                if (c < row.Count) Flow(row[c]);
            }
        }

        ImGui.EndTable();
    }

    /// <summary>Lays runs out word by word, so mixed styles still wrap, returning to where the first word began; keys and icons never split.</summary>
    private void Flow(IReadOnlyList<Run> runs)
    {
        var left = ImGui.GetCursorScreenPos().X;
        var right = left + ImGui.GetContentRegionAvail().X;
        var space = ImGui.CalcTextSize(" ").X;
        float? lineEnd = null;
        var spaceBefore = false;
        var word = new StringBuilder();

        void Place(Run run)
        {
            if (word.Length == 0) return;
            var text = word.ToString();
            word.Clear();

            var gap = spaceBefore ? space : 0f;
            spaceBefore = false;
            if (lineEnd is { } end)
            {
                if (end + gap + Width(run.Style, text) <= right) ImGui.SameLine(0f, gap);
                else ImGui.SetCursorScreenPos(new Vector2(left, ImGui.GetCursorScreenPos().Y));
            }

            DrawItem(run, text);
            lineEnd = ImGui.GetItemRectMax().X;
        }

        foreach (var run in runs)
        {
            if (run.Style is RunStyle.Key or RunStyle.Icon)
            {
                word.Append(run.Text);
                Place(run);
                continue;
            }

            foreach (var ch in run.Text)
            {
                if (!char.IsWhiteSpace(ch)) { word.Append(ch); continue; }
                Place(run);
                spaceBefore = true;
            }

            // A run's last word never takes a space after it; the next run's own leading space decides.
            Place(run);
        }

        if (lineEnd is null) ImGui.NewLine();
    }

    /// <summary>How wide <paramref name="text"/> draws in <paramref name="style"/>.</summary>
    private static float Width(RunStyle style, string text) => style switch
    {
        RunStyle.Key => ImGui.CalcTextSize(text).X + (KeyPadding.X * 2f),
        RunStyle.Icon when Icon(text) is { } icon => IconWidth(icon),
        RunStyle.Icon => ImGui.CalcTextSize(IconTag(text)).X,
        _ => ImGui.CalcTextSize(text).X,
    };

    /// <summary>One word, key or icon at the cursor; a clicked link word queues its page.</summary>
    private void DrawItem(Run run, string text)
    {
        switch (run.Style)
        {
            case RunStyle.Key:
                DrawKey(text);
                return;
            case RunStyle.Icon when Icon(text) is { } icon:
                using (ImRaii.PushFont(UiBuilder.IconFont)) ImGui.TextUnformatted(icon.ToIconString());
                return;
            case RunStyle.Icon:
                ImGui.TextUnformatted(IconTag(text));
                return;
            case RunStyle.Link:
                using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Accent)) ImGui.TextUnformatted(text);
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, UiColours.Accent);
                if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsItemClicked()) followed = run.Target;
                return;
        }

        uint? colour = run.Style switch
        {
            RunStyle.Bold => UiColours.Accent,
            RunStyle.Command => UiColours.Muted(),
            _ => null,
        };
        using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
            ImGui.TextUnformatted(text);
    }

    /// <summary>A key name in a rounded, bordered box, as tall as a line of text.</summary>
    private static void DrawKey(string text)
    {
        var size = ImGui.CalcTextSize(text);
        var at = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size.X + (KeyPadding.X * 2f), size.Y));
        var list = ImGui.GetWindowDrawList();
        var min = at - new Vector2(0f, KeyPadding.Y);
        var max = at + new Vector2(size.X + (KeyPadding.X * 2f), size.Y + KeyPadding.Y);
        list.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), KeyRounding);
        list.AddRect(min, max, UiColours.Dim(), KeyRounding);
        list.AddText(at + new Vector2(KeyPadding.X, 0f), ImGui.GetColorU32(ImGuiCol.Text), text);
    }

    private static FontAwesomeIcon? Icon(string name) => char.IsLetter(name[0]) && Enum.TryParse<FontAwesomeIcon>(name, out var icon) ? icon : null;

    private static string IconTag(string name) => $"{{icon:{name}}}";

    private static float IconWidth(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X;
    }

    /// <summary>A page's blocks, read once; a page that is missing says so in the pane.</summary>
    private IReadOnlyList<Block> Page(string file)
    {
        if (pages.TryGetValue(file, out var blocks)) return blocks;
        blocks = Read(file) is { } text
            ? GuideMarkdown.Parse(text)
            : [new Paragraph([new Run($"This page could not be loaded: {file}", RunStyle.Plain)])];
        pages[file] = blocks;
        return blocks;
    }

    private static string? Read(string file)
    {
        using var stream = typeof(GuideWindow).Assembly.GetManifestResourceStream(Resources + file);
        if (stream is null)
        {
            Plugin.Log.Warning("[guide] missing page {File}", file);
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
