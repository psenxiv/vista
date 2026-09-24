namespace Vista.Core.Guide;

public sealed record Heading(int Level, IReadOnlyList<Run> Runs) : Block;
