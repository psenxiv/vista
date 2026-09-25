#if DEBUG
namespace Vista.Core.SelfTest;

/// <summary>One self-test check's outcome, with the numbers that decided it.</summary>
public sealed record SelfTestResult(string Check, SelfTestOutcome Outcome, string Detail)
{
    /// <summary>The check's line for chat and the log, such as <c>[selftest] PASS movement lock: counter 2 then 3 then 2</c>.</summary>
    public string Line => SelfTestReport.Line($"{Outcome.ToString().ToUpperInvariant()} {Check}: {Detail}");

    /// <summary>A passed check.</summary>
    public static SelfTestResult Pass(string check, string detail) => new(check, SelfTestOutcome.Pass, detail);

    /// <summary>A failed check.</summary>
    public static SelfTestResult Fail(string check, string detail) => new(check, SelfTestOutcome.Fail, detail);

    /// <summary>A check that couldn't run in the current state, with the reason.</summary>
    public static SelfTestResult Skip(string check, string reason) => new(check, SelfTestOutcome.Skip, reason);

    /// <summary>A passed check when <paramref name="passed"/>, otherwise a failed one.</summary>
    public static SelfTestResult Of(string check, bool passed, string detail) =>
        passed ? Pass(check, detail) : Fail(check, detail);
}
#endif
