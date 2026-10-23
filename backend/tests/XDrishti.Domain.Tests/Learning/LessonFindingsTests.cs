using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P6-16: monitor reports become findings with stable evidence keys; only alarms still on are findings.</summary>
public sealed class LessonFindingsTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static DriftReport Drift(params DriftEvent[] events) =>
        new(new DriftOptions(), 0, [new CellDrift(Cell, 50, 20, null, null, events)], null, [], []);

    private static DriftEvent Event(DriftDetectorKind kind, bool fired, long at, int check) => new(kind, Cell, fired, at, check, -1, 0, $"evidence {check}");

    [Fact]
    public void An_alarm_that_fired_and_stays_on_is_a_finding_with_its_numbers_and_a_stable_key()
    {
        var report = Drift(Event(DriftDetectorKind.RollingBand, true, 100, 3), Event(DriftDetectorKind.Cusum, true, 200, 7));

        var findings = LessonFindings.FromDrift(report);

        findings.Select(f => (f.Monitor, f.Subject, f.EvidenceKey)).ShouldBe([(LessonMonitorKind.RollingBand, Cell, "100:3"), (LessonMonitorKind.Cusum, Cell, "200:7")]);
        findings[0].Problem.ShouldContain("evidence 3");
        LessonFindings.FromDrift(report).ShouldBe(findings);
    }

    [Fact]
    public void An_alarm_that_cleared_is_not_a_finding_and_a_later_firing_is_the_one_kept()
    {
        var cleared = Drift(Event(DriftDetectorKind.RollingBand, true, 100, 3), Event(DriftDetectorKind.RollingBand, false, 150, 4));
        var again = Drift(Event(DriftDetectorKind.RollingBand, true, 100, 3), Event(DriftDetectorKind.RollingBand, false, 150, 4), Event(DriftDetectorKind.RollingBand, true, 300, 9));

        LessonFindings.FromDrift(cleared).ShouldBeEmpty();
        LessonFindings.FromDrift(again).Single().EvidenceKey.ShouldBe("300:9");
    }

    [Fact]
    public void A_growing_tag_is_a_finding_whose_key_names_the_counts()
    {
        var growing = new GrowingTag(Cell, LossTagKind.GaveBackProfit, 6, 2, 10);
        var report = new LossTagReport(new LossTagOptions(), 40, 20, [new CellLossTags(Cell, 40, 20, [0, 6, 0, 0, 0, 0, 0], 3, [growing])], [], 1, []);

        var finding = LessonFindings.FromLossTags(report).Single();

        finding.Monitor.ShouldBe(LessonMonitorKind.GrowingTag);
        finding.Subject.ShouldBe(Cell);
        finding.EvidenceKey.ShouldBe("GaveBackProfit:2/10->6/10");
        finding.Problem.ShouldContain("2 of 10 to 6 of 10");
    }

    [Fact]
    public void No_growing_tag_and_no_alarm_give_no_finding()
    {
        LessonFindings.FromDrift(Drift()).ShouldBeEmpty();
        LessonFindings.FromLossTags(new LossTagReport(new LossTagOptions(), 0, 0, [], [], 0, [])).ShouldBeEmpty();
    }
}
