namespace Vista.Core.Guide;

public sealed record BulletList(IReadOnlyList<IReadOnlyList<Run>> Items) : Block;
