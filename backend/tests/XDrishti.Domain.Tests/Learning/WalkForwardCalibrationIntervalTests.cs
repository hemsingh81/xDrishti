using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The pooled calibration error of a candidate carries its day-cluster bootstrap interval and the log loss against the base rate, and only reports (CF-10 part 10a, ADR 0214).</summary>
public sealed class WalkForwardCalibrationIntervalTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);

    private sealed class Half : IScorerFactory
    {
        public string Name => "half";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Fixed();

        private sealed class Fixed : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }

    private static List<ValidationRow> Data() =>
        [.. Enumerable.Range(0, 240).Select(i =>
        {
            var date = Start.AddDays(i * 3);
            var epoch = new DateTimeOffset(date.Year, date.Month, date.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var label = i % 3 == 0 ? 1 : 0;
            return new ValidationRow("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), label, label == 1 ? 1.5 : -1, date, epoch, epoch + 3600, i % 2 == 0 ? "A" : "B", [label, i % 3]);
        })];

    [Fact]
    public void Only_the_pooled_result_of_a_candidate_carries_the_interval()
    {
        var report = WalkForwardValidator.Compare(Data(), [new Half()], new ValidationOptions()).Value;

        report.Pooled.Where(p => p.Name != "half").ShouldAllBe(p => p.Calibration == null);
        report.Results.SelectMany(r => r.Scorers).ShouldAllBe(s => s.Calibration == null);
        var c = report.Pooled.Single(p => p.Name == "half").Calibration!;
        c.N.ShouldBe(report.Pooled.Single(p => p.Name == "half").Metrics.N);
        c.Draws.ShouldBe(1000);
        c.Level.ShouldBe(0.90);
        c.EceLowPp.ShouldBeLessThanOrEqualTo(c.EceHighPp);
        c.EcePp.ShouldBe(report.Pooled.Single(p => p.Name == "half").Metrics.EcePp, 1e-9);
    }

    [Fact]
    public void The_interval_repeats_on_the_same_rows_and_is_printed_with_the_log_loss_against_the_base_rate()
    {
        var one = WalkForwardValidator.Compare(Data(), [new Half()], new ValidationOptions()).Value;
        var two = WalkForwardValidator.Compare(Data(), [new Half()], new ValidationOptions()).Value;

        two.Pooled.Single(p => p.Name == "half").Calibration.ShouldBe(one.Pooled.Single(p => p.Name == "half").Calibration);
        one.ToText().ShouldContain("day-cluster bootstrap interval");
        one.ToText().ShouldContain("logloss minus base rate");
    }

    [Fact]
    public void Zero_draws_turn_the_interval_off_and_the_rest_of_the_report_is_unchanged()
    {
        var on = WalkForwardValidator.Compare(Data(), [new Half()], new ValidationOptions()).Value;
        var off = WalkForwardValidator.Compare(Data(), [new Half()], new ValidationOptions(EceDraws: 0)).Value;

        off.Pooled.ShouldAllBe(p => p.Calibration == null);
        off.ToText().ShouldNotContain("bootstrap interval");
        off.Verdicts.Select(v => (v.Outcome, v.FoldsBeatingBoth, v.PooledEcePp, string.Join('|', v.Reasons))).ShouldBe(on.Verdicts.Select(v => (v.Outcome, v.FoldsBeatingBoth, v.PooledEcePp, string.Join('|', v.Reasons))));
        off.Pooled.Select(p => p.Metrics).ShouldBe(on.Pooled.Select(p => p.Metrics));
    }

    [Fact]
    public void An_interval_that_could_not_be_computed_is_said_in_the_report_and_not_left_out_silently()
    {
        var report = WalkForwardValidator.Compare(Data(), [new Half()], new ValidationOptions(EceDraws: 0)).Value;
        var pooled = report.Pooled.Select(p => p.Name == "half" ? p with { CalibrationUnavailable = EceInterval.InvalidCode } : p).ToArray();

        (report with { Pooled = pooled }).ToText().ShouldContain("ece interval unavailable: calibration.invalid");
    }

    [Theory]
    [InlineData(50, 0.9)]
    [InlineData(30000, 0.9)]
    [InlineData(1000, 0.5)]
    [InlineData(1000, 1.0)]
    public void Draws_and_level_out_of_range_are_problems(int draws, double level)
    {
        new ValidationOptions(EceDraws: draws, EceLevel: level).Problems().ShouldNotBeEmpty();
        new ValidationOptions(EceDraws: 0).Problems().ShouldBeEmpty();
    }
}
