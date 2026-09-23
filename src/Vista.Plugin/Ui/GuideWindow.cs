using System.Numerics;
using System.Text;
using Vista.Core.Guide;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The User Guide: a topic tree on the left and the chosen page on the right, drawn from Markdown embedded in the plugin.</summary>
internal sealed class GuideWindow : Window, IDisposable
{
    private const float TreeWidth = 210f;
    private const string Resources = "Vista.Guide.";

    private static readonly float[] HeadingScales = [1.5f, 1.25f, 1.1f];

    private readonly IFontHandle[] headings;
    private readonly Dictionary<string, IReadOnlyList<Block>> pages = [];
    private IReadOnlyList<GuideTopic> topics = [];
    private bool indexRead;
    private string? shown;

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
            shown = topics.FirstOrDefault()?.File;
        }

        if (ImGui.BeginChild("guide-topics", new Vector2(TreeWidth, 0f), true)) DrawTopics(topics);
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("guide-page", Vector2.Zero, true) && shown is not null) DrawPage(Page(shown));
        ImGui.EndChild();
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
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case Heading heading:
                    if (i > 0) ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.5f));
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
                    ImGui.Separator();
                    break;
            }

            ImGui.Spacing();
        }
    }

    private static void DrawTable(Table table, string id)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count));
        if (columns == 0 || !ImGui.BeginTable(id, columns, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;

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

    /// <summary>Lays runs out word by word, so mixed styles still wrap, returning to where the first word began.</summary>
    private static void Flow(IReadOnlyList<Run> runs)
    {
        var left = ImGui.GetCursorScreenPos().X;
        var right = left + ImGui.GetContentRegionAvail().X;
        var space = ImGui.CalcTextSize(" ").X;
        float? lineEnd = null;
        var spaceBefore = false;
        var word = new StringBuilder();

        void Place(RunStyle style)
        {
            if (word.Length == 0) return;
            var text = word.ToString();
            word.Clear();

            var gap = spaceBefore ? space : 0f;
            spaceBefore = false;
            if (lineEnd is { } end)
            {
                if (end + gap + ImGui.CalcTextSize(text).X <= right) ImGui.SameLine(0f, gap);
                else ImGui.SetCursorScreenPos(new Vector2(left, ImGui.GetCursorScreenPos().Y));
            }

            uint? colour = style switch
            {
                RunStyle.Bold => UiColours.Accent,
                RunStyle.Code => UiColours.Muted(),
                _ => null,
            };
            using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
                ImGui.TextUnformatted(text);
            lineEnd = ImGui.GetItemRectMax().X;
        }

        foreach (var run in runs)
        {
            foreach (var ch in run.Text)
            {
                if (!char.IsWhiteSpace(ch)) { word.Append(ch); continue; }
                Place(run.Style);
                spaceBefore = true;
            }

            // A run's last word never takes a space after it; the next run's own leading space decides.
            Place(run.Style);
        }

        if (lineEnd is null) ImGui.NewLine();
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
