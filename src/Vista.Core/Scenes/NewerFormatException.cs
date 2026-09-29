namespace Vista.Core.Scenes;

/// <summary>Thrown for a scene file saved by a newer Vista, in a format this version can't read.</summary>
public sealed class NewerFormatException()
    : Exception("This scene needs a newer version of Vista. Update Vista to open it.");
