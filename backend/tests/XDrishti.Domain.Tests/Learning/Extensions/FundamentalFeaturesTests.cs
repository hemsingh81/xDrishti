using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>
/// P15-05 criteria 2 and 3 for <c>fu-1</c>: <c>sue_yoy</c> with and without the split, <c>sue_ttm</c>, growth, margin, pledge, promoter change and <c>pe_pctile_5y</c> on hand values (1e-12), the basis rule (consolidated, standalone, mixed, short), the label of an unverified parser, and every NaN with its
/// closed reason. Nothing is filled.
/// </summary>
public sealed class FundamentalFeaturesTests
{
    private const double Tolerance = 1e-12;

    private static readonly DateOnly Q = new(2026, 6, 30);

    private static readonly DateTimeOffset Decision = At(7, 29, 10, 0);

    private static readonly DateTimeOffset Broadcast = At(7, 20, 12, 0);

    private static DateOnly Quarter(int back) => QuarterEnds.Back(Q, back);

    private static ResultFactAnswer Fact(DateOnly period, string field, string basis, decimal value, DateTimeOffset? at = null) =>
        new(period, field, ResultCodes.Quarter, basis, "value", value, ResultCodes.UnitOf(field), null, new Guid(period.DayNumber, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), (at ?? Broadcast).ToUniversalTime());

    private static ResultFactsAnswer Facts(IEnumerable<ResultFactAnswer> facts, string parser = "xbrl-1-synthetic") => new([.. facts], 0) { ParserVersion = parser };

    private static ShareholdingAnswer NoHolding => new(ShareholdingAnswer.Unavailable, null, null, ShareholdingAnswer.NoEntry);

    private static FundamentalFeatureInput Input(ResultFactsAnswer facts, IReadOnlyList<AppliedAction>? actions = null, ShareholdingAnswer? holding = null, IReadOnlyList<DailyClose>? closes = null) =>
        new(Decision, Lag, new TradingCalendar(), facts, holding ?? NoHolding, actions ?? [], closes ?? []);

    // adjusted EPS newest first with eps[i] - eps[i+4] = d[i] for i = 0..8
    private static readonly decimal[] Differences = [2m, -1m, 1m, -1m, 1m, -1m, 1m, -1m, 1m];

    private static decimal[] AdjustedEps()
    {
        var eps = new decimal[13];
        for (var i = 12; i >= 0; i--)
        {
            eps[i] = i >= 9 ? 10m + (i - 9) : Differences[i] + eps[i + 4];
        }

        return eps;
    }

    private static List<ResultFactAnswer> EpsFacts(decimal[] eps, string basis = ResultCodes.Consolidated, Func<int, decimal>? raw = null) =>
        [.. eps.Select((v, k) => Fact(Quarter(k), ResultCodes.EpsBasic, basis, raw is null ? v : raw(k), k == 0 ? Broadcast : PublishedAfter(k)))];

    // an older quarter is published a month after its period end
    private static DateTimeOffset PublishedAfter(int back) => new DateTimeOffset(Quarter(back).AddDays(30).ToDateTime(TimeOnly.MinValue), Ist).AddHours(12);

    private static double ExpectedSue(decimal[] eps)
    {
        var d = Enumerable.Range(0, 9).Select(i => (double)(eps[i] - eps[i + 4])).ToArray();
        var previous = d[1..];
        var mean = previous.Average();
        return d[0] / Math.Sqrt(previous.Sum(x => (x - mean) * (x - mean)) / 7.0);
    }

    [Fact]
    public void Sue_yoy_is_the_newest_year_on_year_difference_over_the_deviation_of_the_eight_before_it()
    {
        var eps = AdjustedEps();

        var cells = FundamentalFeatures.Compute(Input(Facts(EpsFacts(eps))));

        cells[0].Value.ShouldBe(ExpectedSue(eps), Tolerance);
        cells[0].Value.ShouldBe(2.0 / Math.Sqrt(8.0 / 7.0), Tolerance);
    }

    [Fact]
    public void A_split_after_the_year_ago_quarter_is_applied_before_the_surprise_is_taken()
    {
        var eps = AdjustedEps();
        var split = new AppliedAction(new DateOnly(2026, 5, 15), CorporateActionKind.Split, 1, 2, 0.5m);
        var facts = Facts(EpsFacts(eps, raw: k => k == 0 ? eps[0] : eps[k] * 2m));

        var adjusted = FundamentalFeatures.Compute(Input(facts, [split]));
        var unadjusted = FundamentalFeatures.Compute(Input(facts));

        adjusted[0].Value.ShouldBe(ExpectedSue(eps), Tolerance, "the year-ago EPS of 2x is first put on the new share basis");
        Math.Abs(unadjusted[0].Value - adjusted[0].Value).ShouldBeGreaterThan(1.0, "without the adjustment every split is a huge surprise");
    }

