#if DEBUG
using System.Globalization;

namespace Vista.Core.SelfTest;

/// <summary>The self-test's start, end and refusal lines, each tagged so a run can be picked out of the log.</summary>
public static class SelfTestReport
{
    /// <summary>What every self-test line starts with.</summary>
    public const string Tag = "[selftest]";

    /// <summary><paramref name="text"/> as a self-test line.</summary>
    public static string Line(string text) => $"{Tag} {text}";

    /// <summary>The line opening a run: the marker, the build and the time, and a request to keep still.</summary>
    public static string StartLine(string version, DateTime time) =>
        Line(
            string.Create(
                CultureInfo.InvariantCulture,
                $"===== START ===== build {version}, {time:yyyy-MM-dd HH:mm:ss}. Stand still and don't touch the keyboard or mouse until it ends."
            )
        );

    /// <summary>The line closing a run: the marker with the counts, then a note when the run left Vista stopped.</summary>
    public static string EndLine(IReadOnlyCollection<SelfTestResult> results, bool stopped)
    {
        var passed = results.Count(r => r.Outcome == SelfTestOutcome.Pass);
        var failed = results.Count(r => r.Outcome == SelfTestOutcome.Fail);
        var skipped = results.Count(r => r.Outcome == SelfTestOutcome.Skip);
        var end = $"===== END: {passed} passed, {failed} failed, {skipped} skipped =====";
        return Line(stopped ? $"{end} Vista has stopped until it's reloaded." : end);
    }
}
#endif
