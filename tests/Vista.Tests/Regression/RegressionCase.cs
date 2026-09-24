using Vista.Core.Tracks;

namespace Vista.Tests.Regression;

/// <summary>One track in the camera regression scene and how many snaps it has by design.</summary>
internal sealed record RegressionCase(string Name, Track Track, int Snaps);
