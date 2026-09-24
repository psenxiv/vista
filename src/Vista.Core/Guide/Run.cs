namespace Vista.Core.Guide;

/// <summary>A stretch of text in one style: an icon's name for Icon, and a page file in <paramref name="Target"/> for Link.</summary>
public readonly record struct Run(string Text, RunStyle Style, string? Target = null);
