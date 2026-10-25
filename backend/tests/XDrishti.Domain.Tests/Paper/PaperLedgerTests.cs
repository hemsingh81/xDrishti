using XDrishti.Domain.Paper;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>
/// Spec P10-02 criteria 1, 2, 3, 5 on the hand-worked fixture: capital 100,000.00, closed A (BUY 60 at 100.15, sold 102.30: gross 129.00, charges 20.00, result 109.00), closed B (SELL 50 at 200.00, bought 202.00: gross -100.00, charges 20.00, result -120.00)
/// and open C (BUY 40 at 50.00, stop 47.50, last close 51.00). Every expected number below is worked by hand, not computed by the code under test.
/// </summary>
public sealed class PaperLedgerTests
{
    private static readonly Guid Account = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a04");

    private static readonly DateOnly Day1 = new(2026, 11, 3);

    private static readonly DateOnly Day2 = new(2026, 11, 4);

    private static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromMinutes(330));

    private static LedgerTrade A(bool charged = true) =>
        new(1, Account, "AAA", "BUY", 60, At(Day1, 9, 20), 100.15m, 99.05m, false, "primary", "ORB|AAA|BUY|2026-11-02",
            new LedgerExit(At(Day1, 11, 5), 129.00m, charged ? 20.00m : null, charged ? 109.00m : null, charged ? "flat-10" : null, 1.8));

    private static LedgerTrade B() =>
        new(2, Account, "BBB", "SELL", 50, At(Day2, 9, 20), 200.00m, 204.00m, false, "primary", "ORB|BBB|SELL|2026-11-03",
            new LedgerExit(At(Day2, 13, 0), -100.00m, 20.00m, -120.00m, "flat-10", -1.1));

    private static LedgerTrade C() => new(3, Account, "CCC", "BUY", 40, At(Day1, 10, 0), 50.00m, 47.50m, true, "primary", "ORB|CCC|BUY|2026-11-02", null);

    [Fact]
    public void The_fixture_matches_the_hand_numbers_exactly()
    {
        var f = PaperLedger.Compute(100_000m, [A(), B(), C()], Day2);

        f.Gross.ShouldBe(29.00m);
        f.Charges.ShouldBe(40.00m);
        f.Result.ShouldBe(-11.00m);
        f.Equity.ShouldBe(99_989.00m);
        f.OpenNotional.ShouldBe(2_000.00m);
        f.FundsAvailable.ShouldBe(97_989.00m);
        f.OpenRisk.ShouldBe(100.00m);
        Math.Round(f.OpenRiskPct!.Value, 3).ShouldBe(0.100m);
        f.ClosedCount.ShouldBe(2);
        f.OpenCount.ShouldBe(1);
        f.MaxDrawdown.ShouldBe(120.00m);
        Math.Round(f.MaxDrawdownPct!.Value, 4).ShouldBe(0.1199m);
        f.LastSessionResult.ShouldBe(-120.00m);
        Math.Round(f.LastSessionPct!.Value, 4).ShouldBe(-0.1199m);
        Math.Round(f.DailyLossPct!.Value, 4).ShouldBe(0.1199m);
    }

    [Fact]
    public void Unrealised_is_the_mark_move_times_the_quantity_and_the_open_risk_share_is_of_equity()
    {
        var f = PaperLedger.Compute(100_000m, [A(), B(), C()], Day2);
        var c = PaperPosition.Of(C(), PaperMark.Of(Day2, new DailyClose(Day2, 51.00m, true)), f.Equity, 2);

        c.Unrealised.ShouldBe(40.00m);
        Math.Round(c.RiskPctOfEquity!.Value, 3).ShouldBe(0.100m);
        PaperLedger.TotalUnrealised([c]).ShouldBe(40.00m);
    }

    [Fact]
    public void A_short_position_gains_when_the_mark_falls()
    {
        var shortC = C() with { Side = "SELL", StopPrice = 52.50m };

        PaperPosition.Of(shortC, PaperMark.Of(Day2, new DailyClose(Day2, 49.00m, true)), 100_000m, 1).Unrealised.ShouldBe(40.00m);
    }

    [Fact]
    public void The_same_trades_in_any_order_give_the_same_ledger()
    {
        var expected = PaperLedger.Compute(100_000m, [A(), B(), C()], Day2);

        foreach (var order in new[] { new[] { 2, 1, 0 }, new[] { 1, 2, 0 }, new[] { 0, 2, 1 } })
        {
            var trades = new[] { A(), B(), C() };
            PaperLedger.Compute(100_000m, [.. order.Select(i => trades[i])], Day2).ShouldBe(expected);
        }
    }

    [Fact]
    public void A_seeded_random_set_gives_the_same_ledger_after_shuffling_and_its_result_is_the_sum_of_its_exits()
    {
        var random = new Random(20261026);
        for (var round = 0; round < 50; round++)
        {
            var trades = Enumerable.Range(1, 20).Select(i =>
            {
                var day = Day1.AddDays(random.Next(0, 5));
                var entry = 100m + random.Next(0, 50);
                var gross = random.Next(-500, 800) + (random.Next(0, 100) / 100m);
                var charged = random.Next(0, 3) > 0;
                var exit = random.Next(0, 4) == 0
                    ? null
                    : new LedgerExit(At(day, 10 + random.Next(0, 5), random.Next(0, 60)), gross, charged ? 12.5m : null, charged ? gross - 12.5m : null, charged ? "s" : null, random.NextDouble());
                return new LedgerTrade(i, Account, "S" + (i % 7), random.Next(0, 2) == 0 ? "BUY" : "SELL", random.Next(1, 100), At(day, 9, 20), entry, entry - 2, false, "primary", "k" + (i % 11), exit);
            }).ToList();

            var shuffled = trades.OrderBy(_ => random.Next()).ToList();
            var a = PaperLedger.Compute(250_000m, trades, Day1.AddDays(3));
            var b = PaperLedger.Compute(250_000m, shuffled, Day1.AddDays(3));

            b.ShouldBe(a);
            a.Result.ShouldBe(trades.Where(t => t.Closed).Sum(t => t.Exit!.NetPnl ?? t.Exit.GrossPnl));
            a.OpenCount.ShouldBe(trades.Count(t => !t.Closed));
            a.MaxDrawdown.ShouldBeGreaterThanOrEqualTo(0m);
        }
    }

    [Fact]
    public void No_stored_sheet_is_gross_and_a_stored_sheet_is_labelled_after_the_sheet_never_net()
    {
        var gross = PaperLedger.Compute(100_000m, [A(charged: false)], Day2);
        var after = PaperLedger.Compute(100_000m, [A(), B()], Day2);
        var mixed = PaperLedger.Compute(100_000m, [A(charged: false), B()], Day2);

        gross.Basis.ShouldBe("gross, no charges");
        gross.Charges.ShouldBeNull();
        gross.Result.ShouldBe(129.00m);
        after.Basis.ShouldBe("after the stored sheet; the sheet is not confirmed as the owner's in this view (ISS-25)");
        after.Result.ShouldBe(-11.00m);
        mixed.Basis.ShouldStartWith("mixed: 1 of 2 closed trades");
        foreach (var basis in new[] { gross.Basis, after.Basis, mixed.Basis })
        {
            basis.Split(' ', ';', ',', '(', ')').ShouldNotContain("net");
        }
    }

    [Fact]
    public void An_account_without_capital_has_the_ledger_result_but_no_equity_and_no_percentage()
    {
        var f = PaperLedger.Compute(null, [A(), B(), C()], Day2);

        f.Result.ShouldBe(-11.00m);
        f.OpenRisk.ShouldBe(100.00m);
        f.Equity.ShouldBeNull();
        f.FundsAvailable.ShouldBeNull();
        f.OpenRiskPct.ShouldBeNull();
        f.LastSessionPct.ShouldBeNull();
        f.DailyLossPct.ShouldBeNull();
        f.MaxDrawdownPct.ShouldBeNull();
        PaperPosition.Of(C(), PaperMark.Of(Day2, null), f.Equity, 1).RiskPctOfEquity.ShouldBeNull();
    }

    [Fact]
    public void Funds_never_go_below_zero_and_a_winning_last_session_carries_no_loss()
    {
        var big = C() with { Qty = 5000 }; // notional 250,000 on 100,000 of capital

        PaperLedger.Compute(100_000m, [big], Day2).FundsAvailable.ShouldBe(0m);
        var win = PaperLedger.Compute(100_000m, [A()], Day1);
        win.LastSessionResult.ShouldBe(109.00m);
        win.DailyLossPct.ShouldBe(0m);
        PaperLedger.Compute(100_000m, [A()], Day2).LastSessionResult.ShouldBe(0m);
    }

    [Fact]
    public void Marks_are_values_only_for_a_complete_bar_of_the_last_closed_session()
    {
        var value = PaperMark.Of(Day2, new DailyClose(Day2, 51.00m, true));
        var stale = PaperMark.Of(Day2, new DailyClose(Day1, 50.50m, true));
        var none = PaperMark.Of(Day2, null);
        var partial = PaperMark.Of(Day2, new DailyClose(Day2, 51.00m, false));

        value.State.ShouldBe("value");
        value.Price.ShouldBe(51.00m);
        value.AsOf.ShouldBe(Day2);
        value.Reason.ShouldBeNull();
        foreach (var unavailable in new[] { stale, none, partial })
        {
            unavailable.State.ShouldBe("unavailable");
            unavailable.Price.ShouldBeNull();
            unavailable.Reason.ShouldNotBeNullOrWhiteSpace();
        }

        stale.Reason!.ShouldContain("2026-11-03");
        stale.Reason!.ShouldContain("2026-11-04");
    }

    [Fact]
    public void One_unavailable_mark_makes_the_total_unavailable_and_never_the_sum_of_the_others()
    {
        var ok = PaperPosition.Of(C(), PaperMark.Of(Day2, new DailyClose(Day2, 51.00m, true)), 100_000m, 2);
        var stale = PaperPosition.Of(C() with { EntryId = 4, Symbol = "DDD" }, PaperMark.Of(Day2, new DailyClose(Day1, 1m, true)), 100_000m, 2);

        stale.Unrealised.ShouldBeNull();
        PaperLedger.TotalUnrealised([ok, stale]).ShouldBeNull();
        PaperLedger.TotalUnrealised([]).ShouldBeNull();
    }

    [Fact]
    public void The_realised_series_adds_each_sessions_exits_to_the_capital()
    {
        var series = PaperLedger.RealisedSeries(100_000m, [A(), B(), C()], [Day1, Day2]);

        series[0].ShouldBe((Day1, 109.00m, (decimal?)100_109.00m));
        series[1].ShouldBe((Day2, -11.00m, (decimal?)99_989.00m));
        PaperLedger.RealisedSeries(null, [A()], [Day1])[0].Equity.ShouldBeNull();
    }
}
