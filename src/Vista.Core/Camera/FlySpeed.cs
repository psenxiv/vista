namespace Vista.Core.Camera;

/// <summary>The free-cam's speed setting, stepped through fixed multipliers and clamped at both ends.</summary>
public sealed class FlySpeed
{
    /// <summary>The multipliers the setting steps through, slowest first.</summary>
    public static readonly IReadOnlyList<float> Steps = [0.25f, 0.5f, 1f, 2f, 4f];

    /// <summary>Position in <see cref="Steps"/>; starts at normal speed.</summary>
    public int Index { get; private set; } = 2;

    /// <summary>The current multiplier.</summary>
    public float Multiplier => Steps[Index];

    /// <summary>Moves up (positive) or down (negative) by whole steps.</summary>
    public void Step(int steps) => Set(Index + steps);

    /// <summary>Jumps to a step.</summary>
    public void Set(int index) => Index = Math.Clamp(index, 0, Steps.Count - 1);
}
