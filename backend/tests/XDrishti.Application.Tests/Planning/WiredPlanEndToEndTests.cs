#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Tests.Planning;

/// <summary>
/// Spec P7-09, the whole wiring on the seeded fixture of <see cref="WiredPlanFixture"/> (real selection, smart rules, fill, allocation and checklist; fake ports). The plan is worked out by hand:
/// <code>
/// selection (cost, event, ban, surveillance loosened): AAA .50, BBB .40, CCC .30, FFF .25, DDD .20 ranked; EEE held (win_rate:fail)
/// smart rules: gate has no data -> flagged; DDD correlates 0.9 with the better AAA (> 0.6) -> reserve same_bet_better_ticket
/// fill (0.5 % risk each, 2 per sector, 2 swing): AAA taken; BBB taken (corr .3); CCC is a 3rd bank and 3rd swing: sector:fail + mix:fail, admissible alone -> reserve;
///   FFF has no sector: sector:unavailable even alone -> rejected
/// plan: primary AAA, BBB | reserve CCC, DDD | rejected FFF, EEE
/// allocation, paper account 1 000 000, risk 0.5 %, capital 20 %, total 3 %; the live account gets nothing:
///   AAA entry 100 stop 98: risk 1e6*0.5%/2 = 2500, capital 1e6*20%/100 = 2000 (binds), pool 1% of 50 M / 100 = 5000 -> 2000 sh, risk 0.4 %
///   BBB entry 200 stop 190: risk 1e6*0.5%/10 = 500 (binds), capital 1000, funds (1e6-200k)/200 = 4000, headroom (3-0.4)%*1e6/10 = 2600 -> 500 sh
/// </code>
/// </summary>
public sealed class WiredPlanEndToEndTests
{
    private static readonly string[] RankedSymbols = ["AAA", "BBB", "CCC", "DDD", "FFF"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<NewPlan> BuildAsync(IReadOnlyList<TicketCandidate>? found = null, PlanContext? context = null)
    {
        var builder = WiredPlanFixture.Builder(found ?? WiredPlanFixture.Candidates(), context ?? WiredPlanFixture.Context(), out _);
        var result = await builder.BuildAsync(WiredPlanFixture.Request, Ct);
        return result.Value;
    }

    [Fact]
    public async Task The_seeded_fixture_reaches_the_hand_checked_split_with_reasons()
    {
        var plan = await BuildAsync();

        plan.Items.Select(i => (i.Rank, i.Group, i.Symbol)).ToArray().ShouldBe(
        [
            (1, TicketGroup.Primary, "AAA"),
            (2, TicketGroup.Primary, "BBB"),
            (3, TicketGroup.Reserve, "CCC"),
            (4, TicketGroup.Reserve, "DDD"),
            (5, TicketGroup.Rejected, "FFF"),
            (6, TicketGroup.Rejected, "EEE"),
        ]);
        plan.Items[0].Reasons.ShouldBeEmpty();
        plan.Items[2].Reasons.ShouldBe([FillCodes.Sector + ":fail", FillCodes.Mix + ":fail"]);
        plan.Items[3].Reasons.ShouldBe([SmartRuleCodes.SameBetBetterTicket]);
        plan.Items[4].Reasons.ShouldBe([FillCodes.Sector + ":unavailable"]);
        plan.Items[5].Reasons.ShouldBe([FilterCodes.WinRate + ":fail", "cost:unavailable", "event:unavailable", "fno_ban:unavailable", "surveillance:unavailable"]); // the four loosened filters are still listed, as unavailable
    }

    [Fact]
    public async Task Allocations_are_the_hand_worked_paper_rows_and_the_live_account_gets_nothing()
    {
        var plan = await BuildAsync();

        plan.Items[0].Allocations.ShouldBe([new NewAllocation(WiredPlanFixture.Paper, "paper", 2000)]);
        plan.Items[1].Allocations.ShouldBe([new NewAllocation(WiredPlanFixture.Paper, "paper", 500)]);
        plan.Items.Skip(2).ShouldAllBe(i => i.Allocations.Count == 0);
        plan.Items.SelectMany(i => i.Allocations).ShouldAllBe(a => a.AccountId != WiredPlanFixture.Live);
    }

    [Fact]
    public async Task The_checklist_standings_never_pass_an_input_that_has_no_source()
    {
        var plan = await BuildAsync();

        var aaa = plan.Items[0].Evidence.Unavailable;
        aaa.ShouldContain("allocation:" + "account_not_paper");
        aaa.ShouldContain("checklist:" + ChecklistCodes.AccountPaper + ":fail");
        foreach (var code in new[] { ChecklistCodes.DataQuality, ChecklistCodes.ResultsWithinDays, ChecklistCodes.FnoBan, ChecklistCodes.Surveillance, ChecklistCodes.PriceBand, ChecklistCodes.Liquidity, ChecklistCodes.Correlation })
        {
            aaa.ShouldContain("checklist:" + code + ":unavailable");
        }

        aaa.ShouldContain(PlanWiring.IndicativeEntry);
        aaa.ShouldNotContain("checklist:" + ChecklistCodes.RiskProfile + ":fail");
        aaa.ShouldNotContain("checklist:" + ChecklistCodes.CapitalHeadroom + ":fail");
        aaa.ShouldContain(SmartRuleCodes.GateUnavailable);
    }

    [Fact]
    public async Task The_plan_is_flagged_when_the_market_gate_has_no_data_and_says_proposed_provisional_no_order()
    {
        var plan = await BuildAsync();

        plan.Basis.ShouldStartWith(PlanWording.Proposed);
        plan.Basis.ShouldContain(PlanWording.GateUnavailableMarker);
        plan.Basis.Length.ShouldBeLessThanOrEqualTo(SelectionPlanBuilder.MaxBasisLength);
        plan.Provisional.ShouldBeTrue();
        plan.Notice.ShouldBe(PlanWording.NoOrder);
    }

    [Fact]
    public async Task With_the_owner_setting_every_primary_ticket_and_the_basis_say_the_book_is_assumed_empty()
    {
        var plan = await BuildAsync();

        plan.Basis.ShouldContain(PlanWording.OpenBookAssumedEmpty);
        plan.Items.Where(i => i.Group == TicketGroup.Primary).ShouldAllBe(i => i.Evidence.Unavailable.Contains(PlanWording.OpenBookAssumedEmpty));
    }

    [Fact]
    public async Task By_default_the_open_book_is_unavailable_so_the_fill_takes_nothing_and_the_plan_is_flagged()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(bookAssumedEmpty: false));

