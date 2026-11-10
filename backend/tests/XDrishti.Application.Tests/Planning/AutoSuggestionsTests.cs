#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>AU-16 criteria 3 to 6: each suggestion says what, why and on what evidence; a missing figure reads unavailable and is never a pass; a ticket without the required evidence is listed as not suggested with why; reserve and rejected stay visible; every answer carries the basis and the no-order words.</summary>
public sealed class AutoSuggestionsTests
{
    private const string Cell = "ORB|5m|BUY|intraday|fixed|trend";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanEvidence Full() => new(Cell, 120, "range", "basis", 0.55, 0.21, [], null, 0.5, 64.2, new DateOnly(2026, 11, 6));

    private static PlanItemRow Row(long id, TicketGroup group, string symbol, PlanEvidence evidence, params string[] reasons) =>
        new(id, 1, new NewPlanItem((int)id, group, symbol, "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", reasons, evidence, []), TicketState.Proposed);

    private static GetAutoSuggestionsHandler Handler(params PlanItemRow[] items) => Handler(null, items);

    private static GetAutoSuggestionsHandler Handler(NewPlanRun? run, params PlanItemRow[] items)
    {
        var plan = new NewPlan("k", new DateOnly(2026, 11, 9), DateTimeOffset.UnixEpoch, 1, new string('c', 64), "Cell statistics only (shrunk replayed-history estimates); tickets are conditional on tomorrow's bars.", true, "t", DateTimeOffset.UnixEpoch, [])
        {
            BasisFlags = new PlanBasisFlags(1, "normal", [], "clear", "applied", 0, "known", 1, false, "gross", []) { ScopeHash = "abc" },
        };
        var store = Substitute.For<IPlanStore>();
        store.ListAsync(Arg.Any<DateOnly?>(), null, 1, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<PlanRow>>([new PlanRow(1, plan, PlanState.Published, 0, 0, 0)]));
        store.ItemsAsync(1, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<PlanItemRow>>(items));
        store.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<PlanRunRow>>(run is null ? [] : [new PlanRunRow(1, 1, run)]));
        return new GetAutoSuggestionsHandler(store);
    }

    [Fact]
    public async Task A_complete_primary_ticket_is_suggested_with_the_evidence_the_basis_and_the_no_order_words()
    {
        var result = await Handler(Row(1, TicketGroup.Primary, "AAA", Full())).HandleAsync(new GetAutoSuggestionsQuery(new DateOnly(2026, 11, 9)), Ct);

        var view = result.Value;
        var s = view.Suggestions.Single();
        (s.Symbol, s.Side, s.StrategyId).ShouldBe(("AAA", "BUY", "ORB"));
        s.Evidence.ShouldBe(new AutoEvidenceView("120", "64.2", "0.21", "trend", "2026-11-06"));
        s.Reasons.ShouldNotBeEmpty();
        view.Notice.ShouldBe("Suggestion only, no order is placed");
        view.Basis.ShouldContain("replayed-history");
        view.Basis.ShouldContain("tomorrow's bars");
        view.ScopeHash.ShouldBe("abc");
        view.NotSuggested.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_night_that_failed_closed_shows_no_older_plan_and_gives_the_reason()
    {
        var failed = new NewPlanRun(new DateOnly(2026, 11, 10), null, "failed_closed", "eod", "The end-of-day data is not fresh.", null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var result = await Handler(failed, Row(1, TicketGroup.Primary, "AAA", Full())).HandleAsync(new GetAutoSuggestionsQuery(null), Ct);

        result.Error.Code.ShouldBe("auto.night_failed");
        result.Error.Message.ShouldContain("end-of-day data is not fresh");
        result.Error.Message.ShouldContain("No suggestions are shown");
    }

    [Fact]
    public async Task A_failed_attempt_for_a_night_that_has_a_plan_does_not_hide_the_plan()
    {
        var failed = new NewPlanRun(new DateOnly(2026, 11, 9), null, "failed_closed", "eod", "late retry failed", null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var result = await Handler(failed, Row(1, TicketGroup.Primary, "AAA", Full())).HandleAsync(new GetAutoSuggestionsQuery(new DateOnly(2026, 11, 9)), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_primary_ticket_missing_n_eff_or_the_as_of_is_not_suggested_and_says_which_figure_is_unavailable()
    {
        var thin = Full() with { NEff = null, StatsAsOf = null };

        var view = (await Handler(Row(1, TicketGroup.Primary, "AAA", thin)).HandleAsync(new GetAutoSuggestionsQuery(new DateOnly(2026, 11, 9)), Ct)).Value;

        view.Suggestions.ShouldBeEmpty();
        var n = view.NotSuggested.Single();
        n.Why.ShouldBe("Not suggested: n_eff, data as-of unavailable.");
        (n.Evidence.NEff, n.Evidence.DataAsOf, n.Evidence.N).ShouldBe(("unavailable", "unavailable", "120"));
    }

    [Fact]
    public async Task A_missing_expectancy_or_no_trades_reads_unavailable_and_is_never_suggested()
    {
        var noExpectancy = Full() with { ConservativeExpectancyR = null };
        var noTrades = Full() with { Trades = 0 };

        var view = (await Handler(Row(1, TicketGroup.Primary, "AAA", noExpectancy), Row(2, TicketGroup.Primary, "BBB", noTrades)).HandleAsync(new GetAutoSuggestionsQuery(new DateOnly(2026, 11, 9)), Ct)).Value;

        view.Suggestions.ShouldBeEmpty();
        view.NotSuggested.Select(n => n.Evidence.ConservativeExpectancyR).ShouldBe(["unavailable", "0.21"]);
        view.NotSuggested[1].Evidence.N.ShouldBe("unavailable");
    }

    [Fact]
    public async Task Reserve_and_rejected_stay_visible_with_their_reasons_in_words()
    {
        var view = (await Handler(
            Row(1, TicketGroup.Reserve, "BBB", Full(), "same_bet_better_ticket"),
            Row(2, TicketGroup.Rejected, "CCC", Full() with { NEff = null }, "sample:fail", "event:unavailable")).HandleAsync(new GetAutoSuggestionsQuery(new DateOnly(2026, 11, 9)), Ct)).Value;

        view.Reserve.Single().Reasons.ShouldBe(["The same bet as a better ticket."]);
        view.Rejected.Single().Reasons.ShouldBe(["Failed: the cell has too few trades.", "Could not be checked (unavailable): an event excludes the instrument."]);
        view.Rejected.Single().Evidence.NEff.ShouldBe("unavailable");
        view.Suggestions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_date_with_no_plan_is_not_found()
    {
        var store = Substitute.For<IPlanStore>();
        store.ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<PlanRow>>([]));
        store.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<PlanRunRow>>([]));

        var result = await new GetAutoSuggestionsHandler(store).HandleAsync(new GetAutoSuggestionsQuery(null), Ct);

        result.Error.Code.ShouldBe("auto.no_plan");
    }

    [Theory]
    [InlineData("cell_state:fail", "Failed: the cell is not in a good enough state.")]
    [InlineData("book:unavailable", "Could not be checked (unavailable): the open book is not known.")]
    [InlineData("gate_no_trade", "The market gate says no trade.")]
    [InlineData("brand_new_code:fail", "Failed: brand new code.")]
    public void Reason_codes_read_in_words(string code, string words) => AutoSuggestionRules.ReasonInWords(code).ShouldBe(words);

    [Fact]
    public void A_cell_that_spans_regimes_reads_all_regimes_and_a_key_that_cannot_be_read_reads_unavailable()
    {
        AutoSuggestionRules.Evidence(Full() with { CellKey = "ORB|5m|BUY|intraday|fixed|*" }).Figures.Regime.ShouldBe("all regimes");
        var (figures, missing) = AutoSuggestionRules.Evidence(Full() with { CellKey = "garbage" });

        figures.Regime.ShouldBe("unavailable");
        missing.ShouldBe(["regime"]);
    }
}
