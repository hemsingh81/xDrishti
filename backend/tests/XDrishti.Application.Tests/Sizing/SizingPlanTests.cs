#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Application.Sizing;
using XDrishti.Application.Tests.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Tests.Sizing;

/// <summary>
/// BK-10 through the real selection builder on the seeded fixture of P7-09 (fake ports). Baseline by hand (see WiredPlanEndToEndTests): primary AAA 2000 shares (capital binds, risk would allow 2500) and BBB 500 shares (risk binds),
/// one paper account of 1,000,000 at 0.5 % risk. With the scaling OFF nothing may differ; with it ON no quantity may exceed the baseline.
/// </summary>
public sealed class SizingPlanTests
{
    private const long BaselineAaa = 2000;

    private const long BaselineBbb = 500;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VolReading Reading(double scale) => new(VolReading.Ok, 300, 0.02, 0.02 * scale, scale);

    private static SymbolSizingInputs Symbol(VolReading? vol, double? adv = null, params BasketBudgetRef[] budgets) => new(vol, adv, budgets);

    private static SizingInputs Inputs(SizingSettings settings, Dictionary<string, SymbolSizingInputs>? symbols = null, long version = 3) => new(settings, version, symbols ?? []);

    private static async Task<NewPlan> BuildAsync(SizingInputs? inputs, PlanContext? planContext = null)
    {
        var sizing = Substitute.For<ISizingInputs>();
        sizing.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<ContextInstrument>>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(inputs!));
        var found = WiredPlanFixture.Candidates();
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(planContext ?? WiredPlanFixture.Context())));
        var builder = new SelectionPlanBuilder(source, context, NullLogger<SelectionPlanBuilder>.Instance, WiredPlanFixture.Loose, sizing: inputs is null ? null : sizing);
        return (await builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;
    }

    private static long Quantity(NewPlan plan, string symbol) => plan.Items.Single(i => i.Symbol == symbol && i.Group == TicketGroup.Primary).Allocations.Sum(a => a.Quantity);

    private static NewPlanItem Item(NewPlan plan, string symbol) => plan.Items.Single(i => i.Symbol == symbol);

    [Fact]
    public async Task With_no_stored_setting_and_no_budget_the_plan_is_byte_identical_to_a_builder_without_the_port()
    {
        var without = await BuildAsync(null);
        var withDefaults = await BuildAsync(Inputs(SizingSettings.Defaults));

        withDefaults.InputHash.ShouldBe(without.InputHash);
        withDefaults.Items.SelectMany(i => i.Allocations).ShouldBe(without.Items.SelectMany(i => i.Allocations));
    }

    [Fact]
    public async Task With_every_setting_off_even_in_a_reduced_gate_day_with_readings_the_plan_is_identical_to_a_build_without_the_port()
    {
        var reduced = WiredPlanFixture.Context(new MarketGateInput(new DateOnly(2026, 10, 23), 19, 0.5, false));
        var inputs = Inputs(SizingSettings.Defaults, new() { ["AAA"] = Symbol(Reading(0.6)), ["BBB"] = Symbol(Reading(0.6)) });

        var without = await BuildAsync(null, reduced);
        var off = await BuildAsync(inputs, reduced);

        off.InputHash.ShouldBe(without.InputHash);
        off.Items.Select(i => (i.Group, i.Symbol, string.Join(',', i.Reasons), string.Join(',', i.Evidence.Unavailable), i.Sizing)).ShouldBe(without.Items.Select(i => (i.Group, i.Symbol, string.Join(',', i.Reasons), string.Join(',', i.Evidence.Unavailable), i.Sizing)));
        off.Items.SelectMany(i => i.Allocations).ShouldBe(without.Items.SelectMany(i => i.Allocations));
        off.Items.ShouldAllBe(i => i.Sizing == null);
    }

    [Fact]
    public async Task With_volatility_on_in_a_reduced_day_the_smaller_of_the_two_factors_is_taken_once_and_never_the_product()
    {
        var reduced = WiredPlanFixture.Context(new MarketGateInput(new DateOnly(2026, 10, 23), 19, 0.5, false));
        var baseline = await BuildAsync(null, reduced);
        var inputs = Inputs(SizingSettings.Defaults with { VolTargetEnabled = true }, new() { ["AAA"] = Symbol(Reading(0.9)), ["BBB"] = Symbol(Reading(0.9)) });

        var plan = await BuildAsync(inputs, reduced);

        var aaa = plan.Items.First(i => i.Group == TicketGroup.Primary);
        aaa.Sizing!.Factor.ShouldBe(0.75); // min(0.9, 0.75), not 0.675
        aaa.Allocations.Sum(a => (long)a.Quantity).ShouldBe(baseline.Items.First(i => i.Group == TicketGroup.Primary).Allocations.Sum(a => (long)a.Quantity));
    }

    [Fact]
    public async Task A_scale_that_leaves_less_than_one_share_sends_the_ticket_to_the_reserve_as_sizing_zero()
    {
        // BBB fixed quantity is 500 (risk bound); a factor of 0.001 leaves 0. Capital is 1,000,000, so a vol reading of 0.001 is below the floor 0.25 and is clamped: use a floor of 0.25 and a tiny account instead.
        var tiny = new ContextAccount(WiredPlanFixture.Paper, XDrishti.Domain.Accounts.TradingMode.Paper, 4_000, 0.5, 20, 3, 0, 4_000, 2, 1.5, 0, new HashSet<string>());
        var context = WiredPlanFixture.Context(accounts: [tiny]);
        var inputs = Inputs(SizingSettings.Defaults with { VolTargetEnabled = true, Floor = 0.25 }, new() { ["AAA"] = Symbol(Reading(0.25)), ["BBB"] = Symbol(Reading(0.25)) });

        var plan = await BuildAsync(inputs, context);

        // BBB: risk 4000 * 0.5 % / 10 = 2 shares; scaled by 0.25 = 0 -> sizing_zero. AAA: 4000 * 0.5 % / 2 = 10; scaled 2 -> stays.
        var bbb = Item(plan, "BBB");
        bbb.Group.ShouldBe(TicketGroup.Reserve);
        bbb.Reasons[0].ShouldBe("sizing_zero:fail");
        Item(plan, "AAA").Group.ShouldBe(TicketGroup.Primary);
    }

    [Fact]
    public async Task With_volatility_targeting_on_a_scale_of_one_half_halves_the_risk_bound_quantities_and_records_the_setting_version()
    {
        var inputs = Inputs(SizingSettings.Defaults with { VolTargetEnabled = true }, new() { ["AAA"] = Symbol(Reading(0.5)), ["BBB"] = Symbol(Reading(0.5)) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(1250); // 1e6 * 0.5 % * 0.5 / 2
        Quantity(plan, "BBB").ShouldBe(250); // 1e6 * 0.5 % * 0.5 / 10
        Item(plan, "AAA").Sizing!.SettingsVersion.ShouldBe(3);
        Item(plan, "AAA").Sizing!.Text.ShouldContain("settings v3");
        Item(plan, "AAA").Sizing!.VolScale.ShouldBe(0.5);
        Item(plan, "AAA").Sizing!.GateScale.ShouldBe(1.0);
        Item(plan, "AAA").Sizing!.Factor.ShouldBe(0.5);
        Item(plan, "AAA").Sizing!.Text.ShouldContain("scale 0.50");
    }

    [Fact]
    public async Task An_unavailable_volatility_is_sized_at_the_fixed_quantity_and_flagged_never_scaled_by_a_guess()
    {
        var unknown = new VolReading("insufficient_returns", 40, null, null, null);
        var inputs = Inputs(SizingSettings.Defaults with { VolTargetEnabled = true }, new() { ["AAA"] = Symbol(unknown), ["BBB"] = Symbol(unknown) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(BaselineAaa);
        Quantity(plan, "BBB").ShouldBe(BaselineBbb);
        Item(plan, "AAA").Evidence.Unavailable.ShouldContain(SizeScaler.VolatilityUnavailable);
        Item(plan, "AAA").Sizing!.Text.ShouldContain("volatility unknown");
    }

    [Fact]
    public async Task The_capacity_cap_cuts_to_one_percent_of_the_traded_value_and_an_unknown_value_is_flagged_not_a_pass()
    {
        var inputs = Inputs(SizingSettings.Defaults with { CapacityEnabled = true }, new() { ["AAA"] = Symbol(null, 4_000_000), ["BBB"] = Symbol(null, null) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(200); // floor(0.005 * 4,000,000 / 100)
        Item(plan, "AAA").Sizing!.CapacityShares.ShouldBe(200);
        Item(plan, "AAA").Sizing!.Text.ShouldContain("capped at 0.5 % of 20-day traded value");
        Quantity(plan, "BBB").ShouldBe(BaselineBbb);
        Item(plan, "BBB").Evidence.Unavailable.ShouldContain(SizeScaler.CapacityUnavailable);
        Item(plan, "BBB").Sizing!.Text.ShouldContain("capacity unknown");
    }

    [Fact]
    public async Task A_capacity_below_one_share_sends_the_ticket_to_the_reserve_with_the_code()
    {
        var inputs = Inputs(SizingSettings.Defaults with { CapacityEnabled = true }, new() { ["AAA"] = Symbol(null, 10_000), ["BBB"] = Symbol(null, 100_000_000) });

        var plan = await BuildAsync(inputs);

        var aaa = Item(plan, "AAA");
        aaa.Group.ShouldBe(TicketGroup.Reserve);
        aaa.Reasons.ShouldContain("capacity_cap:fail");
        aaa.Allocations.ShouldBeEmpty();
        Quantity(plan, "BBB").ShouldBe(BaselineBbb); // 0.5 % of 100 M / 200 = 2500 shares: no cut
    }

    [Fact]
    public async Task A_basket_budget_scales_the_second_ticket_to_the_room_by_hand()
    {
        // total limit 3.0 %, budget 30 % = 0.9; AAA takes 0.5, room 0.4, BBB asks 0.5: ratio 0.8 (>= 0.5 floor) -> 500 * 0.8 = 400
        var nifty = new BasketBudgetRef("Nifty", 30);
        var inputs = Inputs(SizingSettings.Defaults, new() { ["AAA"] = Symbol(null, null, nifty), ["BBB"] = Symbol(null, null, nifty) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(BaselineAaa);
        Quantity(plan, "BBB").ShouldBe(400);
        Item(plan, "BBB").Sizing!.Text.ShouldContain("uses 100 % of basket budget");
        Item(plan, "BBB").Sizing!.BudgetUsesPct!.Value.ShouldBe(100.0, 1e-6);
        Item(plan, "BBB").Sizing!.BudgetRoom!.Value.ShouldBe(0.4, 1e-9);
    }

    [Fact]
    public async Task A_basket_budget_with_a_ratio_below_the_floor_sends_the_ticket_to_the_reserve_with_the_numbers()
    {
        // budget 20 % = 0.6; AAA takes 0.5, room 0.1, BBB asks 0.5: ratio 0.2 < 0.5
        var nifty = new BasketBudgetRef("Nifty", 20);
        var inputs = Inputs(SizingSettings.Defaults, new() { ["AAA"] = Symbol(null, null, nifty), ["BBB"] = Symbol(null, null, nifty) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(BaselineAaa);
        var bbb = Item(plan, "BBB");
        bbb.Group.ShouldBe(TicketGroup.Reserve);
        bbb.Reasons[0].ShouldBe("basket_budget:fail");
        bbb.Reasons[1].ShouldContain("room 0.1");
        bbb.Reasons[1].ShouldContain("of 0.6");
        bbb.Allocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_symbol_with_no_budget_row_has_no_budget()
    {
        var inputs = Inputs(SizingSettings.Defaults, new() { ["AAA"] = Symbol(null), ["BBB"] = Symbol(null) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(BaselineAaa);
        Quantity(plan, "BBB").ShouldBe(BaselineBbb);
    }

    [Fact(Explicit = true)]
    public async Task Criterion_14_the_scaling_adds_under_thirty_milliseconds_to_a_plan_of_five_tickets()
    {
        // Wall-clock: run on demand only (the full gate's load would make it flaky). Best of five runs of the same plan with and without the scaling.
        var inputs = Inputs(SizingSettings.Defaults with { VolTargetEnabled = true, CapacityEnabled = true }, new() { ["AAA"] = Symbol(Reading(0.8), 5_000_000, new BasketBudgetRef("Nifty", 50)), ["BBB"] = Symbol(Reading(0.8), 5_000_000, new BasketBudgetRef("Nifty", 50)) });
        await BuildAsync(null);
        await BuildAsync(inputs);
        var plain = TimeSpan.MaxValue;
        var scaled = TimeSpan.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await BuildAsync(null);
            plain = TimeSpan.FromTicks(Math.Min(plain.Ticks, clock.Elapsed.Ticks));
            clock.Restart();
            await BuildAsync(inputs);
            scaled = TimeSpan.FromTicks(Math.Min(scaled.Ticks, clock.Elapsed.Ticks));
        }

        (scaled - plain).TotalMilliseconds.ShouldBeLessThan(30);
    }

    [Fact]
    public async Task Property_no_combination_of_scale_capacity_and_budget_gives_more_than_the_baseline()
    {
        var random = new Random(21);
        for (var i = 0; i < 40; i++)
        {
            var settings = new SizingSettings(random.Next(2) == 0, 0.25 + (random.NextDouble() * 0.75), random.Next(2) == 0, 0.0005 + (random.NextDouble() * 0.0045), 0.25 + (random.NextDouble() * 0.75));
            Dictionary<string, SymbolSizingInputs> symbols = [];
            foreach (var s in new[] { "AAA", "BBB" })
            {
                var vol = random.Next(4) == 0 ? null : new VolReading(VolReading.Ok, 300, 0.01, 0.01, (random.NextDouble() * 2) - 0.2); // a scale above 1 or below 0 must never act
                BasketBudgetRef[] budgets = random.Next(2) == 0 ? [new BasketBudgetRef("Nifty", 1 + (random.NextDouble() * 99))] : [];
                symbols[s] = Symbol(vol, random.Next(3) == 0 ? null : random.NextDouble() * 50_000_000, budgets);
            }

            var plan = await BuildAsync(Inputs(settings, symbols));

            foreach (var item in plan.Items.Where(x => x.Group == TicketGroup.Primary))
            {
                item.Allocations.Sum(a => (long)a.Quantity).ShouldBeLessThanOrEqualTo(item.Symbol == "AAA" ? BaselineAaa : BaselineBbb);
            }
        }
    }
}
