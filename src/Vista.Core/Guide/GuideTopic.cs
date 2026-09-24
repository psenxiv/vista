namespace Vista.Core.Guide;

/// <summary>A topic in the User Guide's tree: its title, its page's file, and its sub-topics.</summary>
public sealed record GuideTopic(string Title, string File, IReadOnlyList<GuideTopic> Children);
