namespace Vista.Core.Guide;

public sealed record Paragraph(IReadOnlyList<Run> Runs) : Block;
