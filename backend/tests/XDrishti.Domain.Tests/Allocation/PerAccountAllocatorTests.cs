using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;

namespace XDrishti.Domain.Tests.Allocation;

/// <summary>The per-account allocator (spec P7-07): hand tables, exclusions, unavailable inputs, determinism, limits.</summary>
public sealed class PerAccountAllocatorTests
{
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static AllocationTicket Ticket(double? entry = 100, double? stop = 98, int? qty = 100000, int? lot = 1, string side = "BUY") => new("AAA", side, entry, stop, qty, lot);

    // equity 1,000,000; risk 0.5 % = 5000 / 2 = 2500; capital 20 % = 200000 / 100 = 2000; funds 5000; headroom (3 - 1) % = 20000 / 2 = 10000
    private static AllocationAccount Account(int n = 1, TradingMode mode = TradingMode.Paper, bool trading = true, double? equity = 1_000_000, double? risk = 0.5, double? capital = 20, double? total = 3, double? open = 1, double? funds = 500_000, bool? holds = false) =>
        new(Id(n), mode, trading, equity, risk, capital, total, open, funds, holds);

    private static AccountAllocation For(AllocationResult r, int n) => r.Decisions.Single(d => d.AccountId == Id(n));

    [Fact]
    public void The_tightest_limit_binds_and_is_named()
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account()]);

        var d = For(r, 1);
        d.Quantity.ShouldBe(2000);
        d.Binding.ShouldBe(AllocationCodes.CapitalPerTrade);
        d.Capacity.ShouldBe(2000);
        d.RiskPct.ShouldBe(0.4, 1e-9);
        d.Code.ShouldBe(AllocationCodes.Allocated);
    }

    [Theory]
    [InlineData(0.5, 20, 3, 1, 500_000, 2000, AllocationCodes.CapitalPerTrade)]
    [InlineData(0.1, 20, 3, 1, 500_000, 500, AllocationCodes.RiskPerTrade)]
    [InlineData(0.5, 20, 3, 1, 100_000, 1000, AllocationCodes.Funds)]
    [InlineData(0.5, 20, 3, 2.8, 500_000, 1000, AllocationCodes.ExposureHeadroom)]
    [InlineData(0.5, 50, 3, 1, 900_000, 2500, AllocationCodes.RiskPerTrade)]
    public void Each_limit_in_turn_sets_the_capacity(double risk, double capital, double total, double open, double funds, int expected, string binding)
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account(risk: risk, capital: capital, total: total, open: open, funds: funds)]);

        For(r, 1).Quantity.ShouldBe(expected);
        For(r, 1).Binding.ShouldBe(binding);
    }

    [Theory]
    [InlineData(1, 2000)]
    [InlineData(75, 1950)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 0)]
    public void Quantity_rounds_down_to_whole_lots(int lot, int expected)
    {
        var r = PerAccountAllocator.Allocate(Ticket(lot: lot), [Account()]);

        For(r, 1).Quantity.ShouldBe(expected);
        if (expected == 0)
        {
            For(r, 1).Code.ShouldBe(AllocationCodes.BelowOneLot);
            For(r, 1).Message.ShouldContain("2000");
        }
    }

    [Fact]
    public void A_sell_is_sized_from_the_distance_to_a_stop_above()
    {
        var r = PerAccountAllocator.Allocate(Ticket(stop: 102, side: "SELL"), [Account(risk: 0.1)]);

        For(r, 1).Quantity.ShouldBe(500);
    }

    [Fact]
    public void The_ticket_quantity_is_a_pool_shared_by_capacity()
    {
        // capacities 2000 and 1000 (funds 100000), pool 1500 -> 1000 and 500
        var r = PerAccountAllocator.Allocate(Ticket(qty: 1500), [Account(1), Account(2, funds: 100_000)]);

        For(r, 1).Quantity.ShouldBe(1000);
        For(r, 2).Quantity.ShouldBe(500);
        r.TotalAllocated.ShouldBe(1500);
    }

    [Fact]
    public void A_pool_larger_than_all_capacities_gives_each_its_capacity()
    {
        var r = PerAccountAllocator.Allocate(Ticket(qty: 100000), [Account(1), Account(2, funds: 100_000)]);

        r.Decisions.Select(d => d.Quantity).ShouldBe([2000, 1000]);
    }

    [Fact]
    public void Leftover_lots_go_to_the_largest_remainder_then_the_lowest_id()
    {
        // capacities 10 and 10 shares (funds 1000 at 100), pool 5 lots of 1: 2 and 2 by proportion, one left to the lower id
        var r = PerAccountAllocator.Allocate(Ticket(qty: 5), [Account(2, funds: 1000), Account(1, funds: 1000)]);

        For(r, 1).Quantity.ShouldBe(3);
        For(r, 2).Quantity.ShouldBe(2);
    }

    [Fact]
    public void An_account_squeezed_out_of_the_pool_says_so_with_its_capacity()
    {
        var r = PerAccountAllocator.Allocate(Ticket(qty: 1), [Account(1, funds: 100_000), Account(2, funds: 100_000)]);

        r.TotalAllocated.ShouldBe(1);
        var out2 = For(r, 2);
        out2.Code.ShouldBe(AllocationCodes.PoolExhausted);
        out2.Capacity.ShouldBe(1000);
    }

    [Fact]
    public void A_live_account_is_excluded_with_a_reason_and_never_allocated()
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account(1, TradingMode.ManualLive), Account(2)]);

        For(r, 1).Quantity.ShouldBe(0);
        For(r, 1).Code.ShouldBe(AllocationCodes.NotPaper);
        For(r, 1).Mode.ShouldBe(TradingMode.ManualLive);
        For(r, 2).Quantity.ShouldBeGreaterThan(0);
        r.Allocated.ShouldAllBe(d => d.Mode == TradingMode.Paper);
    }

    [Fact]
    public void A_non_trading_account_is_excluded_even_in_paper_mode()
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account(1, trading: false)]);

        For(r, 1).Code.ShouldBe(AllocationCodes.NotTrading);
        r.TotalAllocated.ShouldBe(0);
    }

    [Fact]
    public void A_non_trading_live_account_is_still_excluded_and_other_accounts_are_unaffected()
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account(1, TradingMode.ManualLive, trading: false), Account(2)]);

        For(r, 1).Quantity.ShouldBe(0);
        For(r, 2).Quantity.ShouldBe(2000);
    }

    [Fact]
    public void A_held_symbol_gets_nothing()
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account(holds: true)]);

        For(r, 1).Code.ShouldBe(AllocationCodes.AlreadyHeld);
    }

    [Fact]
    public void The_same_account_twice_is_excluded_every_time()
    {
        var r = PerAccountAllocator.Allocate(Ticket(), [Account(1), Account(1, funds: 1), Account(2)]);

        r.Decisions.Count(d => d.Code == AllocationCodes.DuplicateAccount).ShouldBe(2);
        For(r, 2).Quantity.ShouldBe(2000);
    }

    [Theory]
    [InlineData("equity")]
    [InlineData("risk")]
    [InlineData("capital")]
    [InlineData("total")]
    [InlineData("open")]
    [InlineData("funds")]
    [InlineData("holds")]
    [InlineData("nan")]
    [InlineData("zero-equity")]
    [InlineData("negative-funds")]
    public void A_missing_or_unusable_account_number_allocates_nothing_and_names_it(string which)
    {
        var account = which switch
        {
            "equity" => Account(equity: null),
            "risk" => Account(risk: null),
            "capital" => Account(capital: null),
            "total" => Account(total: null),
            "open" => Account(open: null),
            "funds" => Account(funds: null),
            "holds" => Account(holds: null),
            "nan" => Account(risk: double.NaN),
            "zero-equity" => Account(equity: 0),
            _ => Account(funds: -1),
        };

        var d = For(PerAccountAllocator.Allocate(Ticket(), [account, Account(2)]), 1);

        d.Quantity.ShouldBe(0);
        d.Code.ShouldBe(AllocationCodes.UnavailableAccount);
        d.Message.ShouldContain("missing or unusable");
    }

    [Theory]
    [InlineData(null, 98.0, 100, 1, "BUY")]
    [InlineData(100.0, null, 100, 1, "BUY")]
    [InlineData(100.0, 98.0, null, 1, "BUY")]
    [InlineData(100.0, 98.0, 100, null, "BUY")]
    [InlineData(100.0, 98.0, 100, 0, "BUY")]
    [InlineData(100.0, 98.0, 100, 1, "HOLD")]
    [InlineData(100.0, 100.0, 100, 1, "BUY")]
    [InlineData(100.0, 102.0, 100, 1, "BUY")]
    [InlineData(100.0, 98.0, 100, 1, "SELL")]
    [InlineData(100.0, 98.0, 50, 75, "BUY")]
    [InlineData(double.NaN, 98.0, 100, 1, "BUY")]
    [InlineData(double.PositiveInfinity, 98.0, 100, 1, "BUY")]
    [InlineData(100.0, -1.0, 100, 1, "BUY")]
    public void An_unusable_ticket_allocates_nothing_to_anyone(double? entry, double? stop, int? qty, int? lot, string side)
    {
        var r = PerAccountAllocator.Allocate(new AllocationTicket("AAA", side, entry, stop, qty, lot), [Account(1), Account(2)]);

        r.TotalAllocated.ShouldBe(0);
        r.Decisions.ShouldAllBe(d => d.Code == AllocationCodes.UnavailableTicket);
    }

    [Fact]
    public void An_unusable_ticket_still_names_a_live_account_as_excluded_not_unavailable()
    {
        var r = PerAccountAllocator.Allocate(Ticket(entry: null), [Account(1, TradingMode.ManualLive)]);

        For(r, 1).Code.ShouldBe(AllocationCodes.NotPaper);
    }

    [Fact]
    public void The_same_inputs_in_any_order_give_the_same_decisions()
    {
        var accounts = new[] { Account(1), Account(2, funds: 123_456), Account(3, risk: 0.37), Account(4, TradingMode.ManualLive), Account(5, equity: 777_777), Account(6, holds: true) };
        var expected = PerAccountAllocator.Allocate(Ticket(qty: 3333, lot: 7), accounts).Decisions;
        var random = new Random(7);

        for (var i = 0; i < 25; i++)
        {
            var shuffled = accounts.OrderBy(_ => random.Next()).ToList();
            PerAccountAllocator.Allocate(Ticket(qty: 3333, lot: 7), shuffled).Decisions.ShouldBe(expected);
        }
    }

    [Fact]
    public void No_account_exceeds_its_own_limits_and_the_sum_stays_within_the_ticket_over_random_inputs()
    {
        var random = new Random(11);
        for (var round = 0; round < 300; round++)
        {
            var entry = 20 + random.NextDouble() * 2000;
            var stop = entry * (0.9 + random.NextDouble() * 0.09);
            var lot = new[] { 1, 5, 25, 75 }[random.Next(4)];
            var qty = random.Next(lot, 50_000);
            var accounts = Enumerable.Range(1, 1 + random.Next(6)).Select(n => Account(
                n,
                random.Next(5) == 0 ? TradingMode.ManualLive : TradingMode.Paper,
                equity: 50_000 + random.NextDouble() * 5_000_000,
                risk: 0.1 + random.NextDouble() * 1.9,
                capital: 5 + random.NextDouble() * 95,
                total: 3 + random.NextDouble() * 5,
                open: random.NextDouble() * 4,
                funds: random.NextDouble() * 3_000_000)).ToList();

            var r = PerAccountAllocator.Allocate(new AllocationTicket("AAA", "BUY", entry, stop, qty, lot), accounts);

            r.TotalAllocated.ShouldBeLessThanOrEqualTo(qty);
            r.Decisions.Count.ShouldBe(accounts.Count);
            foreach (var d in r.Decisions)
            {
                var a = accounts.Single(x => x.AccountId == d.AccountId);
                if (a.Mode != TradingMode.Paper)
                {
                    d.Quantity.ShouldBe(0);
                    continue;
                }

                (d.Quantity % lot).ShouldBe(0);
                var distance = entry - stop;
                var equity = a.Equity!.Value;
                (d.Quantity * distance).ShouldBeLessThanOrEqualTo(equity * a.RiskPerTradePct!.Value / 100 + 1e-6);
                (d.Quantity * entry).ShouldBeLessThanOrEqualTo(equity * a.MaxCapitalPerTradePct!.Value / 100 + 1e-6);
                (d.Quantity * entry).ShouldBeLessThanOrEqualTo(a.FundsAvailable!.Value + 1e-6);
                (d.Quantity * distance).ShouldBeLessThanOrEqualTo(Math.Max(0, equity * (a.MaxTotalRiskPct!.Value - a.OpenRiskPct!.Value) / 100) + 1e-6);
                if (d.Quantity > 0)
                {
                    d.Quantity.ShouldBeLessThanOrEqualTo(d.Capacity!.Value);
                }
            }
        }
    }

    [Fact]
    public void Every_supplied_account_appears_once_in_id_order_and_the_inputs_are_unchanged()
    {
        var accounts = new List<AllocationAccount> { Account(3), Account(1), Account(2, TradingMode.ManualLive) };
        var copy = accounts.ToList();

        var r = PerAccountAllocator.Allocate(Ticket(), accounts);

        r.Decisions.Select(d => d.AccountId).ShouldBe([Id(1), Id(2), Id(3)]);
        accounts.ShouldBe(copy);
    }

    [Fact]
    public void No_accounts_gives_an_empty_result()
    {
        PerAccountAllocator.Allocate(Ticket(), []).Decisions.ShouldBeEmpty();
    }
}