    [Fact]
    public void A_quarter_published_after_the_split_already_reports_the_new_share_count_and_is_not_adjusted_again()
    {
        var eps = AdjustedEps();
        // ex-date 2025-12-01: quarters published after it (period ends 2025-12-31 and later: k = 0..2) are on the new count already; older ones (k >= 3) were published before it
        var split = new AppliedAction(new DateOnly(2025, 12, 1), CorporateActionKind.Split, 1, 2, 0.5m);
        var facts = Facts(EpsFacts(eps, raw: k => k >= 3 ? eps[k] * 2m : eps[k]));

        FundamentalFeatures.Compute(Input(facts, [split]))[0].Value.ShouldBe(ExpectedSue(eps), Tolerance);
    }

    [Fact]
    public void A_split_that_is_not_yet_known_at_the_decision_date_is_not_applied()
    {
        var eps = AdjustedEps();
        var later = new AppliedAction(new DateOnly(2026, 8, 15), CorporateActionKind.Split, 1, 2, 0.5m);

        var cells = FundamentalFeatures.Compute(Input(Facts(EpsFacts(eps)), [later]));

        cells[0].Value.ShouldBe(ExpectedSue(eps), Tolerance);
    }

    [Fact]
    public void Sue_ttm_uses_sixteen_quarters_and_the_sums_of_four()
    {
        var eps = Enumerable.Range(0, 16).Select(i => 2m + (i % 3 == 0 ? 0.25m : 0m) + (i < 4 ? 1m : 0m)).ToArray();
        var ttm = Enumerable.Range(0, 13).Select(i => (double)(eps[i] + eps[i + 1] + eps[i + 2] + eps[i + 3])).ToArray();
        var d = Enumerable.Range(0, 9).Select(i => ttm[i] - ttm[i + 4]).ToArray();
        var mean = d[1..].Average();
        var expected = d[0] / Math.Sqrt(d[1..].Sum(x => (x - mean) * (x - mean)) / 7.0);

        FundamentalFeatures.Compute(Input(Facts(EpsFacts(eps))))[1].Value.ShouldBe(expected, Tolerance);
        FundamentalFeatures.Compute(Input(Facts(EpsFacts(eps[..15]))))[1].ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory));
    }

    [Fact]
    public void Too_few_quarters_and_a_zero_deviation_are_unknown_with_their_own_reasons()
    {
        FundamentalFeatures.Compute(Input(Facts(EpsFacts(AdjustedEps()[..12]))))[0].ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory));
        FundamentalFeatures.Compute(Input(Facts(EpsFacts([.. Enumerable.Repeat(3m, 13)]))))[0].ShouldBe(FeatureCell.Unknown(FeatureReasons.Degenerate));
    }

    [Fact]
    public void Revenue_growth_and_margin_change_are_hand_values_and_a_base_of_zero_is_unknown()
    {
        var facts = Facts(
        [
            Fact(Q, ResultCodes.Revenue, ResultCodes.Consolidated, 120m), Fact(Quarter(4), ResultCodes.Revenue, ResultCodes.Consolidated, 100m),
            Fact(Q, ResultCodes.Pat, ResultCodes.Consolidated, 12m), Fact(Quarter(4), ResultCodes.Pat, ResultCodes.Consolidated, 8m),
        ]);

        var cells = FundamentalFeatures.Compute(Input(facts));

        cells[2].Value.ShouldBe(0.2, Tolerance);
        cells[3].Value.ShouldBe(100.0 * ((12.0 / 120.0) - (8.0 / 100.0)), Tolerance);
        cells[3].Value.ShouldBe(2.0, Tolerance);

        var zero = Facts([Fact(Q, ResultCodes.Revenue, ResultCodes.Consolidated, 120m), Fact(Quarter(4), ResultCodes.Revenue, ResultCodes.Consolidated, 0m), Fact(Q, ResultCodes.Pat, ResultCodes.Consolidated, 12m), Fact(Quarter(4), ResultCodes.Pat, ResultCodes.Consolidated, 8m)]);
        FundamentalFeatures.Compute(Input(zero))[2].ShouldBe(FeatureCell.Unknown(FeatureReasons.Degenerate));
        FundamentalFeatures.Compute(Input(zero))[3].ShouldBe(FeatureCell.Unknown(FeatureReasons.Degenerate));
    }

    [Fact]
    public void One_basis_per_feature_consolidated_first_then_standalone_and_never_a_mix()
    {
        ResultFactAnswer R(int back, string basis, decimal v) => Fact(Quarter(back), ResultCodes.Revenue, basis, v);
        double Growth(params ResultFactAnswer[] facts) => FundamentalFeatures.Compute(Input(Facts(facts)))[2].Value;
        string? Reason(params ResultFactAnswer[] facts) => FundamentalFeatures.Compute(Input(Facts(facts)))[2].Reason;

        Growth(R(0, "consolidated", 120), R(4, "consolidated", 100), R(0, "standalone", 150), R(4, "standalone", 100)).ShouldBe(0.2, Tolerance);
        Growth(R(0, "consolidated", 120), R(0, "standalone", 150), R(4, "standalone", 100)).ShouldBe(0.5, Tolerance);
        Reason(R(0, "consolidated", 120), R(4, "standalone", 100)).ShouldBe(FeatureReasons.BasisMixed);
        Reason(R(0, "consolidated", 120)).ShouldBe(FeatureReasons.ShortHistory);
        Reason(R(0, "consolidated", 120), R(0, "standalone", 130)).ShouldBe(FeatureReasons.ShortHistory);
    }

    [Fact]
    public void Values_built_on_a_synthetic_parser_are_labelled_and_a_checked_parser_is_not()
    {
        var facts = new[] { Fact(Q, ResultCodes.Revenue, ResultCodes.Consolidated, 120m), Fact(Quarter(4), ResultCodes.Revenue, ResultCodes.Consolidated, 100m) };

        FundamentalFeatures.Compute(Input(Facts(facts, "xbrl-1-synthetic")))[2].Unverified.ShouldBeTrue();
        FundamentalFeatures.Compute(Input(Facts(facts, "xbrl-1")))[2].Unverified.ShouldBeFalse();
        FundamentalFeatures.Compute(Input(Facts(facts, "")))[2].Unverified.ShouldBeTrue("a missing parser version is not a verified one");
        FundamentalFeatures.Compute(Input(Facts(facts, null!)))[2].Unverified.ShouldBeTrue();
        FundamentalFeatures.Compute(Input(Facts(facts, "xbrl-1-synthetic")))[2].Value.ShouldBe(0.2, Tolerance, "read and labelled, not dropped");
    }

    [Fact]
    public void Pledge_and_promoter_change_come_from_the_visible_group_and_the_one_a_quarter_earlier()
    {
        var holding = new ShareholdingAnswer(
            ShareholdingAnswer.Value,
            new ShareholdingSnapshot(Q, Broadcast, [new ShareholdingLine("promoter", 55.5m, 12.25m), new ShareholdingLine("public", 44.5m, null)], false),
            new ShareholdingSnapshot(Quarter(1), Broadcast.AddDays(-90), [new ShareholdingLine("promoter", 54.0m, 10m)], false),
            null);

        var cells = FundamentalFeatures.Compute(Input(Facts([]), holding: holding));

        cells[4].Value.ShouldBe(12.25, Tolerance);
        cells[5].Value.ShouldBe(1.5, Tolerance);
        FundamentalFeatures.Compute(Input(Facts([]), holding: holding with { Previous = null }))[5].ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory));
        FundamentalFeatures.Compute(Input(Facts([]), holding: NoHolding))[4].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling));
        FundamentalFeatures.Compute(Input(Facts([]), holding: NoHolding))[5].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling));
        var noPledge = new ShareholdingAnswer(ShareholdingAnswer.Value, new ShareholdingSnapshot(Q, Broadcast, [new ShareholdingLine("promoter", 55.5m, null)], false), null, null);
        FundamentalFeatures.Compute(Input(Facts([]), holding: noPledge))[4].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling), "a blank pledge is unknown, not zero");
    }

    private static List<ResultFactAnswer> TtmFacts(decimal eps = 2.5m)
    {
        // four consecutive quarters: the newest broadcast on 2026-07-20 12:00, the others earlier
        return
        [
            Fact(Quarter(0), ResultCodes.EpsBasic, ResultCodes.Consolidated, eps, At(7, 20, 12, 0)),
            Fact(Quarter(1), ResultCodes.EpsBasic, ResultCodes.Consolidated, eps, At(4, 20, 12, 0)),
            Fact(Quarter(2), ResultCodes.EpsBasic, ResultCodes.Consolidated, eps, At(1, 20, 12, 0)),
            Fact(Quarter(3), ResultCodes.EpsBasic, ResultCodes.Consolidated, eps, new DateTimeOffset(2025, 10, 20, 12, 0, 0, Ist)),
        ];
    }

    private static DailyClose[] Closes() =>
    [
        new(new DateOnly(2026, 7, 20), 90), new(new DateOnly(2026, 7, 21), 95), new(new DateOnly(2026, 7, 22), 100), new(new DateOnly(2026, 7, 23), 105),
        new(new DateOnly(2026, 7, 24), 110), new(new DateOnly(2026, 7, 27), 115), new(new DateOnly(2026, 7, 28), 120),
    ];

    [Fact]
    public void Pe_percentile_ranks_the_previous_close_over_ttm_eps_among_the_pe_of_the_earlier_sessions()
    {
        // TTM EPS 10 for every session shown: pe = 9.5 .. 12 over the last six sessions; the current pe is 12, the top of them: (5 below + half of 1 tie) / 6
        var cells = FundamentalFeatures.Compute(Input(Facts(TtmFacts()), closes: Closes()), peWindow: 6, peMinimum: 6);

        cells[6].Value.ShouldBe(100.0 * 5.5 / 6.0, Tolerance);
        cells[6].Unverified.ShouldBeTrue();
    }

    [Fact]
    public void Pe_percentile_needs_the_minimum_number_of_earlier_values_and_a_positive_ttm_and_the_previous_close()
    {
        FundamentalFeatures.Compute(Input(Facts(TtmFacts()), closes: Closes()), peWindow: 6, peMinimum: 7)[6].ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory));
        FundamentalFeatures.Compute(Input(Facts(TtmFacts()), closes: Closes()))[6].ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory), "the real window wants 1,000 values");
        FundamentalFeatures.Compute(Input(Facts(TtmFacts(-1m)), closes: Closes()), peWindow: 6, peMinimum: 6)[6].ShouldBe(FeatureCell.Unknown(FeatureReasons.Degenerate));
        FundamentalFeatures.Compute(Input(Facts(TtmFacts()), closes: Closes()[..6]), peWindow: 6, peMinimum: 6)[6].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoBars), "the previous session has no close");
        FundamentalFeatures.Compute(Input(Facts(TtmFacts().Take(3)), closes: Closes()), peWindow: 6, peMinimum: 6)[6].ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory), "three quarters are not a TTM");
    }

    [Fact]
    public void A_session_before_the_newest_quarter_was_public_uses_only_the_quarters_public_then()
    {
        // the newest quarter (EPS 5.0) is broadcast after the close of Friday 07-24: the earlier sessions see TTM 10 of the three older quarters plus the one before them, which is missing, so they have no value
        var facts = TtmFacts();
        facts[0] = Fact(Quarter(0), ResultCodes.EpsBasic, ResultCodes.Consolidated, 5.0m, At(7, 24, 16, 0));

        var cells = FundamentalFeatures.Compute(Input(Facts(facts), closes: Closes()), peWindow: 7, peMinimum: 1);

        // sessions 07-27 and 07-28 see four quarters (TTM 12.5): pe 9.2 and 9.6; the earlier five sessions see only three quarters and have no value
        cells[6].Value.ShouldBe(100.0 * 1.5 / 2.0, Tolerance, "current pe 120/12.5 = 9.6 ranks above 9.2 and ties itself");
    }

    [Fact]
    public void Without_any_fact_every_fact_based_value_is_no_filing_and_the_catalog_has_seven_columns_in_order()
    {
        var cells = FundamentalFeatures.Compute(Input(Facts([])));

        cells.Length.ShouldBe(7);
        FundamentalFeatureCatalog.Names.ShouldBe(["sue_yoy", "sue_ttm", "rev_growth_yoy", "margin_chg_yoy", "promoter_pledge_pct", "promoter_chg_qoq", "pe_pctile_5y"]);
        foreach (var i in new[] { 0, 1, 2, 3, 6 })
        {
            cells[i].ShouldBe(FeatureCell.Unknown(FeatureReasons.NoFiling), FundamentalFeatureCatalog.Names[i]);
        }

        FundamentalFeatureCatalog.Group.Problem().ShouldBeNull();
        ExtensionCatalogs.GroupOf("fu-1")!.Id.ShouldBe("fu");
        ExtensionCatalogs.GroupOf("ev-1")!.Id.ShouldBe("ev");
        ExtensionCatalogs.GroupOf("fu-2").ShouldBeNull();
    }

    [Fact]
    public void The_nan_counts_json_counts_per_feature_and_reason_and_the_unverified_values()
    {
        FeatureCell[][] rows =
        [
            [FeatureCell.Known(1, true), FeatureCell.Unknown(FeatureReasons.NoFiling)],
            [FeatureCell.Known(2, false), FeatureCell.Unknown(FeatureReasons.NoFiling)],
            [FeatureCell.Unknown(FeatureReasons.ShortHistory), FeatureCell.Unknown(FeatureReasons.NoBars)],
        ];

        ExtensionNanCounts.Json(["a", "b"], rows).ShouldBe("{\"nan\":{\"a\":{\"short_history\":1},\"b\":{\"no_bars\":1,\"no_filing\":2}},\"parser_unverified\":{\"a\":1,\"b\":0}}");
    }
}
