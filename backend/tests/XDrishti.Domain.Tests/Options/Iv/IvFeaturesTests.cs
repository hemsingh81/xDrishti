using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Tests.Filings;
using static XDrishti.Domain.Tests.Options.Iv.IvKit;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>P14-11 criteria 1 and 3: the hand values of <c>iv_atm</c> and <c>pcr_oi</c> through <see cref="IvFeatures.Compute"/>, the order of the catalog, and every closed NaN reason reached by a fixture.</summary>
public sealed class IvFeaturesTests
{
    private const int IvAtm = 0;
    private const int Rank = 1;
    private const int Pct = 2;
    private const int Term = 3;
    private const int MinusRv = 4;
    private const int NiftyRank = 5;
    private const int Pcr = 6;
    private const int Results = 7;

    private static IvBarBook Both(DateTimeOffset start, float? ce = 15f, float? pe = 17f, long? ceOi = 800, long? peOi = 1_200, bool usable = true) => Book(1, start, ce, pe, ceOi, peOi, usable);

    private static IReadOnlyDictionary<DateOnly, double> Hand(int sessions = 252) => Series(Wed, sessions, i => (260 - i + 1) / 100.0 + 0.0);

    private static FeatureCell[] Run(IvFeatureInput input) => IvFeatures.Compute(input);

    [Fact]
    public void The_catalog_has_eight_features_and_the_row_follows_its_order()
    {
        IvFeatureCatalog.Names.ShouldBe(["iv_atm", "iv_rank_252", "iv_pct_252", "iv_term_slope", "iv_minus_rv20", "nifty_iv_rank_252", "pcr_oi", "results_before_expiry"]);
        Run(Input()).Length.ShouldBe(IvFeatureCatalog.Names.Count);
        IvFeatureCatalog.Group.Problem().ShouldBeNull();
        ExtensionCatalogs.GroupOf("iv-1").ShouldBe(IvFeatureCatalog.Group);
    }

    [Fact]
    public void Ce_15_and_pe_17_percent_give_an_atm_iv_of_016_and_oi_of_1200_over_800_gives_a_ratio_of_one_and_a_half()
    {
        var cells = Run(Input(plan: Plan(), bars: Both(At(10, 55))));

        cells[IvAtm].Value.ShouldBe(0.16, 1e-12);
        cells[Pcr].Value.ShouldBe(1.5, 1e-12);
    }

    [Fact]
    public void The_rank_and_percentile_are_the_hand_values_when_the_previous_session_holds_136_percent_in_a_window_of_10_to_260_percent()
    {
        // the previous session holds 1.36; the 251 sessions before it hold 0.10 ... 2.60, so the window of 252 has min 0.10 and max 2.60 and 126 values below 1.36
        var series = new Dictionary<DateOnly, double>(Series(Wed, 252, i => i == 1 ? 1.36 : (262 - i) / 100.0));

        var cells = Run(Input(own: series));

        cells[Rank].Value.ShouldBe((1.36 - 0.10) / (2.60 - 0.10), 1e-12);
        cells[Pct].Value.ShouldBe(0.5, 1e-12);
    }

    [Fact]
    public void An_instrument_that_is_not_an_option_underlying_gets_no_options_for_its_own_features_and_still_the_nifty_rank()
    {
        var cells = Run(Input(nifty: Series(Wed, 252, i => (261 - i) / 100.0)));

        foreach (var index in new[] { IvAtm, Rank, Pct, Term, MinusRv, Pcr, Results })
        {
            cells[index].Reason.ShouldBe(IvReasons.NoOptions, IvFeatureCatalog.Names[index]);
            double.IsNaN(cells[index].Value).ShouldBeTrue();
        }

        cells[NiftyRank].IsKnown.ShouldBeTrue();
    }

    [Fact]
    public void No_nifty_series_is_no_options_for_the_nifty_rank_and_a_short_one_is_short_history()
    {
        Run(Input())[NiftyRank].Reason.ShouldBe(IvReasons.NoOptions);
        Run(Input(nifty: Hand(199)))[NiftyRank].Reason.ShouldBe(FeatureReasons.ShortHistory);
    }

    [Fact]
    public void A_bar_that_ends_after_the_instant_or_belongs_to_an_earlier_day_is_no_bar_before_t()
    {
        Run(Input(plan: Plan(), bars: Both(At(11, 0))))[IvAtm].Reason.ShouldBe(IvReasons.NoBarBeforeT);
        Run(Input(plan: Plan(), bars: Both(At(15, 25, new DateOnly(2026, 7, 28)))))[IvAtm].Reason.ShouldBe(IvReasons.NoBarBeforeT);
        Run(Input(plan: Plan(), bars: Both(At(11, 0))))[Pcr].Reason.ShouldBe(IvReasons.NoBarBeforeT);
        Run(Input(plan: Plan(), bars: null))[IvAtm].Reason.ShouldBe(IvReasons.NoBarBeforeT);
    }

