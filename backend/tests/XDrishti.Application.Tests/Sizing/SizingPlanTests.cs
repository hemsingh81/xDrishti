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

    private static async Task<NewPlan> BuildAsync(SizingInputs? inputs)
    {
        var sizing = Substitute.For<ISizingInputs>();
        sizing.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<ContextInstrument>>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(inputs!));
        var found = WiredPlanFixture.Candidates();
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
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
    public async Task With_the_setting_off_the_computed_scale_is_shown_as_not_applied_and_the_quantities_do_not_change()
    {
        var inputs = Inputs(SizingSettings.Defaults, new() { ["AAA"] = Symbol(Reading(0.6)), ["BBB"] = Symbol(Reading(0.6)) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(BaselineAaa);
        Quantity(plan, "BBB").ShouldBe(BaselineBbb);
        Item(plan, "AAA").Reasons.ShouldContain(r => r.Contains("volatility scale 0.60 not applied (setting off)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_volatility_targeting_on_a_scale_of_one_half_halves_the_risk_bound_quantities_and_records_the_setting_version()
    {
        var inputs = Inputs(SizingSettings.Defaults with { VolTargetEnabled = true }, new() { ["AAA"] = Symbol(Reading(0.5)), ["BBB"] = Symbol(Reading(0.5)) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(1250); // 1e6 * 0.5 % * 0.5 / 2
        Quantity(plan, "BBB").ShouldBe(250); // 1e6 * 0.5 % * 0.5 / 10
        Item(plan, "AAA").Reasons.Single().ShouldContain("settings v3");
        Item(plan, "AAA").Reasons.Single().ShouldContain("scale 0.50");
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
        Item(plan, "AAA").Reasons.Single().ShouldContain("volatility unknown");
    }

    [Fact]
    public async Task The_capacity_cap_cuts_to_one_percent_of_the_traded_value_and_an_unknown_value_is_flagged_not_a_pass()
    {
        var inputs = Inputs(SizingSettings.Defaults with { CapacityEnabled = true }, new() { ["AAA"] = Symbol(null, 2_000_000), ["BBB"] = Symbol(null, null) });

        var plan = await BuildAsync(inputs);

        Quantity(plan, "AAA").ShouldBe(200); // floor(0.01 * 2,000,000 / 100)
        Item(plan, "AAA").Reasons.Single().ShouldContain("capped at 1 % of 20-day traded value");
        Quantity(plan, "BBB").ShouldBe(BaselineBbb);
        Item(plan, "BBB").Evidence.Unavailable.ShouldContain(SizeScaler.CapacityUnavailable);
        Item(plan, "BBB").Reasons.Single().ShouldContain("capacity unknown");
    }

    [Fact]
    public async Task A_capacity_below_one_share_sends_the_ticket_to_the_reserve_with_the_code()
    {
        var inputs = Inputs(SizingSettings.Defaults with { CapacityEnabled = true }, new() { ["AAA"] = Symbol(null, 5_000), ["BBB"] = Symbol(null, 100_000_000) });

        var plan = await BuildAsync(inputs);

        var aaa = Item(plan, "AAA");
        aaa.Group.ShouldBe(TicketGroup.Reserve);
        aaa.Reasons.ShouldContain("capacity_cap:fail");
        aaa.Allocations.ShouldBeEmpty();
        Quantity(plan, "BBB").ShouldBe(BaselineBbb); // 1 % of 100 M / 200 = 5000 shares: no cut
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
        Item(plan, "BBB").Reasons.Single().ShouldContain("uses 100 % of basket budget");
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
            var settings = new SizingSettings(random.Next(2) == 0, 0.25 + (random.NextDouble() * 0.75), random.Next(2) == 0, 0.001 + (random.NextDouble() * 0.05));
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
