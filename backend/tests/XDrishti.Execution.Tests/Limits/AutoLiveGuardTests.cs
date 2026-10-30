using XDrishti.Domain.Execution.Limits;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Tests.Limits;

/// <summary>Spec P12-09 criteria 1, 2, 5, 6, 7 and 12 on the pure guard: an empty sheet allows nothing, the hand example's caps exactly, the window by India time (end excluded), one test per refusal code and the all-pass case.</summary>
public sealed class AutoLiveGuardTests
{
    private static IReadOnlyList<string> Caps(AutoEntry entry, LedgerDay day, ExecutionLimits? sheet = null, DateTimeOffset? now = null, AutoLiveSettings? settings = null) =>
        AutoLiveGuard.Caps(sheet ?? LimitsScene.Sheet, settings ?? LimitsScene.On, entry, day, now ?? LimitsScene.Now);

    private static GuardVerdict Guard(AutoFacts? facts = null, AutoEntry? entry = null, AutoLiveSettings? settings = null, DateTimeOffset? now = null) =>
        AutoLiveGuard.Evaluate(ExecMode.AutoLive, settings ?? LimitsScene.On, entry ?? LimitsScene.Entry(), facts ?? LimitsScene.All(), now ?? LimitsScene.Now);

    public static TheoryData<string> IncompleteSheets() => ["no row", "orders", "notional order", "notional day", "loss", "positions", "allow-list", "window", "products"];

    private static ExecutionLimits? Incomplete(string what) => what switch
    {
        "no row" => null,
        "orders" => LimitsScene.Sheet with { MaxOrdersPerDay = null },
        "notional order" => LimitsScene.Sheet with { MaxNotionalPerOrder = null },
        "notional day" => LimitsScene.Sheet with { MaxNotionalPerDay = null },
        "loss" => LimitsScene.Sheet with { MaxLossPerDay = null },
        "positions" => LimitsScene.Sheet with { MaxOpenPositions = null },
        "allow-list" => LimitsScene.Sheet with { AllowList = [] },
        "window" => LimitsScene.Sheet with { WindowStart = null, WindowEnd = null },
        _ => LimitsScene.Sheet with { Products = [] },
    };

    [Theory]
    [MemberData(nameof(IncompleteSheets))]
    public void An_empty_or_incomplete_sheet_refuses_every_entry_limits_incomplete(string what)
    {
        var sheet = Incomplete(what);

        var caps = AutoLiveGuard.Caps(sheet, LimitsScene.On, LimitsScene.Entry(quantity: 1, price: 1m), new LedgerDay(0, 0m), LimitsScene.Now);
        var verdict = AutoLiveGuard.Evaluate(ExecMode.AutoLive, LimitsScene.On, LimitsScene.Entry(quantity: 1, price: 1m), LimitsScene.All() with { Limits = sheet }, LimitsScene.Now);

        caps.ShouldBe([AutoLiveCodes.LimitsIncomplete]);
        verdict.Codes.ShouldContain(AutoLiveCodes.LimitsIncomplete);
        verdict.Passed.ShouldBeFalse();
        verdict.Silent.ShouldBeFalse();
    }

    [Fact]
    public void The_hand_example_three_entries_of_49482_are_accepted_and_a_fourth_is_refused_cap_notional_day()
    {
        var entry = LimitsScene.Entry();
        var running = new List<decimal>();
        var day = new LedgerDay(0, 0m);

        for (var i = 0; i < 3; i++)
        {
            Caps(entry, day).ShouldBeEmpty();
            day = new LedgerDay(day.Orders + 1, day.Notional + entry.Notional);
            running.Add(day.Notional);
        }

        entry.Notional.ShouldBe(49_482.00m);
        running.ShouldBe([49_482.00m, 98_964.00m, 148_446.00m]);
        Caps(entry, day).ShouldBe([AutoLiveCodes.NotionalDay]);
        Caps(LimitsScene.Entry(quantity: 1, price: 1_554.00m), day).ShouldBeEmpty();
        (day.Notional + 1_554.00m).ShouldBe(150_000.00m);
        Caps(LimitsScene.Entry(quantity: 1, price: 1_554.01m), day).ShouldBe([AutoLiveCodes.NotionalDay]);
    }