    [Fact]
    public void A_key_session_that_failed_the_quality_checks_or_a_suspect_bar_is_archive_unusable()
    {
        var cells = Run(Input(plan: Plan(), bars: Both(At(10, 55), usable: false)));

        cells[IvAtm].Reason.ShouldBe(IvReasons.ArchiveUnusable);
        cells[Pcr].Reason.ShouldBe(IvReasons.ArchiveUnusable);
        cells[Term].Reason.ShouldBe(IvReasons.ArchiveUnusable);
        cells[MinusRv].Reason.ShouldBe(IvReasons.ArchiveUnusable);

        var suspect = new IvBarBook();
        suspect.Add(1, "CE", 5, [Bar(At(10, 55), 0, 15f, suspect: true)]);
        suspect.Add(1, "PE", 5, [Bar(At(10, 55), 0, 17f)]);
        Run(Input(plan: Plan(), bars: suspect))[IvAtm].Reason.ShouldBe(IvReasons.ArchiveUnusable);
    }

    [Fact]
    public void A_missing_or_invalid_iv_or_a_missing_side_is_iv_missing()
    {
        Run(Input(plan: Plan(), bars: Both(At(10, 55), pe: null)))[IvAtm].Reason.ShouldBe(IvReasons.IvMissing);
        Run(Input(plan: Plan(), bars: Both(At(10, 55), ce: 0f)))[IvAtm].Reason.ShouldBe(IvReasons.IvMissing);
        var oneSide = new IvBarBook();
        oneSide.Add(1, "CE", 5, Offsets(At(10, 55), 15f, 100));
        Run(Input(plan: Plan(), bars: oneSide))[IvAtm].Reason.ShouldBe(IvReasons.IvMissing, "the PE side has no bar at all");
    }

    [Fact]
    public void The_term_slope_is_the_back_minus_the_front_and_unknown_for_a_share_or_a_missing_back_bar()
    {
        var book = new IvBarBook();
        book.Add(1, "CE", 5, Offsets(At(10, 55), 15f, 100));
        book.Add(1, "PE", 5, Offsets(At(10, 55), 17f, 100));
        book.Add(2, "CE", 5, Offsets(At(10, 55), 19f, 100));
        book.Add(2, "PE", 5, Offsets(At(10, 55), 21f, 100));

        Run(Input(plan: Plan(), bars: book))[Term].Value.ShouldBe(0.20 - 0.16, 1e-12);
        Run(Input(plan: Plan(back: null), bars: book))[Term].Reason.ShouldBe(IvReasons.NoTermSeries);
        Run(Input(plan: Plan(), bars: Both(At(10, 55))))[Term].Reason.ShouldBe(IvReasons.NoBarBeforeT, "the second series has no bar");
        Run(Input(plan: Plan(back: null), bars: Both(At(10, 55), pe: null)))[Term].Reason.ShouldBe(IvReasons.NoTermSeries, "a share has no second series whatever its front bar");
    }

    [Fact]
    public void Iv_minus_realised_volatility_is_the_hand_value_and_short_history_without_21_closes()
    {
        var closes = new Dictionary<DateOnly, double>(Series(Wed, 21, i => i % 2 == 0 ? 100.0 : 101.0));
        var rv = Math.Log(1.01) * Math.Sqrt(20.0 / 19.0) * Math.Sqrt(252.0);

        Run(Input(plan: Plan(), bars: Both(At(10, 55)), closes: closes))[MinusRv].Value.ShouldBe(0.16 - rv, 1e-12);
        Run(Input(plan: Plan(), bars: Both(At(10, 55))))[MinusRv].Reason.ShouldBe(FeatureReasons.ShortHistory);
    }

    [Fact]
    public void A_flat_window_has_a_flat_history_reason_for_the_rank_and_the_percentile()
    {
        var cells = Run(Input(own: Series(Wed, 252, _ => 0.2)));

        cells[Rank].Reason.ShouldBe(IvReasons.FlatHistory);
        cells[Pct].Reason.ShouldBe(IvReasons.FlatHistory);
        Run(Input(own: Hand(199)))[Rank].Reason.ShouldBe(FeatureReasons.ShortHistory);
    }

