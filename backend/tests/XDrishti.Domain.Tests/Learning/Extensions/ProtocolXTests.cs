using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Domain.Tests.Learning.Extensions.ExtensionKit;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Protocol X of P15-10 (the adapter over the P13-05 core with quarterly folds and season resampling): criterion 1 for X, and criterion 10's gate.</summary>
public sealed class ProtocolXTests
{
    private static readonly DateOnly From = new(2020, 1, 1);

    private static ExtensionComparisonOptions Opts(DateOnly? holdOut = null) => ProtocolX.Options("recipe", "tb-1", holdOut) with { Draws = 300 };

    private static ExtensionReport Run(Mode mode, DateOnly to, int trials = 1, int seed = 3)
    {
        var rows = Synthetic(seed, From, to, 4, mode);
        var options = Opts();
        var recipe = new ProbeFactory(0, 1);
        var sessions = Sessions(From, to.AddDays(10));
        return ProtocolX.Compare(rows, Base, Group, recipe, Ledger(Group, recipe, rows, options, trials), sessions, options).Value;
    }

    [Fact]
    public void The_filings_preset_uses_quarterly_folds_and_season_blocks()
    {
        var o = Opts();

        o.Unit.ShouldBe(FoldUnit.Quarter);
        o.Block.ShouldBe(BlockKind.Season);
        o.MinEvaluableFolds.ShouldBe(6);
        o.MinPairedCandidates.ShouldBe(1_000);
    }

    [Fact]
    public void A_planted_effect_is_a_keep_at_the_required_folds()
    {
        var report = Run(Mode.Planted, new DateOnly(2022, 9, 30));

        report.FoldsEvaluable.ShouldBe(6);
        report.Verdict.ShouldBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void One_fold_fewer_is_not_evaluable_and_names_the_folds()
    {
        var report = Run(Mode.Planted, new DateOnly(2022, 6, 30));

        report.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        report.Missing.ShouldContain(m => m.StartsWith("folds 5 of 6", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 16)]
    public void Random_labels_are_never_a_keep(int seed, int trials)
    {
        Run(Mode.Placebo, new DateOnly(2023, 6, 30), trials, seed).Verdict.ShouldNotBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void A_gated_or_thin_evaluation_is_not_evaluable_and_prints_no_estimate()
    {
        var outcome = ProtocolX.NotEvaluable(["parser_unverified: run abc was extracted with parser 'xbrl-1-synthetic'"]);

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.Single().ShouldStartWith("parser_unverified");
        outcome.EstimatesJson.ShouldBe("{\"status\":\"unavailable\"}");
    }
}