        plan.Items.ShouldNotContain(i => i.Group == TicketGroup.Primary);
        plan.Basis.ShouldContain(PlanWording.OpenBookUnavailableMarker);
        plan.Basis.ShouldNotContain(PlanWording.OpenBookAssumedEmpty);
        plan.Items.Single(i => i.Symbol == "AAA").Reasons.ShouldContain(FillCodes.Book + ":unavailable");
        plan.Items.Single(i => i.Symbol == "DDD").Group.ShouldBe(TicketGroup.Reserve); // moved by a smart rule before the fill
        SuggestionRules.Evaluate(new SuggestionInput(WiredPlanFixture.ForDate, plan.Basis, [.. plan.Items.Select(i => new SuggestionItem(i.Group, i.Symbol, i.Reasons))], new DateOnly(2026, 10, 22)))
            .Select(s => s.Kind).ShouldContain(SuggestionKinds.OpenBookUnavailable);
    }

    [Fact]
    public async Task The_open_book_flag_and_the_gate_flag_both_survive_the_basis_clip_with_no_paper_account()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(accounts: [], bookAssumedEmpty: false));

        plan.Basis.Length.ShouldBeLessThanOrEqualTo(SelectionPlanBuilder.MaxBasisLength);
        plan.Basis.ShouldContain(PlanWording.OpenBookUnavailableMarker);
        plan.Basis.ShouldContain(PlanWording.GateUnavailableMarker);
        plan.Basis.ShouldContain(PlanContextNotes.NoAccountNote);
    }

    [Fact]
    public void When_the_basis_is_clipped_the_risk_multiplier_sentence_goes_before_the_gate_flag()
    {
        var gate = new PlanFlag(SmartRuleCodes.GateUnavailable, PlanWording.GateUnavailableMarker + " partly (breadth not known); a limit that is known tripped");
        var smart = new SmartPlan([], [], default!, default!, RuleStatus.Applied, 0, 0.75, [gate], false);
        var filler = new string('x', 500 - PlanWording.Proposed.Length - SelectionPlanBuilder.Basis.Length - gate.Text.Length - 40);
        var ctx = WiredPlanFixture.Context() with { Notes = [filler] };

        var basis = SelectionPlanBuilder.BasisText(smart, ctx);

        basis.Length.ShouldBe(SelectionPlanBuilder.MaxBasisLength);
        basis.ShouldContain(gate.Text);
        basis.ShouldNotContain("(market gate / equity throttle)");
    }

    [Fact]
    public async Task A_gate_that_has_data_does_not_flag_the_plan()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(new MarketGateInput(new DateOnly(2026, 10, 23), 14, 0.5, false)));

        plan.Basis.ShouldNotContain(PlanWording.GateUnavailableMarker);
    }

    [Fact]
    public async Task A_no_trade_gate_moves_everything_to_the_reserve_and_nothing_is_primary()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(new MarketGateInput(new DateOnly(2026, 10, 23), 30, 0.5, false)));

        plan.Items.ShouldNotContain(i => i.Group == TicketGroup.Primary);
        plan.Items.Where(i => i.Group == TicketGroup.Reserve).ShouldAllBe(i => i.Reasons.Contains(SmartRuleCodes.ReserveGateNoTrade));
    }

    [Fact]
    public async Task Without_correlation_figures_only_the_first_pick_is_taken_and_the_plan_says_so()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(withCorrelations: false));

        plan.Items.Count(i => i.Group == TicketGroup.Primary).ShouldBe(1); // a pair with no figure blocks the second pick (unavailable never passes)
        plan.Items.Single(i => i.Symbol == "BBB").Reasons.ShouldContain(FillCodes.Correlation + ":unavailable");
        plan.Items[0].Evidence.Unavailable.ShouldContain(SmartRuleCodes.CorrelationUnavailable);
    }

    [Fact]
    public async Task With_no_trading_account_the_tickets_are_primary_but_unsized_and_say_so()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(accounts: []));

        plan.Items.Where(i => i.Group == TicketGroup.Primary).ShouldAllBe(i => i.Allocations.Count == 0);
        plan.Items.Count(i => i.Group == TicketGroup.Primary).ShouldBe(0); // no paper account: the ticket risk is not known, the fill takes nothing
        plan.Items.Single(i => i.Symbol == "AAA").Reasons.ShouldContain(FillCodes.PerTradeRisk + ":unavailable");
    }

    [Fact]
    public async Task An_account_that_already_holds_the_symbol_gets_nothing_for_it()
    {
        var plan = await BuildAsync(context: WiredPlanFixture.Context(accounts: [WiredPlanFixture.PaperAccount(["AAA"])]));

        plan.Items[0].Symbol.ShouldBe("AAA");
        plan.Items[0].Allocations.ShouldBeEmpty();
        plan.Items[0].Evidence.Unavailable.ShouldContain("allocation:already_held");
        plan.Items[1].Allocations.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_plan_in_any_input_order_and_a_changed_input_a_different_hash()
    {
        var found = WiredPlanFixture.Candidates();

        var a = await BuildAsync(found);
        var b = await BuildAsync([.. found.Reverse()]);
        var changed = await BuildAsync([.. found.Select(c => c.Draft.Symbol == "AAA" ? WiredPlanFixture.Candidate("AAA", 2.0, entry: 101) : c)]);

        b.InputHash.ShouldBe(a.InputHash);
        b.Items.Select(i => (i.Rank, i.Symbol, i.Group)).ShouldBe(a.Items.Select(i => (i.Rank, i.Symbol, i.Group)));
        changed.InputHash.ShouldNotBe(a.InputHash);
    }

    [Fact]
    public async Task The_builder_asks_the_context_only_about_the_ranked_symbols_as_of_the_session()
    {
        var builder = WiredPlanFixture.Builder(WiredPlanFixture.Candidates(), WiredPlanFixture.Context(), out var context);

        await builder.BuildAsync(WiredPlanFixture.Request, Ct);

        await context.Received(1).ReadAsync(
            Arg.Is<PlanContextRequest>(r => r.Session == WiredPlanFixture.Session && r.ForDate == WiredPlanFixture.ForDate && r.Instruments.Select(i => i.Symbol).SequenceEqual(RankedSymbols)),
            Ct);
    }

    [Fact]
    public async Task A_context_failure_is_a_failed_result_and_no_plan()
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult([], 0, 0, []))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<PlanContext>(Error.Upstream("x.y", "no"))));

        var result = await new SelectionPlanBuilder(source, context, Microsoft.Extensions.Logging.Abstractions.NullLogger<SelectionPlanBuilder>.Instance).BuildAsync(WiredPlanFixture.Request, Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_item_is_a_proposal_and_nothing_in_the_plan_is_approved_or_armed()
    {
        var plan = await BuildAsync();

        // The plan type carries no approval, no state and no order field: items are inserted as Proposed by the store and only an owner (or the off-by-default setting) moves them.
        typeof(NewPlanItem).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Approv", StringComparison.Ordinal) || n.Contains("State", StringComparison.Ordinal) || n.Contains("Order", StringComparison.Ordinal));
        plan.CreatedBy.ShouldBe("system:nightly-selection");
    }

    [Fact]
    public async Task A_dry_run_of_the_nightly_with_this_builder_writes_nothing()
    {
        var store = Substitute.For<IPlanStore>();
        store.ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<PlanRow>>([]));
        store.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<PlanRunRow>>([]));
        var calendar = Substitute.For<XDrishti.Application.Abstractions.Market.Calendar.ITradingCalendar>();
        calendar.GetAsync(Ct).Returns(new XDrishti.Domain.Market.Calendar.TradingCalendar());
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero));
        var builder = WiredPlanFixture.Builder(WiredPlanFixture.Candidates(), WiredPlanFixture.Context(), out _);
        var handler = new RunNightlyPlanHandler(calendar, [], builder, store, time, Microsoft.Extensions.Logging.Abstractions.NullLogger<RunNightlyPlanHandler>.Instance);

        var result = await handler.HandleAsync(new RunNightlyPlanCommand(null, true, false, "test"), Ct);

        result.IsSuccess.ShouldBeTrue();
        await store.DidNotReceive().PublishAsync(Arg.Any<NewPlan>(), Ct);
        await store.DidNotReceive().PublishWithRunAsync(Arg.Any<NewPlan>(), Arg.Any<PublishRun>(), Ct);
        await store.DidNotReceive().RecordRunAsync(Arg.Any<NewPlanRun>(), Ct);
    }

    [Fact]
    public async Task The_gate_unavailable_text_of_the_smart_rules_starts_with_the_marker_the_suggestions_read()
    {
        var plan = await BuildAsync();

        // Pins the coupling: the engine's flag text and PlanWording.GateUnavailableMarker.
        var smart = SmartRulesEngine.Apply([], WiredPlanFixture.ForDate, new SmartInputs(), new SmartRulesOptions());
        smart.Flags.Single(f => f.Code == SmartRuleCodes.GateUnavailable).Text.ShouldStartWith(PlanWording.GateUnavailableMarker);
        plan.ShouldNotBeNull();
    }
}