    [Fact]
    public void Missing_open_interest_and_zero_call_open_interest_have_their_own_reasons()
    {
        Run(Input(plan: Plan(), bars: Both(At(10, 55), ceOi: null)))[Pcr].Reason.ShouldBe(IvReasons.OiMissing);
        Run(Input(plan: Plan(), bars: Both(At(10, 55), ceOi: 0)))[Pcr].Reason.ShouldBe(IvReasons.ZeroCallOi);
        Run(Input(plan: Plan(), bars: Both(At(10, 55), peOi: 0)))[Pcr].Value.ShouldBe(0.0, "zero put open interest over a positive call open interest is a measured zero");
        var oneSide = new IvBarBook();
        oneSide.Add(1, "CE", 5, Offsets(At(10, 55), 15f, 100));
        Run(Input(plan: Plan(), bars: oneSide))[Pcr].Reason.ShouldBe(IvReasons.OiMissing);
    }

    [Fact]
    public void Results_before_expiry_is_one_when_the_next_results_session_is_on_or_before_the_expiry_zero_when_after_and_unavailable_without_a_visible_entry()
    {
        var on = new[] { CalendarSamples.Row(1, new DateOnly(2026, 8, 4), CalendarSamples.At(7, 20, 10, 0)) };
        var after = new[] { CalendarSamples.Row(1, new DateOnly(2026, 8, 5), CalendarSamples.At(7, 20, 10, 0)) };
        var notYetVisible = new[] { CalendarSamples.Row(1, new DateOnly(2026, 8, 3), CalendarSamples.At(7, 29, 11, 1)) };

        Run(Input(plan: Plan(), rows: on))[Results].ShouldBe(FeatureCell.Known(1));
        Run(Input(plan: Plan(), rows: after))[Results].ShouldBe(FeatureCell.Known(0));
        Run(Input(plan: Plan(), rows: notYetVisible))[Results].ShouldBe(FeatureCell.Unknown(IvReasons.EventUnavailable));
        Run(Input(plan: Plan()))[Results].ShouldBe(FeatureCell.Unknown(IvReasons.EventUnavailable));
        Run(Input(plan: null, rows: on))[Results].ShouldBe(FeatureCell.Unknown(IvReasons.NoOptions));
    }

    [Fact]
    public void An_option_underlying_with_no_eligible_series_is_no_eligible_series_and_a_non_underlying_is_no_options()
    {
        var underlying = Run(Input(plan: null, bars: new IvBarBook(), own: Hand(10)));
        var other = Run(Input());

        foreach (var index in new[] { IvAtm, Term, MinusRv, Pcr, Results })
        {
            underlying[index].Reason.ShouldBe(IvReasons.NoEligibleSeries, IvFeatureCatalog.Names[index]);
            other[index].Reason.ShouldBe(IvReasons.NoOptions, IvFeatureCatalog.Names[index]);
        }
    }

    [Fact]
    public void The_ce_and_pe_bars_must_share_one_timestamp_the_latest_both_have_else_the_value_is_missing()
    {
        // CE has bars at 10:50 and 10:55, PE only at 10:50: the pair is 10:50 (IV 15 and 17), not CE 10:55 with PE 10:50
        var book = new IvBarBook();
        book.Add(1, "CE", 5, Offsets(At(10, 50), 15f, 100).Concat(Offsets(At(10, 55), 40f, 100)));
        book.Add(1, "PE", 5, Offsets(At(10, 50), 17f, 100));

        var cells = Run(Input(plan: Plan(), bars: book));

        cells[IvAtm].Value.ShouldBe(0.16, 1e-12);
        cells[Pcr].Value.ShouldBe(1.0, 1e-12);

        var never = new IvBarBook();
        never.Add(1, "CE", 5, Offsets(At(10, 55), 15f, 100));
        never.Add(1, "PE", 5, Offsets(At(10, 50), 17f, 100));
        var unpaired = Run(Input(plan: Plan(), bars: never));
        unpaired[IvAtm].Reason.ShouldBe(IvReasons.IvMissing);
        unpaired[Pcr].Reason.ShouldBe(IvReasons.OiMissing);
    }

    [Fact]
    public void No_unknown_value_is_ever_a_number_and_every_reason_is_in_the_closed_set()
    {
        var cells = Run(Input(plan: Plan(), bars: Both(At(11, 0), usable: false), own: Hand(10), nifty: Hand(10)));

        foreach (var cell in cells.Where(c => !c.IsKnown))
        {
            double.IsNaN(cell.Value).ShouldBeTrue();
            IvReasons.All.ShouldContain(cell.Reason!);
        }

        cells.ShouldAllBe(c => !c.Unverified);
    }
}
