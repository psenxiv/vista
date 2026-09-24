namespace Vista.Core.Guide;

public sealed record Table(
    IReadOnlyList<IReadOnlyList<Run>> Header,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<Run>>> Rows
) : Block;