    [Fact]
    public void The_seventh_order_of_any_size_is_refused_cap_orders_day()
    {
        var six = new LedgerDay(6, 6m);

        Caps(LimitsScene.Entry(quantity: 1, price: 1m), new LedgerDay(5, 5m)).ShouldBeEmpty();
        Caps(LimitsScene.Entry(quantity: 1, price: 1m), six).ShouldBe([AutoLiveCodes.OrdersDay]);
        Caps(LimitsScene.Entry(quantity: 1, price: 0.05m), six).ShouldBe([AutoLiveCodes.OrdersDay]);
    }

    [Fact]
    public void Rs_60000_in_one_order_passes_and_60000_01_is_refused_cap_notional_order()
    {
        Caps(LimitsScene.Entry(quantity: 1, price: 60_000.00m), new LedgerDay(0, 0m)).ShouldBeEmpty();
        Caps(LimitsScene.Entry(quantity: 1, price: 60_000.01m), new LedgerDay(0, 0m)).ShouldBe([AutoLiveCodes.NotionalOrder]);
    }

    [Fact]
    public void The_stricter_of_the_sheet_and_the_global_registry_value_applies()
    {
        var global = AutoLiveSettings.From(7, true, true, new Dictionary<string, string> { ["exec.max_order_value_inr"] = "50000", ["exec.max_orders_per_day"] = "10", ["exec.entry_window_start"] = "09:30" });

        Caps(LimitsScene.Entry(quantity: 1, price: 50_000.01m), new LedgerDay(0, 0m), settings: global).ShouldBe([AutoLiveCodes.NotionalOrder]);
        Caps(LimitsScene.Entry(quantity: 1, price: 1m), new LedgerDay(0, 0m), now: LimitsScene.Ist(9, 25), settings: global).ShouldBe([AutoLiveCodes.WindowClosed]);
        Caps(LimitsScene.Entry(quantity: 1, price: 1m), new LedgerDay(6, 6m), settings: AutoLiveSettings.From(7, true, true, new Dictionary<string, string> { ["exec.max_orders_per_day"] = "10" })).ShouldBe([AutoLiveCodes.OrdersDay]);
    }

    [Theory]
    [InlineData(9, 19, 59, 0, false)]
    [InlineData(9, 20, 0, 0, true)]
    [InlineData(14, 44, 59, 900, true)]
    [InlineData(14, 45, 0, 0, false)]
    public void The_window_starts_at_09_20_00_and_ends_before_14_45_00_india_time(int hour, int minute, int second, int millisecond, bool allowed)
    {
        var codes = Caps(LimitsScene.Entry(), new LedgerDay(0, 0m), now: LimitsScene.Ist(hour, minute, second, millisecond));

        (codes.Count == 0).ShouldBe(allowed);
        if (!allowed)
        {
            codes.ShouldBe([AutoLiveCodes.WindowClosed]);
        }
    }

