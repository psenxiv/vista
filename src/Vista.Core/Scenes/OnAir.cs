namespace Vista.Core.Scenes;

/// <summary>Live's place on the switchboard: the slots on Program and Next, seconds through the Program shot, and each slot's resume point.</summary>
public sealed record OnAir(int? Program, int? Next, double ProgramTime, IReadOnlyList<double?> Resume);
