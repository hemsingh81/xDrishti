using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-03 acceptance criteria 2 to 5 at the verdict (ADR 0205): the deflated Sharpe ratio from the stored trial statistics, in the honest states.</summary>
public sealed class LabDeflationTests
{
    private const string Family = "LAB_TEST";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly ITestLedger _ledger = Substitute.For<ITestLedger>();

    private readonly IVariantStatsStore _store = Substitute.For<IVariantStatsStore>();

    private static string Hash(int i) => i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0');

    private static LedgerRow Ledger(int i) => new(Guid.NewGuid(), new string('k', 64), Family, 1, Hash(i), null, "u", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "sweep", T0);

    private static VariantStatsRow Stat(int i, double? sr, DateTimeOffset? at = null) => new(Family, Guid.NewGuid(), Hash(i), 50, 0.1, 1, 0, 3, sr, "sweep", at ?? T0);

    private static double[] Results() => [.. Enumerable.Range(0, 100).Select(i => (i % 5) switch { 0 => -1.0, 1 => 0.5, 2 => 1.5, 3 => 0.25, _ => 1.0 })];

    private static LabVerdictView Verdict(double? nEff = 80, bool dependence = true) => new(
        "FAIL", 1, 1, new EvidenceView(100, 0.65, 2, 3, 2, 3, 0, "walk_forward", dependence ? new DependenceView("ReportOnly", 100, nEff, 1.25, "cluster", null, null, null, null, null, null, nEff is null ? "unavailable: fewer than 5 sessions" : string.Empty, string.Empty) : null),
        [], [], [], [], []);