    [Fact]
    public void The_window_is_india_time_of_the_clock_given_whatever_its_offset()
    {
        var utc = LimitsScene.Ist(9, 19, 59).ToUniversalTime();

        Caps(LimitsScene.Entry(), new LedgerDay(0, 0m), now: utc).ShouldBe([AutoLiveCodes.WindowClosed]);
        Caps(LimitsScene.Entry(), new LedgerDay(0, 0m), now: utc.AddSeconds(1)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("NSE:TATAPOWER", true)]
    [InlineData("TATAPOWER", true)]
    [InlineData("NSE:TATAPOWER-EQ", false)]
    [InlineData("NSE:tatapower", false)]
    [InlineData("NSE:TATAMOTORS", false)]
    [InlineData("BSE:TATAPOWER", false)]
    public void A_symbol_must_match_the_trading_symbol_of_the_allow_list_exactly(string symbol, bool allowed)
    {
        var codes = Caps(LimitsScene.Entry(symbol: symbol), new LedgerDay(0, 0m));

        codes.SequenceEqual(allowed ? [] : [AutoLiveCodes.InstrumentNotAllowed]).ShouldBeTrue(string.Join(",", codes));
    }

    [Fact]
    public void A_product_other_than_eq_mis_a_futures_ticket_or_a_sell_entry_is_refused_product_not_allowed()
    {
        var day = new LedgerDay(0, 0m);

        Caps(LimitsScene.Entry() with { Product = "EQ_CNC" }, day).ShouldBe([AutoLiveCodes.ProductNotAllowed]);
        Caps(LimitsScene.Entry() with { IsFutures = true }, day).ShouldBe([AutoLiveCodes.ProductNotAllowed]);
        Caps(LimitsScene.Entry() with { Side = ExecSide.Sell }, day).ShouldBe([AutoLiveCodes.ProductNotAllowed]);
    }

    [Fact]
    public void With_every_fact_fine_the_guard_passes()
    {
        var verdict = Guard();

        verdict.Passed.ShouldBeTrue(string.Join(",", verdict.Codes));
        verdict.Silent.ShouldBeFalse();
        verdict.RouteToTicket.ShouldBeFalse();
    }

    public static TheoryData<string> RefusalCodes() =>
    [
        AutoLiveCodes.ApprovalNotOwner, AutoLiveCodes.PlanProvisional, AutoLiveCodes.QtyAbovePlan, AutoLiveCodes.ExposureDuplicate, AutoLiveCodes.StrategyFirstUse, AutoLiveCodes.PretradeFailed, AutoLiveCodes.NotUnlocked,
        AutoLiveCodes.NotAttended, AutoLiveCodes.NotReady, AutoLiveCodes.FeedStale, AutoLiveCodes.ReconBreak, AutoLiveCodes.Halted, AutoLiveCodes.OpenPositions, AutoLiveCodes.LossDay, AutoLiveCodes.LossUnavailable,
    ];

    private static (AutoFacts Facts, AutoEntry Entry) Breaking(string code)
    {
        var all = LimitsScene.All();
        var f = all.Entry;
        var entry = LimitsScene.Entry();
        return code switch
        {
            AutoLiveCodes.ApprovalNotOwner => (all, entry with { ApprovalKind = "auto" }),
            AutoLiveCodes.PlanProvisional => (all with { Entry = f with { PlanUnavailableInputs = ["market gate unavailable"] } }, entry),
            AutoLiveCodes.QtyAbovePlan => (all, entry with { Quantity = 121, PlanAllocation = 120 }),
            AutoLiveCodes.ExposureDuplicate => (all with { Entry = f with { ExposureInSymbol = true } }, entry),
            AutoLiveCodes.StrategyFirstUse => (all with { StrategyUsedLive = false }, entry),
            AutoLiveCodes.PretradeFailed => (all with { Entry = f with { PretradeAllPass = false } }, entry),
            AutoLiveCodes.NotUnlocked => (all with { Entry = f with { DayUnlocked = false } }, entry),
            AutoLiveCodes.NotAttended => (all with { Owner = null }, entry),
            AutoLiveCodes.NotReady => (all with { Entry = f with { Ready = false } }, entry),
            AutoLiveCodes.FeedStale => (all with { FeedPriceAt = LimitsScene.Now.AddSeconds(-16) }, entry),
            AutoLiveCodes.ReconBreak => (all with { Entry = f with { ReconBreakToday = true } }, entry),
            AutoLiveCodes.Halted => (all with { Entry = f with { Halted = true } }, entry),
            AutoLiveCodes.OpenPositions => (all with { Entry = f with { OpenPositions = 3 } }, entry),
            AutoLiveCodes.LossDay => (all with { Entry = f with { RealisedToday = -600m, OpenRiskToStops = 100m } }, entry),
            _ => (all with { Entry = f with { OpenRiskToStops = null } }, entry),
        };
    }

    [Theory]
    [MemberData(nameof(RefusalCodes))]
    public void Each_rule_refuses_with_its_own_code_and_only_that_code(string code)
    {
        var (facts, entry) = Breaking(code);

        var verdict = Guard(facts, entry);

        verdict.Codes.ShouldBe([code]);
        verdict.Silent.ShouldBeFalse();
    }

    [Fact]
    public void An_unreadable_fact_never_passes()
    {
        var unread = new EntryFacts(null, null, null, null, null, null, null, null, null, null, null);

        var verdict = Guard(LimitsScene.All(facts: unread) with { StrategyUsedLive = null, FeedPriceAt = null });

        verdict.Codes.ShouldBe(
        [
            AutoLiveCodes.OpenPositions, AutoLiveCodes.LossUnavailable, AutoLiveCodes.PlanProvisional, AutoLiveCodes.ExposureDuplicate, AutoLiveCodes.StrategyFirstUse, AutoLiveCodes.PretradeFailed, AutoLiveCodes.NotUnlocked,
            AutoLiveCodes.NotReady, AutoLiveCodes.FeedStale, AutoLiveCodes.ReconBreak, AutoLiveCodes.Halted,
        ]);
    }

    [Fact]
    public void A_provisional_plan_is_refused_and_an_auto_approval_passes_only_with_its_switch_on()
    {
        var provisional = LimitsScene.All() with { Entry = LimitsScene.Facts() with { PlanProvisional = true } };
        var auto = LimitsScene.Entry() with { ApprovalKind = "auto" };
        var allowAuto = AutoLiveSettings.From(7, true, true, new Dictionary<string, string> { [AutoLiveSettings.AllowAutoApprovedKey] = "true" });

        Guard(provisional).Codes.ShouldBe([AutoLiveCodes.PlanProvisional]);
        Guard(entry: auto).Codes.ShouldBe([AutoLiveCodes.ApprovalNotOwner]);
        Guard(entry: auto, settings: allowAuto).Passed.ShouldBeTrue();
        Guard(entry: LimitsScene.Entry() with { ApprovalKind = null }, settings: allowAuto).Codes.ShouldBe([AutoLiveCodes.ApprovalNotOwner]);
    }

    [Fact]
    public void The_quantity_never_rounds_up_to_the_plan_and_an_unknown_allocation_refuses()
    {
        Guard(entry: LimitsScene.Entry() with { Quantity = 120, PlanAllocation = 120 }).Passed.ShouldBeTrue();
        Guard(entry: LimitsScene.Entry() with { Quantity = 119, PlanAllocation = 120 }).Passed.ShouldBeTrue();
        Guard(entry: LimitsScene.Entry() with { PlanAllocation = null }).Codes.ShouldBe([AutoLiveCodes.QtyAbovePlan]);
    }

    [Fact]
    public void A_first_live_use_of_a_strategy_version_is_routed_to_a_ticket()
    {
        var verdict = Guard(LimitsScene.All() with { StrategyUsedLive = false });

        verdict.RouteToTicket.ShouldBeTrue();
        Guard(LimitsScene.All() with { StrategyUsedLive = false, Entry = LimitsScene.Facts() with { Halted = true } }).RouteToTicket.ShouldBeFalse();
    }

    [Fact]
    public void The_day_unlock_and_attendance_are_asked_only_while_their_settings_are_on()
    {
        var off = AutoLiveSettings.From(7, true, true, new Dictionary<string, string> { [AutoLiveSettings.RequireDayUnlockKey] = "false", [AutoLiveSettings.RequireAttendedKey] = "false" });
        var unattended = LimitsScene.All() with { Owner = null, Entry = LimitsScene.Facts() with { DayUnlocked = false } };

        Guard(unattended).Codes.ShouldBe([AutoLiveCodes.NotUnlocked, AutoLiveCodes.NotAttended]);
        Guard(unattended, settings: off).Passed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(9 * 60 + 59, false, true)]
    [InlineData(10 * 60, false, true)]
    [InlineData(10 * 60 + 1, false, false)]
    [InlineData(60, true, false)]
    [InlineData(0, false, true)]
    [InlineData(-120, false, false)]
    public void Attended_means_an_owner_session_seen_within_the_window_and_not_locked(int secondsAgo, bool locked, bool attended)
    {
        var verdict = Guard(LimitsScene.All() with { Owner = new OwnerSeen(LimitsScene.Now.AddSeconds(-secondsAgo), locked) });

        verdict.Passed.ShouldBe(attended);
        if (!attended)
        {
            verdict.Codes.ShouldBe([AutoLiveCodes.NotAttended]);
        }
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void A_feed_price_older_than_15_seconds_is_stale(int secondsAgo, bool fresh)
    {
        Guard(LimitsScene.All() with { FeedPriceAt = LimitsScene.Now.AddSeconds(-secondsAgo) }).Passed.ShouldBe(fresh);
    }

    public static TheoryData<ExecMode?, bool, bool> NotAutoCases() => new()
    {
        { ExecMode.AutoLive, false, true },
        { ExecMode.AutoLive, true, false },
        { ExecMode.AutoLive, false, false },
        { ExecMode.SemiAuto, true, true },
        { ExecMode.Assisted, true, true },
        { ExecMode.Paper, true, true },
        { null, true, true },
    };

    [Theory]
    [MemberData(nameof(NotAutoCases))]
    public void Without_auto_live_and_both_switches_on_the_answer_is_mode_not_auto_and_silent(ExecMode? mode, bool executionEnabled, bool autoLiveEnabled)
    {
        var settings = AutoLiveSettings.From(7, executionEnabled, autoLiveEnabled, null);

        var verdict = AutoLiveGuard.Evaluate(mode, settings, LimitsScene.Entry(), LimitsScene.All(), LimitsScene.Now);

        verdict.ShouldBe(GuardVerdict.NotAuto);
        verdict.Silent.ShouldBeTrue();
        AutoLiveGuard.Evaluate(ExecMode.AutoLive, null, LimitsScene.Entry(), LimitsScene.All(), LimitsScene.Now).Silent.ShouldBeTrue();
        AutoLiveSettings.None.AutoLiveEnabled.ShouldBeFalse();
    }

    [Fact]
    public void For_any_random_sequence_the_accepted_sum_stays_inside_the_day_caps_and_each_refusal_names_a_rule()
    {
        var known = typeof(AutoLiveCodes).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet(StringComparer.Ordinal);
        var random = new Random(20261023);

        for (var run = 0; run < 200; run++)
        {
            var day = new LedgerDay(0, 0m);
            for (var step = 0; step < 15; step++)
            {
                var entry = LimitsScene.Entry(quantity: random.Next(1, 200), price: decimal.Round((decimal)(random.NextDouble() * 900) + 1m, 2));
                var codes = Caps(entry, day);
                if (codes.Count == 0)
                {
                    day = new LedgerDay(day.Orders + 1, day.Notional + entry.Notional);
                }
                else
                {
                    codes.ShouldAllBe(c => known.Contains(c));
                }
            }

            day.Orders.ShouldBeLessThanOrEqualTo(LimitsScene.Sheet.MaxOrdersPerDay!.Value);
            day.Notional.ShouldBeLessThanOrEqualTo(LimitsScene.Sheet.MaxNotionalPerDay!.Value);
        }
    }

    [Fact]
    public void The_guard_is_fast_enough_to_run_inside_the_intent_handling()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            Guard();
        }

        (watch.Elapsed.TotalMilliseconds / 1_000).ShouldBeLessThan(5);
    }
}
