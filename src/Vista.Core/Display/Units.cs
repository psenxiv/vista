using static System.FormattableString;

namespace Vista.Core.Display;

/// <summary>How the editor shows seconds, yalms, speeds and degrees: one precision each, always with a full stop.</summary>
public static class Units
{
    /// <summary>ImGui's format for seconds under a heading that names the unit: two decimals, fine enough for keys 0.05 s apart.</summary>
    public const string SecondsNumber = "%.2f";

    /// <summary>ImGui's format for seconds with their unit.</summary>
    public const string SecondsField = SecondsNumber + " s";

    /// <summary>ImGui's format for a position, height or distance in yalms: two decimals.</summary>
    public const string YalmsField = "%.2f";

    /// <summary>ImGui's format for a speed in yalms per second: two decimals.</summary>
    public const string YalmsPerSecondField = "%.2f";

    /// <summary>ImGui's format for an angle in degrees: one decimal.</summary>
    public const string DegreesField = "%.1f°";

    /// <summary><paramref name="seconds"/> as text with its unit, to <see cref="SecondsField"/>'s precision.</summary>
    public static string Seconds(double seconds) => Invariant($"{seconds:0.00} s");

    /// <summary><paramref name="yalms"/> as text with its unit, to <see cref="YalmsField"/>'s precision.</summary>
    public static string Yalms(float yalms) => Invariant($"{yalms:0.00} y");

    /// <summary><paramref name="speed"/> in yalms per second as text with its unit, to <see cref="YalmsPerSecondField"/>'s precision.</summary>
    public static string YalmsPerSecond(float speed) => Invariant($"{speed:0.00} y/s");
}
