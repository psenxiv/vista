using System.Text;
using System.Text.RegularExpressions;

namespace Vista.Core.Guide;

/// <summary>How a run of text is shown.</summary>
public enum RunStyle { Plain, Bold, Command, Key, Icon, Link }

/// <summary>A stretch of text in one style: an icon's name for Icon, and a page file in <paramref name="Target"/> for Link.</summary>
public readonly record struct Run(string Text, RunStyle Style, string? Target = null);

/// <summary>One block of a guide page.</summary>
public abstract record Block;

public sealed record Heading(int Level, IReadOnlyList<Run> Runs) : Block;

public sealed record Paragraph(IReadOnlyList<Run> Runs) : Block;

public sealed record BulletList(IReadOnlyList<IReadOnlyList<Run>> Items) : Block;

public sealed record NumberedList(IReadOnlyList<IReadOnlyList<Run>> Items) : Block;

public sealed record Table(IReadOnlyList<IReadOnlyList<Run>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<Run>>> Rows) : Block;

public sealed record Divider : Block;

/// <summary>Reads a guide page's Markdown into blocks, for the subset the User Guide draws; anything else is plain text.</summary>
public static partial class GuideMarkdown
{
    public static IReadOnlyList<Block> Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<Block>();
        var paragraph = new List<string>();

        void EndParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new Paragraph(Inline(string.Join(' ', paragraph))));
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { EndParagraph(); continue; }

            if (HeadingLine().Match(line) is { Success: true } heading)
            {
                EndParagraph();
                var level = heading.Groups["marks"].Length;
                // Each section heading sits under a faint line, unless the page starts with it or one is already there.
                if (level == 2 && blocks.Count > 0 && blocks[^1] is not Divider) blocks.Add(new Divider());
                blocks.Add(new Heading(level, Inline(heading.Groups["text"].Value.Trim())));
                continue;
            }

            if (IsTableRow(line) && i + 1 < lines.Length && Separator().IsMatch(lines[i + 1]))
            {
                EndParagraph();
                var header = Cells(line);
                var rows = new List<IReadOnlyList<IReadOnlyList<Run>>>();
                i += 2;
                while (i < lines.Length && IsTableRow(lines[i])) rows.Add(Cells(lines[i++]));
                i--;
                blocks.Add(new Table(header, rows));
                continue;
            }

            if (DividerLine().IsMatch(line)) { EndParagraph(); blocks.Add(new Divider()); continue; }

            if (BulletItem().IsMatch(line) || NumberedItem().IsMatch(line))
            {
                EndParagraph();
                var numbered = NumberedItem().IsMatch(line);
                var marker = numbered ? NumberedItem() : BulletItem();
                var items = new List<IReadOnlyList<Run>>();
                var item = new StringBuilder();
                for (; i < lines.Length; i++)
                {
                    var next = lines[i];
                    if (marker.Match(next) is { Success: true } start)
                    {
                        if (item.Length > 0) items.Add(Inline(item.ToString()));
                        item.Clear().Append(start.Groups["text"].Value.Trim());
                    }
                    else if (!string.IsNullOrWhiteSpace(next) && char.IsWhiteSpace(next[0]))
                    {
                        item.Append(' ').Append(next.Trim());
                    }
                    else break;
                }

                items.Add(Inline(item.ToString()));
                i--;
                blocks.Add(numbered ? new NumberedList(items) : new BulletList(items));
                continue;
            }

            paragraph.Add(line.Trim());
        }

        EndParagraph();
        return blocks;
    }

    /// <summary>Bold, keys, commands, icons and links within a line; an unclosed marker stays as it was typed.</summary>
    public static IReadOnlyList<Run> Inline(string text)
    {
        var runs = new List<Run>();
        var plain = new StringBuilder();

        void Add(string value, RunStyle style, string? target = null)
        {
            if (plain.Length > 0) { runs.Add(new Run(plain.ToString(), RunStyle.Plain)); plain.Clear(); }
            runs.Add(new Run(value, style, target));
        }

        void AddSpan(string span)
        {
            if (span.StartsWith('/')) { Add(span, RunStyle.Command); return; }
            var keys = span.Split(" + ");
            for (var k = 0; k < keys.Length; k++)
            {
                if (k > 0) plain.Append(" + ");
                Add(keys[k], RunStyle.Key);
            }
        }

        for (var i = 0; i < text.Length;)
        {
            if (text[i] == '`' && text.IndexOf('`', i + 1) is var tick and > 0)
            {
                AddSpan(text[(i + 1)..tick]);
                i = tick + 1;
            }
            else if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*' && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var close and > 0)
            {
                Add(text[(i + 2)..close], RunStyle.Bold);
                i = close + 2;
            }
            else if (text[i] == '[' && Link().Match(text, i) is { Success: true } link && link.Index == i)
            {
                Add(link.Groups["text"].Value, RunStyle.Link, link.Groups["target"].Value.Trim());
                i += link.Length;
            }
            else if (text[i] == '{' && IconTag().Match(text, i) is { Success: true } icon && icon.Index == i)
            {
                Add(icon.Groups["name"].Value, RunStyle.Icon);
                i += icon.Length;
            }
            else
            {
                plain.Append(text[i++]);
            }
        }

        if (plain.Length > 0) runs.Add(new Run(plain.ToString(), RunStyle.Plain));
        return runs;
    }

    private static bool IsTableRow(string line) => line.TrimStart().StartsWith('|');

    private static IReadOnlyList<IReadOnlyList<Run>> Cells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(cell => Inline(cell.Trim())).ToList();
    }

    [GeneratedRegex(@"^(?<marks>#{1,3})\s+(?<text>.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^\s*-{3,}\s*$")]
    private static partial Regex DividerLine();

    [GeneratedRegex(@"^[-*]\s+(?<text>.*)$")]
    private static partial Regex BulletItem();

    [GeneratedRegex(@"^\d+\.\s+(?<text>.*)$")]
    private static partial Regex NumberedItem();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<target>[^)]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\{icon:(?<name>[A-Za-z0-9]+)\}")]
    private static partial Regex IconTag();
}
