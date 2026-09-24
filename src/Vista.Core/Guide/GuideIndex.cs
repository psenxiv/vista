using System.Text.RegularExpressions;

namespace Vista.Core.Guide;

/// <summary>Reads the guide's index, a nested bullet list of links, into its topic tree.</summary>
public static partial class GuideIndex
{
    /// <summary>The topics in file order; an entry indented under another is its sub-topic, and lines that are not linked bullets are skipped.</summary>
    public static IReadOnlyList<GuideTopic> Parse(string markdown)
    {
        var roots = new List<Node>();
        var open = new List<(int Indent, Node Node)>();

        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var match = Entry().Match(line);
            if (!match.Success)
                continue;

            var indent = match.Groups["indent"].Value.Replace("\t", "    ").Length;
            var node = new Node(match.Groups["title"].Value.Trim(), match.Groups["file"].Value.Trim());
            while (open.Count > 0 && open[^1].Indent >= indent)
                open.RemoveAt(open.Count - 1);

            if (open.Count == 0)
                roots.Add(node);
            else
                open[^1].Node.Children.Add(node);
            open.Add((indent, node));
        }

        return roots.Select(Freeze).ToList();
    }

    private static GuideTopic Freeze(Node node) => new(node.Title, node.File, node.Children.Select(Freeze).ToList());

    private sealed record Node(string Title, string File)
    {
        public List<Node> Children { get; } = [];
    }

    [GeneratedRegex(@"^(?<indent>\s*)[-*]\s+\[(?<title>[^\]]+)\]\((?<file>[^)]+)\)\s*$")]
    private static partial Regex Entry();
}
