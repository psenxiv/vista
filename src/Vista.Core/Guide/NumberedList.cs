namespace Vista.Core.Guide;

public sealed record NumberedList(IReadOnlyList<IReadOnlyList<Run>> Items) : Block;
