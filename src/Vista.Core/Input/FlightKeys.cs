using System.Numerics;

namespace Vista.Core.Input;

/// <summary>The free-cam flight the held keys ask for: move along (forward, up, right), roll (+1 right, −1 left), and whether faster.</summary>
public readonly record struct FlightKeys(Vector3 Move, float Roll, bool Faster);
