using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Application.OptionReports;
using XDrishti.Application.Tests.OptionQuality;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Tests.OptionReports;

/// <summary>Spec P14-16 criteria 1 to 4 at the handler: the honest states, the closed reasons, the bounded reads, the errors.</summary>
public sealed class OptionReportsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Guid Nifty = QualityWorld.Nifty;

    private static readonly string Hash = new('a', 64);

    private readonly FakeReportReader _reader = new();
    private readonly FakeBackfillLog _log = new();
    private readonly FakeQualityStore _quality = new();
    private readonly FakeReference _reference = new();
    private readonly FakeSpreadModels _spreads = new();
    private readonly FakeEvaluationReader _evaluations = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private GetOptionArchiveSummaryHandler Summary() => new(_reader, _log, _quality, _reference, _spreads, _clock);

    private ListExpressionRunsHandler Runs() => new(_reader);

    private ListPairFormationsHandler Formations() => new(_reader, _clock);

    private GetP14VerdictListHandler Verdicts() => new(_evaluations);

    private static OptionQualityRow Row(DateOnly day, string code, QualityStatus status, string? reason = null) => new(day, Nifty, "chain", null, null, new QualityCheck(code, status, status == QualityStatus.Unavailable ? null : 0.9, 0.95, reason, 10, null));

    private static ExpressionRunStored Run(string id, string profile, int trades, int paired, double? fallback, int derived = 0, int spread = 0, Dictionary<string, int>? sizable = null, string unpaired = "{}", DateTimeOffset? at = null) =>
        new(id, new string('c', 64), profile, "remark-1", at ?? Now, trades, paired, unpaired, fallback, ["basis: gross"], derived, spread, sizable ?? []);

    private static string Detail(int rejected, string table = "[]", string halfLives = "[]") => $"{{\"m\":10,\"unavailable\":0,\"rejected\":{rejected},\"selected\":2,\"half_lives\":{halfLives},\"table\":{table}}}";

    private static PairFormationStored Formation(string quarter, string detail, DateOnly? from = null, DateOnly? to = null) =>
        new(quarter.PadRight(64, '0'), "pairs:intraday-1", quarter, from ?? new DateOnly(2026, 4, 1), to ?? new DateOnly(2026, 6, 30), Now, 10, 8, 2, 2, detail);

    [Fact]
    public async Task An_empty_archive_is_an_empty_list_and_every_read_is_bounded()
    {
        var result = await Summary().HandleAsync(new GetOptionArchiveSummaryQuery(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Underlyings.ShouldBeEmpty();
        _reader.Asked.ShouldBe(["underlyings 2025-10-01..2026-11-04 limit 200"]);
    }

    [Fact]
    public async Task A_checked_underlying_shows_its_counts_dates_gaps_and_a_missing_backfill_and_model_as_unavailable_with_their_reason()
    {
        _reference.Symbols[Nifty] = "NSE:NIFTY";
        _reader.Underlyings.Add(Nifty);
        await _quality.AddResultsAsync(1, [
            Row(new DateOnly(2026, 10, 6), "coverage", QualityStatus.Pass), Row(new DateOnly(2026, 10, 6), "parity", QualityStatus.Pass),
            Row(new DateOnly(2026, 10, 7), "coverage", QualityStatus.Unavailable, "not_recorded"), Row(new DateOnly(2026, 10, 7), "parity", QualityStatus.Unavailable, "not_recorded")], Ct);

        var u = (await Summary().HandleAsync(new GetOptionArchiveSummaryQuery(), Ct)).Value.Underlyings.Single();

        u.Underlying.ShouldBe("NSE:NIFTY");
        u.RecordedSessions.ShouldBe(ReportedCount.Of(1));
        u.UsableSessions.ShouldBe(ReportedCount.Of(1));
        u.FirstDate.ShouldBe(ReportedDate.Of(new DateOnly(2026, 10, 6)));
        u.Gaps.Outcomes.Select(o => (o.Outcome, o.Reason, o.Sessions)).ShouldBe([("pass", null, 1), ("unavailable", "not_recorded", 1)]);
        u.Backfill.State.ShouldBe("unavailable");
        u.Backfill.Reason.ShouldBe("not_recorded");
        u.Backfill.Done.ShouldBeNull();
        u.SpreadModelSessions.ShouldBe(ReportedCount.None("no_model"));
    }

    [Fact]
    public async Task An_underlying_known_only_from_the_backfill_has_no_session_figures_and_never_a_zero()
    {
        _reference.Symbols[Nifty] = "NSE:NIFTY";
        _log.Add(Nifty, 96, 3, 1, 2);
        _spreads.Models[Nifty] = new HalfSpreadModel(new string('d', 64), Nifty, 61, new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 1), []);

        var u = (await Summary().HandleAsync(new GetOptionArchiveSummaryQuery(), Ct)).Value.Underlyings.Single();
        var json = JsonSerializer.Serialize(u, Web);

        u.RecordedSessions.State.ShouldBe("unavailable");
        u.UsableSessions.Value.ShouldBeNull();
        u.Gaps.State.ShouldBe("unavailable");
        u.Backfill.Done.ShouldBe(100);
        u.Backfill.GivenUp.ShouldBe(2);
        u.SpreadModelSessions.ShouldBe(ReportedCount.Of(61));
        json.ShouldContain("\"recordedSessions\":{\"state\":\"unavailable\",\"value\":null,\"reason\":\"not_recorded\"}");
    }

    [Fact]
    public async Task Underlyings_are_listed_by_symbol()
    {
        var bank = QualityWorld.BankNifty;
        _reference.Symbols[Nifty] = "NSE:NIFTY";
        _reference.Symbols[bank] = "NSE:BANKNIFTY";
        _log.Add(Nifty, 1, 0, 0, 0);
        _log.Add(bank, 1, 0, 0, 0);

        var result = await Summary().HandleAsync(new GetOptionArchiveSummaryQuery(), Ct);

        result.Value.Underlyings.Select(u => u.Underlying).ShouldBe(["NSE:BANKNIFTY", "NSE:NIFTY"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task A_limit_outside_1_to_100_is_options_invalid_query(int limit)
    {
        var result = await Runs().HandleAsync(new ListExpressionRunsQuery(null, limit), Ct);

        result.Error.Code.ShouldBe("options.invalid_query");
        _reader.Asked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("nothex")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("")]
    public async Task A_profile_that_is_not_64_lower_case_hex_is_options_invalid_query(string profile)
    {
        var result = await Runs().HandleAsync(new ListExpressionRunsQuery(profile, null), Ct);

        result.Error.Code.ShouldBe("options.invalid_query");
    }

    [Fact]
    public async Task No_stored_run_is_an_empty_list_with_the_reason_no_runs_and_the_default_limit_is_20_and_100_is_allowed()
    {
        var empty = await Runs().HandleAsync(new ListExpressionRunsQuery(null, null), Ct);
        var most = await Runs().HandleAsync(new ListExpressionRunsQuery(Hash, 100), Ct);

        empty.Value.State.ShouldBe("unavailable");
        empty.Value.Reason.ShouldBe("no_runs");
        empty.Value.Items.ShouldBeEmpty();
        most.IsSuccess.ShouldBeTrue();
        _reader.Asked.ShouldBe(["runs profile all limit 20", $"runs profile {Hash} limit 100"]);
    }

    [Fact]
    public async Task A_run_shows_its_counts_unpaired_reasons_and_the_shares_of_the_paired_trades()
    {
        _reader.Runs.Add(Run(Hash, Hash, 50, 20, 0.1, derived: 5, spread: 10, sizable: new() { ["sizable"] = 15, ["lot_unknown"] = 5 }, unpaired: "{\"no_series\":7,\"illiquid\":3}"));

        var run = (await Runs().HandleAsync(new ListExpressionRunsQuery(null, null), Ct)).Value.Items.Single();

        run.Trades.ShouldBe(50);
        run.Paired.ShouldBe(20);
        run.Unpaired.Select(u => (u.Reason, u.Trades)).ShouldBe([("illiquid", 3), ("no_series", 7)]);
        run.FallbackShare.ShouldBe(ReportedShare.Of(0.1));
        run.ExpiryDerivedShare.ShouldBe(ReportedShare.Of(0.25));
        run.SpreadFromRecentArchiveShare.ShouldBe(ReportedShare.Of(0.5));
        run.LotFeasibility.Select(l => (l.Feasibility, l.Paired)).ShouldBe([("sizable", 15), ("not_sizable", 0), ("lot_unknown", 5), ("delta_unavailable", 0), ("risk_invalid", 0), ("n/a", 0)]);
        run.LotFeasibility[0].Share.ShouldBe(ReportedShare.Of(0.75));
    }

    [Fact]
    public async Task A_run_that_paired_nothing_has_every_share_unavailable_with_no_pairs_and_never_a_zero()
    {
        _reader.Runs.Add(Run(Hash, Hash, 4, 0, null, unpaired: "{\"no_series\":4}"));

        var run = (await Runs().HandleAsync(new ListExpressionRunsQuery(null, null), Ct)).Value.Items.Single();
        var json = JsonSerializer.Serialize(run, Web);

        new[] { run.FallbackShare, run.ExpiryDerivedShare, run.SpreadFromRecentArchiveShare }.ShouldAllBe(s => s.State == "unavailable" && s.Reason == "no_pairs" && s.Value == null);
        run.LotFeasibility.ShouldAllBe(l => l.Share.State == "unavailable");
        json.ShouldContain("\"fallbackShare\":{\"state\":\"unavailable\",\"value\":null,\"reason\":\"no_pairs\"}");
    }

    [Fact]
    public async Task One_run_by_id_and_an_unknown_or_malformed_id_is_research_not_found()
    {
        _reader.Runs.Add(Run(Hash, Hash, 1, 1, 0));
        var handler = new GetExpressionRunHandler(_reader);

        var found = await handler.HandleAsync(new GetExpressionRunQuery(Hash), Ct);
        var unknown = await handler.HandleAsync(new GetExpressionRunQuery(new string('b', 64)), Ct);
        var malformed = await handler.HandleAsync(new GetExpressionRunQuery("x"), Ct);

        found.IsSuccess.ShouldBeTrue();
        unknown.Error.Code.ShouldBe("research.not_found");
        malformed.Error.Code.ShouldBe("research.not_found");
        _reader.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_formation_shows_its_counts_the_threshold_at_the_last_rejected_rank_and_the_half_life_distribution()
    {
        var table = "[{\"rank\":1,\"threshold\":0.005,\"rejected\":true},{\"rank\":2,\"threshold\":0.01,\"rejected\":true},{\"rank\":3,\"threshold\":0.015,\"rejected\":false}]";
        _reader.Formations.Add(Formation("2026Q2", Detail(2, table, "[{\"pair\":\"A|B\",\"sessions\":4.0},{\"pair\":\"C|D\",\"sessions\":10.0},{\"pair\":\"E|F\",\"sessions\":6.0}]")));

        var list = (await Formations().HandleAsync(new ListPairFormationsQuery(null, null), Ct)).Value;
        var f = list.Items.Single();

        f.Candidates.ShouldBe(10);
        f.Selected.ShouldBe(2);
        f.BhThreshold.ShouldBe(ReportedShare.Of(0.01));
        f.HalfLives.Count.ShouldBe(3);
        (f.HalfLives.Min, f.HalfLives.Median, f.HalfLives.Max).ShouldBe((4.0, 6.0, 10.0));
        _reader.Asked.ShouldBe(["formations 2025-10-01..2026-11-04 limit 401"]);
    }

    [Fact]
    public async Task A_formation_with_no_rejection_or_no_selected_pair_says_unavailable_with_the_reason()
    {
        _reader.Formations.Add(Formation("2026Q2", Detail(0)));
        _reader.Formations.Add(Formation("2026Q3", "{\"half_lives\":[]}"));

        var items = (await Formations().HandleAsync(new ListPairFormationsQuery(null, null), Ct)).Value.Items;

        items[0].BhThreshold.ShouldBe(ReportedShare.None("no_rejection"));
        items[0].HalfLives.Reason.ShouldBe("no_pairs");
        items[0].HalfLives.Count.ShouldBeNull();
        items[1].BhThreshold.ShouldBe(ReportedShare.None("not_recorded"));
    }

    [Fact]
    public async Task No_formation_is_an_empty_list_and_a_bad_range_is_options_invalid_query()
    {
        var empty = await Formations().HandleAsync(new ListPairFormationsQuery(null, null), Ct);
        var inverted = await Formations().HandleAsync(new ListPairFormationsQuery(new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 1)), Ct);
        var over = await Formations().HandleAsync(new ListPairFormationsQuery(new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1)), Ct);
        var exact = await Formations().HandleAsync(new ListPairFormationsQuery(new DateOnly(2026, 6, 1).AddDays(-399), new DateOnly(2026, 6, 1)), Ct);

        empty.Value.Items.ShouldBeEmpty();
        empty.Value.Reason.ShouldBe("no_runs");
        inverted.Error.Code.ShouldBe("options.invalid_query");
        over.Error.Code.ShouldBe("options.invalid_query");
        exact.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task More_than_400_formations_in_the_range_say_truncated_and_list_400()
    {
        for (var i = 0; i < 401; i++)
        {
            _reader.Formations.Add(Formation("2026Q2", Detail(0)));
        }

        var list = (await Formations().HandleAsync(new ListPairFormationsQuery(null, null), Ct)).Value;

        list.Items.Count.ShouldBe(400);
        list.Truncated.ShouldBeTrue();
        _reader.Asked.ShouldBe(["formations 2025-10-01..2026-11-04 limit 401"]);
    }

    [Fact]
    public void The_backfill_counts_follow_the_progress_rule_done_includes_empty_and_no_data_and_given_up_uses_the_key_state()
    {
        var log = new FakeBackfillLog();
        log.Add(Nifty, 5, 2, 1, 3);

        var counts = BackfillKeyCounts.From(log.States)[Nifty];

        (counts.Done, counts.Empty, counts.NoData, counts.GivenUp).ShouldBe((8, 2, 1, 3));
    }

    [Fact]
    public async Task The_verdict_list_shows_the_stored_records_with_their_missing_quantities_and_none_stored_for_the_rest()
    {
        _evaluations.Records.Add(FakeEvaluationReader.Record("expr:option_buy", "NOT_EVALUABLE", "O", 6, 2, ["folds 2 of 6"]));
        _evaluations.Records.Add(FakeEvaluationReader.Record("pairs:intraday-1", "INCONCLUSIVE", "S", 6, 6, []));

        var list = (await Verdicts().HandleAsync(new GetP14VerdictListQuery([]), Ct)).Value;

        list.Families.Select(f => f.Family).ShouldBe(["expr:option_buy", "expr:debit_spread", "expr:credit_spread", "expr:iron_condor", "pairs:intraday-1", "ext:iv-1"]);
        var buy = list.Families[0];
        (buy.State, buy.Verdict, buy.Protocol, buy.FoldsEvaluable, buy.FoldsTotal).ShouldBe(("stored", "NOT_EVALUABLE", "O", 2, 6));
        buy.Missing.ShouldBe(["folds 2 of 6"]);
        buy.Limits.ShouldNotBeEmpty();
        list.Families[4].Verdict.ShouldBe("INCONCLUSIVE");
        list.Families.Where(f => f.Family is "expr:debit_spread" or "expr:credit_spread" or "expr:iron_condor" or "ext:iv-1").ShouldAllBe(f => f.State == "none stored" && f.Verdict == null);
        _evaluations.Asked.ShouldAllBe(a => a.Limit == 1);
    }

    [Fact]
    public async Task A_cut_family_is_cut_and_is_not_read_and_an_unknown_cut_name_is_refused()
    {
        _evaluations.Records.Add(FakeEvaluationReader.Record("expr:iron_condor", "DROP", "R", 6, 6, []));

        var list = (await Verdicts().HandleAsync(new GetP14VerdictListQuery(["expr:iron_condor", "ext:iv-1"]), Ct)).Value;
        var unknown = await Verdicts().HandleAsync(new GetP14VerdictListQuery(["basis:carry-1"]), Ct);

        list.Families.Where(f => f.State == "cut").Select(f => f.Family).ShouldBe(["expr:iron_condor", "ext:iv-1"]);
        list.Families.Single(f => f.Family == "expr:iron_condor").Verdict.ShouldBeNull();
        _evaluations.Asked.ShouldNotContain(a => a.Family == "expr:iron_condor");
        unknown.IsFailure.ShouldBeTrue();
        unknown.Error.Code.ShouldBe("options.unknown_family");
    }

    [Fact]
    public async Task The_report_carries_the_same_figures_as_the_routes_the_limits_block_and_a_text_that_prints_no_digit_for_a_non_value()
    {
        var handler = new GetOptionsReportHandler(Summary(), Runs(), Formations(), Verdicts());

        var report = (await handler.HandleAsync(new GetOptionsReportQuery([]), Ct)).Value;

        var route = await Summary().HandleAsync(new GetOptionArchiveSummaryQuery(), Ct);
        JsonSerializer.Serialize(report.Data.Archive).ShouldBe(JsonSerializer.Serialize(route.Value));
        report.Data.Limits.Count.ShouldBeGreaterThan(3);
        report.Text.ShouldBe(
            """
            options and stat-arb report (read only)

            option archive
              nothing stored: unavailable (not_recorded)

            expression runs (newest first)
              unavailable (no_runs)

            pair formations (oldest quarter first)
              unavailable (no_runs)

            verdicts (newest stored record per family)
              expr:option_buy: none stored
              expr:debit_spread: none stored
              expr:credit_spread: none stored
              expr:iron_condor: none stored
              pairs:intraday-1: none stored
              ext:iv-1: none stored

            limits
              read only: counts, shares, dates and verdict words; no price, IV, Greek, bar or quote is shown
              unavailable names its reason (not_recorded, no_model, no_runs, no_pairs, no_rejection) and is never a number
              expression counts and shares are of the whole stored run, hold-out included; no figure of return is shown here
              the archive counts rest on the stored quality results over all checked history (a session is usable when coverage passes and parity passes or warns); the gaps list includes the pass sessions; a session not yet checked is not counted
              verdicts are the newest stored record per family; a NOT_EVALUABLE record names what is missing; the basis families are not built and are not listed

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_printed_report_shows_each_state_in_words()
    {
        _reference.Symbols[Nifty] = "NSE:NIFTY";
        _log.Add(Nifty, 96, 3, 1, 2);
        _reader.Runs.Add(Run(Hash, Hash, 4, 0, null, unpaired: "{\"no_series\":4}"));
        _reader.Formations.Add(Formation("2026Q2", Detail(0)));
        _evaluations.Records.Add(FakeEvaluationReader.Record("expr:option_buy", "NOT_EVALUABLE", "O", 6, 2, ["folds 2 of 6"]));
        var handler = new GetOptionsReportHandler(Summary(), Runs(), Formations(), Verdicts());

        var text = (await handler.HandleAsync(new GetOptionsReportQuery(["ext:iv-1"]), Ct)).Value.Text;

        text.ShouldContain("  NSE:NIFTY: recorded unavailable (not_recorded), usable unavailable (not_recorded), first unavailable (not_recorded), last unavailable (not_recorded)");
        text.ShouldContain("    gaps: unavailable (not_recorded)");
        text.ShouldContain("    backfill keys: done 100, empty 3, no data 1, given up 2");
        text.ShouldContain("    spread model: sessions unavailable (no_model), first unavailable (no_model), last unavailable (no_model)");
        text.ShouldContain("unpaired no_series 4");
        text.ShouldContain("model-price share unavailable (no_pairs), derived-expiry share unavailable (no_pairs), recent-archive-spread share unavailable (no_pairs)");
        text.ShouldContain("BH threshold unavailable (no_rejection)");
        text.ShouldContain("    half-life (sessions): unavailable (no_pairs)");
        text.ShouldContain("  expr:option_buy: NOT_EVALUABLE, trials 3, folds 2 of 6, hold-out not read");
        text.ShouldContain("    missing: folds 2 of 6");
        text.ShouldContain("  ext:iv-1: cut");
        text.ShouldContain("  expr:debit_spread: none stored");
        text.ShouldContain("  expr:credit_spread: none stored");
        text.ShouldContain("  expr:iron_condor: none stored");
        text.ShouldContain("  pairs:intraday-1: none stored");
    }

    [Fact]
    public async Task An_unknown_cut_family_fails_the_report()
    {
        var handler = new GetOptionsReportHandler(Summary(), Runs(), Formations(), Verdicts());

        var result = await handler.HandleAsync(new GetOptionsReportQuery(["nope"]), Ct);

        result.IsFailure.ShouldBeTrue();
    }
}
