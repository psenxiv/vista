#if DEBUG
using Vista.Core.SelfTest;
using Xunit;

namespace Vista.Tests.SelfTest;

public class SelfTestReportTests
{
    [Fact]
    public void TheStartLineGivesTheBuildAndTimeAndAsksThePlayerToKeepStill() =>
        Assert.Equal(
            "[selftest] ===== START ===== build 0.9.0.1, 2026-09-25 14:03:07. Stand still and don't touch the keyboard or mouse until it ends.",
            SelfTestReport.StartLine("0.9.0.1", new DateTime(2026, 9, 25, 14, 3, 7))
        );

    // Two passes, one fail and one skip.
    private static readonly SelfTestResult[] Results =
    [
        SelfTestResult.Pass("touch points", "4 of 4 installed"),
        SelfTestResult.Fail("movement lock", "counter 2 then 2, expected 3"),
        SelfTestResult.Skip("game UI", "the game UI was already hidden"),
        SelfTestResult.Pass("input hooks", "5 of 5 enabled while held, 0 after release"),
    ];

    [Fact]
    public void TheEndLineCountsEachOutcome() =>
        Assert.Equal(
            "[selftest] ===== END: 2 passed, 1 failed, 1 skipped =====",
            SelfTestReport.EndLine(Results, stopped: false)
        );

    [Fact]
    public void TheEndLineSaysWhenTheRunLeftVistaStopped() =>
        Assert.Equal(
            "[selftest] ===== END: 2 passed, 1 failed, 1 skipped ===== Vista has stopped until it's reloaded.",
            SelfTestReport.EndLine(Results, stopped: true)
        );

    [Fact]
    public void AnEmptyRunEndsWithNoCounts() =>
        Assert.Equal("[selftest] ===== END: 0 passed, 0 failed, 0 skipped =====", SelfTestReport.EndLine([], false));

    [Fact]
    public void ACheckLineGivesItsOutcomeNameAndNumbers()
    {
        // The spec's own example line.
        Assert.Equal(
            "[selftest] FAIL movement lock: counter 2 then 2, expected 3",
            SelfTestResult.Fail("movement lock", "counter 2 then 2, expected 3").Line
        );
        Assert.Equal("[selftest] PASS input hooks: all", SelfTestResult.Pass("input hooks", "all").Line);
        Assert.Equal("[selftest] SKIP game UI: hidden", SelfTestResult.Skip("game UI", "hidden").Line);
    }

    [Fact]
    public void OfPassesOnlyWhenTold()
    {
        Assert.Equal(SelfTestOutcome.Pass, SelfTestResult.Of("x", true, "d").Outcome);
        Assert.Equal(SelfTestOutcome.Fail, SelfTestResult.Of("x", false, "d").Outcome);
    }
}
#endif
