using System.Text.Json;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>The record of an exit choice (ADR 0054) is read without inventing a number: a missing member is absent or a zero count, never a figure.</summary>
public sealed class ReportComparisonSourceTests
{
    [Fact]
    public void A_record_with_null_numbers_and_no_stability_member_reads_as_absent()
    {
        using var doc = JsonDocument.Parse("""
            {"unit":"RNet","default":"A","chosen":null,"no_choice_reason":"no stable winner",
             "folds":[{"index":1,"test_start":"2025-01-01","test_end":"2025-03-31","winner":null,"winner_test_trades":0,"winner_test_score":null,"thin":true}],
             "pooled":[{"name":"p","trades":5,"mean_r":null,"score":0.5}],"banner":["x"]}
            """);

        var info = ReportComparisonSource.Map(doc.RootElement, "s", "c", "ORB", "15m", "BUY", "intraday", "*", DateTimeOffset.UnixEpoch);

        info.Stable.ShouldBeNull();
        info.ChancePass.ShouldBeNull();
        info.ChosenCandidateId.ShouldBeNull();
        info.NoChoiceReason.ShouldBe("no stable winner");
        info.Pooled.Single().MeanR.ShouldBeNull();
        info.Pooled.Single().Score.ShouldBe(0.5);
        info.Folds.Single().Winner.ShouldBeNull();
        info.Folds.Single().Thin.ShouldBeTrue();
        info.Banner.ShouldBe(["x"]);
    }

    [Fact]
    public void A_stability_verdict_and_its_counts_are_read()
    {
        using var doc = JsonDocument.Parse("""{"stability":{"stable":true,"reason":"ok","folds":4,"agreeing":3,"positive_out_of_sample":3,"chance_pass":0.25,"lone_peaks":1}}""");

        var info = ReportComparisonSource.Map(doc.RootElement, "s", "c", "ORB", "15m", "BUY", "intraday", "*", DateTimeOffset.UnixEpoch);

        info.Stable.ShouldBe(true);
        (info.UsableFolds, info.AgreeingFolds, info.PositiveFolds, info.LonePeaks).ShouldBe((4, 3, 3, 1));
        info.ChancePass.ShouldBe(0.25);
    }
}
