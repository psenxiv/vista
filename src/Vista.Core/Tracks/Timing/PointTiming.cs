namespace Vista.Core.Tracks.Timing;

/// <summary>A point's timing: the pinned speed of the leg arriving at it, its hold, and its arrival and departure easing.</summary>
public sealed record PointTiming(
    float? LegSpeed = null,
    float Hold = 0f,
    TangentMode InMode = TangentMode.Auto,
    TangentMode OutMode = TangentMode.Auto,
    float InTangent = 0f,
    float OutTangent = 0f,
    bool Broken = false
);
