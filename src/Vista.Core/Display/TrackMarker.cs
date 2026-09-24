using System.Numerics;

namespace Vista.Core.Display;

/// <summary>One marker on screen; <see cref="Screen"/> is null when off screen, and anchors and Look At points use point −1.</summary>
public readonly record struct TrackMarker(Guid Track, int Point, Vector2? Screen, MarkerKind Kind = MarkerKind.Point);