    private LabDeflation Service(DeflationOptions? options = null, int ledgerRows = 3, params VariantStatsRow[] stored)
    {
        _ledger.ListAsync(Family, Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(1, ledgerRows).Select(Ledger)]);
        _store.ListAsync(Family, Arg.Any<CancellationToken>()).Returns(stored);
        return new LabDeflation(_ledger, _store, options ?? new DeflationOptions());
    }

    private static VariantStatsRow MainRow(double[] results, int hash = 1) => LabVariantStats.Of(Family, Guid.NewGuid(), Hash(hash), TradeMoments.Of(results), "robust", T0.AddDays(1));

    [Fact]
    public async Task The_probability_is_the_deflated_sharpe_ratio_of_the_out_of_sample_trades_against_the_stored_trials()
    {
        var results = Results();
        var main = MainRow(results);
        var service = Service(null, 3, Stat(2, 0.10), Stat(3, 0.20));
        var m = TradeMoments.Of(results);
        var sigma = Math.Sqrt(((0.10 - ((0.10 + 0.20 + m.Sr) / 3)) * (0.10 - ((0.10 + 0.20 + m.Sr) / 3))
            + (0.20 - ((0.10 + 0.20 + m.Sr) / 3)) * (0.20 - ((0.10 + 0.20 + m.Sr) / 3))
            + (m.Sr - ((0.10 + 0.20 + m.Sr) / 3)) * (m.Sr - ((0.10 + 0.20 + m.Sr) / 3))) / 2);

        var view = (await service.ComputeAsync(Family, Verdict(80), results, [main], Ct))!;

        view.Trials.ShouldBe(3);
        view.LedgerTrials.ShouldBe(3);
        view.TrialSrSd!.Value.ShouldBe(sigma, 1e-12);
        view.LuckBarSr!.Value.ShouldBe(DeflatedSharpe.ExpectedMax(3, sigma), 1e-12);
        view.Sr!.Value.ShouldBe(m.Sr, 1e-12);
        view.NEff.ShouldBe(80);
        view.Probability!.Value.ShouldBe(DeflatedSharpe.Deflated(m.Sr, 3, sigma, 80, m.Skew, m.Kurt).Value!.Value, 1e-12);
        view.Basis.ShouldBe("nEff");
        view.Settings.ShouldBe("ReportOnly/0.95/stored/NEff");
        view.Note.ShouldContain("independent");
        view.Note.ShouldContain("informational");
        await _store.DidNotReceive().AppendAsync(Arg.Any<IReadOnlyList<VariantStatsRow>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Off_gives_no_figure_and_reads_nothing()
    {
        var service = Service(new DeflationOptions(ConfidenceMode.Off));

        (await service.ComputeAsync(Family, Verdict(), Results(), [MainRow(Results())], Ct)).ShouldBeNull();
        await _store.DidNotReceive().ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fewer_than_two_stored_trials_is_unavailable_and_never_a_number()
    {
        var results = Results();
        var service = Service(null, 1);

        var view = (await service.ComputeAsync(Family, Verdict(), results, [MainRow(results)], Ct))!;

        view.Probability.ShouldBeNull();
        view.LuckBarSr.ShouldBeNull();
        view.Note.ShouldStartWith("unavailable: trial Sharpe ratios not stored");
        view.Trials.ShouldBe(1);
    }

    [Fact]
    public async Task Statistics_for_under_eighty_percent_of_the_ledgers_variants_is_unavailable_and_says_how_many()
    {
        var results = Results();
        var service = Service(null, 10, Stat(2, 0.1), Stat(3, 0.2), Stat(4, 0.15));

        var view = (await service.ComputeAsync(Family, Verdict(), results, [MainRow(results)], Ct))!;

        view.Probability.ShouldBeNull();
        view.Note.ShouldStartWith("unavailable: trial statistics stored for only 4 of 10 variants");
    }

    [Fact]
    public async Task Eight_of_ten_variants_is_enough()
    {
        var results = Results();
        var service = Service(null, 10, [.. Enumerable.Range(2, 7).Select(i => Stat(i, 0.05 + (i * 0.01)))]);

        var view = (await service.ComputeAsync(Family, Verdict(), results, [MainRow(results)], Ct))!;

        view.Trials.ShouldBe(8);
        view.Probability.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_assumed_spread_replaces_the_stored_trials_and_counts_the_ledgers_variants()
    {
        var results = Results();
        var service = Service(new DeflationOptions(AssumedTrialSrSd: 0.05), 16);
        var m = TradeMoments.Of(results);

        var view = (await service.ComputeAsync(Family, Verdict(100), results, [MainRow(results)], Ct))!;

        view.Trials.ShouldBe(16);
        view.TrialSrSd.ShouldBe(0.05);
        view.LuckBarSr!.Value.ShouldBe(0.0900, 5e-5);
        view.Probability!.Value.ShouldBe(DeflatedSharpe.Deflated(m.Sr, 16, 0.05, 100, m.Skew, m.Kurt).Value!.Value, 1e-12);
        view.Note.ShouldContain("assumption");
        view.Settings.ShouldBe("ReportOnly/0.95/0.05/NEff");
    }

    [Fact]
    public async Task The_latest_row_of_a_variant_counts_and_this_runs_row_replaces_a_stored_one()
    {
        var results = Results();
        var main = MainRow(results);
        var service = Service(null, 3, Stat(2, 0.9, T0), Stat(2, 0.10, T0.AddDays(1)), Stat(3, 0.20), Stat(1, 5.0));

        var view = (await service.ComputeAsync(Family, Verdict(), results, [main], Ct))!;

        view.Trials.ShouldBe(3);
        var sd = view.TrialSrSd!.Value;
        var all = new[] { 0.10, 0.20, TradeMoments.Of(results).Sr };
        var mean = all.Average();
        sd.ShouldBe(Math.Sqrt(all.Sum(v => (v - mean) * (v - mean)) / 2), 1e-12);
    }

    [Fact]
    public async Task A_variant_without_a_ratio_is_not_a_trial_and_lowers_the_coverage()
    {
        var results = Results();
        var service = Service(null, 3, Stat(2, null), Stat(3, 0.2));

        var view = (await service.ComputeAsync(Family, Verdict(), results, [MainRow(results)], Ct))!;

        view.Trials.ShouldBe(2);
        view.Note.ShouldStartWith("unavailable: trial statistics stored for only 2 of 3 variants");
    }

    [Fact]
    public async Task Without_effective_trades_it_is_unavailable_and_with_a_raw_basis_it_uses_the_count()
    {
        var results = Results();
        var stored = new[] { Stat(2, 0.10), Stat(3, 0.20) };

        var effective = (await Service(null, 3, stored).ComputeAsync(Family, Verdict(null), results, [MainRow(results)], Ct))!;
        var noBlock = (await Service(null, 3, stored).ComputeAsync(Family, Verdict(dependence: false), results, [MainRow(results)], Ct))!;
        var raw = (await Service(new DeflationOptions(TBasis: DeflationBasis.Raw), 3, stored).ComputeAsync(Family, Verdict(null), results, [MainRow(results)], Ct))!;

        effective.Probability.ShouldBeNull();
        effective.Note.ShouldStartWith("unavailable: effective trades unavailable (fewer than 5 sessions)");
        noBlock.Note.ShouldStartWith("unavailable: effective trades unavailable (effective n is off)");
        raw.Probability.ShouldNotBeNull();
        raw.NEff.ShouldBe(100);
        raw.Basis.ShouldBe("raw");
        raw.Note.ShouldContain("100 raw trades");
    }

    [Fact]
    public async Task Fewer_than_twenty_effective_trades_is_unavailable_with_the_bar_still_shown()
    {
        var results = Results();

        var view = (await Service(null, 3, Stat(2, 0.10), Stat(3, 0.20)).ComputeAsync(Family, Verdict(12), results, [MainRow(results)], Ct))!;

        view.Probability.ShouldBeNull();
        view.Note.ShouldStartWith("unavailable: fewer than 20 effective trades");
        view.LuckBarSr.ShouldNotBeNull();
        view.NEff.ShouldBe(12);
    }

    [Fact]
    public async Task Results_with_no_spread_have_no_ratio_and_so_no_probability()
    {
        double[] flat = [.. Enumerable.Repeat(0.5, 60)];

        var view = (await Service(null, 3, Stat(2, 0.10), Stat(3, 0.20)).ComputeAsync(Family, Verdict(), flat, [MainRow(flat)], Ct))!;

        view.Probability.ShouldBeNull();
        view.Sr.ShouldBeNull();
    }
}
