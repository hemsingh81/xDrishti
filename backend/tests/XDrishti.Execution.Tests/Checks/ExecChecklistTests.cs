using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

public sealed class ExecChecklistTests
{
    [Fact]
    public void The_good_case_clears_all_34_in_the_fixed_order_with_26_hard_and_8_soft()
    {
        var run = Facts.Good().Run();

        run.Verdict.ShouldBe(CheckVerdict.Cleared);
        run.AuthorisesSend.ShouldBeTrue();
        run.Results.Select(r => r.Code).ShouldBe(CheckCodes.All);
        run.Results.Count.ShouldBe(34);
        run.Results.Count(r => r.Class == CheckClass.Hard).ShouldBe(26);
        run.Results.Count(r => r.Class == CheckClass.Soft).ShouldBe(8);
        run.Results.ShouldAllBe(r => r.Outcome == CheckOutcome.Pass && r.Source.Length > 0 && r.Detail.Length > 0);
        run.ValidUntil.ShouldBe(Facts.Now.AddSeconds(10));
    }

    [Fact]
    public void Two_runs_on_the_same_inputs_are_identical()
    {
        var a = Facts.Good().Run();
        var b = Facts.Good().Run();

        b.Results.ShouldBe(a.Results);
        b.InputsHash.ShouldBe(a.InputsHash);
        b.SettingsHash.ShouldBe(a.SettingsHash);
    }

    [Fact]
    public void Every_check_runs_even_when_the_first_fails()
    {
        var f = Facts.Good();
        var bad = f with { S = f.S with { Mode = new ModeAnswer(ExecMode.Paper, true) }, I = f.I with { Side = "SELL" } };

        var run = bad.Run();

        run.Results.Count.ShouldBe(34);
        run.Verdict.ShouldBe(CheckVerdict.Rejected);
        run.Results.First(r => r.Code == CheckCodes.GateOpen).Outcome.ShouldBe(CheckOutcome.Fail);
        run.Results.First(r => r.Code == CheckCodes.ProductSegment).Detail.ShouldContain("short_not_supported");
    }

    [Fact]
    public void A_fast_run_judges_the_seven_time_critical_checks_only_and_an_advisory_run_never_authorises()
    {
        var f = Facts.Good();

        f.Run(CheckRunKind.Fast).Results.Select(r => r.Code).ShouldBe(CheckCodes.Fast.OrderBy(c => CheckCodes.All.ToList().IndexOf(c)));
        f.Run(CheckRunKind.Fast).AuthorisesSend.ShouldBeFalse();

        var advisory = (f with { S = f.S with { Mode = new ModeAnswer(ExecMode.Assisted, false) } }).Run(CheckRunKind.Advisory);
        advisory.Results.Count.ShouldBe(34);
        advisory.Results.First(r => r.Code == CheckCodes.GateOpen).Detail.ShouldContain("advisory");
        advisory.AuthorisesSend.ShouldBeFalse();
    }

    [Fact]
    public void A_gate_that_is_closed_fails_each_way_and_a_missing_mode_answer_is_unavailable()
    {
        var f = Facts.Good();
        (f with { S = f.S with { Mode = new ModeAnswer(ExecMode.SemiAuto, false) } }).Result(CheckCodes.GateOpen).Outcome.ShouldBe(CheckOutcome.Fail);
        (f with { S = f.S with { Environment = ExecEnvironment.None } }).Result(CheckCodes.GateOpen).Outcome.ShouldBe(CheckOutcome.Fail);
        (f with { S = f.S with { Mode = null } }).Result(CheckCodes.GateOpen).Outcome.ShouldBe(CheckOutcome.Unavailable);
    }

    // Each row: a defect that must make exactly the named Hard check FAIL (not merely unavailable), proving the check can reject.
    public static TheoryData<string, string> FailingRows => new()
    {
        { CheckCodes.NoHalt, "halted" },
        { CheckCodes.BrokerKillSwitchOff, "kill" },
        { CheckCodes.TokenValid, "token" },
        { CheckCodes.StaticIpOk, "ip" },
        { CheckCodes.SessionWindow, "window" },
        { CheckCodes.StreamsAlive, "stream" },
        { CheckCodes.TicketValid, "ticket" },
        { CheckCodes.LevelsValid, "levels" },
        { CheckCodes.QuantityValid, "qty" },
        { CheckCodes.ProductSegment, "segment" },
        { CheckCodes.InstrumentTradable, "tradable" },
        { CheckCodes.LtpCollar, "collar" },
        { CheckCodes.CircuitBand, "circuit" },
        { CheckCodes.SpreadDepth, "spread" },
        { CheckCodes.FreezeQuantity, "freeze" },
        { CheckCodes.MarginFunds, "margin" },
        { CheckCodes.RiskPerTrade, "risk" },
        { CheckCodes.ExposureHeadroom, "exposure" },
        { CheckCodes.OpenPositions, "open" },
        { CheckCodes.EntriesToday, "entries" },
        { CheckCodes.DailyLoss, "loss" },
        { CheckCodes.OnePerSymbol, "symbol" },
        { CheckCodes.OrderBudget, "budget" },
        { CheckCodes.CanaryCap, "canary" },
        { CheckCodes.SectorExposure, "sector" },
        { CheckCodes.HoldingsConflict, "holdings" },
        { CheckCodes.AdvShare, "adv" },
        { CheckCodes.ResultsWithinDays, "results" },
        { CheckCodes.FnoBan, "ban" },
        { CheckCodes.Surveillance, "surv" },
        { CheckCodes.Correlation, "corr" },
        { CheckCodes.DataQuality, "dq" },
    };

    private static Facts Break(Facts f, string key) => key switch
    {
        "halted" => f with { S = f.S with { LiveHalt = HaltReading.Halted } },
        "kill" => f with { S = f.S with { KillSwitchOff = new Stamped<bool>(false, Facts.Now) } },
        "token" => f with { S = f.S with { TokenExpiry = new Stamped<DateTimeOffset>(Facts.Now.AddMinutes(19), Facts.Now) } },
        "ip" => f with { S = f.S with { StaticIpAllowed = new Stamped<bool>(false, Facts.Now) } },
        "window" => f with { M = f.M with { TradingDay = false } },
        "stream" => f with { S = f.S with { StreamConnected = new Stamped<bool>(false, Facts.Now) } },
        "ticket" => f with { I = f.I with { Ticket = new TicketFacts(TicketState.Expired, "owner", false, 10) } },
        "levels" => f with { I = f.I with { Target = 1500.05 } },
        "qty" => f with { I = f.I with { Quantity = 2500, Ticket = new TicketFacts(TicketState.Armed, "owner", false, 2500) } },
        "segment" => f with { I = f.I with { Side = "SELL" } },
        "tradable" => f with { M = f.M with { Instrument = new InstrumentFacts(true, true, true) } },
        "collar" => f with { M = f.M with { Quote = new Stamped<QuoteFacts>(new QuoteFacts(1540, 1539.9, 1540.1, 1650, 1350, 100), Facts.Now) } },
        "circuit" => f with { M = f.M with { Quote = new Stamped<QuoteFacts>(new QuoteFacts(1500, 1499.9, 1500.1, 1500.05, 1350, 100), Facts.Now) } },
        "spread" => f with { M = f.M with { Quote = new Stamped<QuoteFacts>(new QuoteFacts(1500, 1498, 1502, 1650, 1350, 100), Facts.Now) } },
        "freeze" => f with { M = f.M with { FreezeQuantity = 5 } },
        "margin" => f with { A = f.A with { Margin = new Stamped<MarginFacts>(new MarginFacts(2_000, true), Facts.Now) } },
        "risk" => f with { I = f.I with { Stop = 1480, Target = 1530, Quantity = 70, Ticket = new TicketFacts(TicketState.Armed, "owner", false, 70) } },
        "exposure" => f with { A = f.A with { IntentOpenRiskInr = 14_900 } },
        "open" => f with { A = f.A with { OpenIntentEntries = 3 } },
        "entries" => f with { A = f.A with { EntriesToday = 5 } },
        "loss" => f with { A = f.A with { TodayPnl = new Stamped<double>(-5000, Facts.Now) } },
        "symbol" => f with { A = f.A with { Book = new Stamped<BookFacts>(new BookFacts(0, 1, true, false, "foreign order in the instrument"), Facts.Now) } },
        "budget" => f with { S = f.S with { OrdersToday = 200 } },
        "canary" => f with { S = f.S with { CanaryPending = true } },
        "sector" => f with { A = f.A with { SectorPositions = 2 } },
        "holdings" => f with { A = f.A with { OpenTradeExists = true } },
        "adv" => f with { M = f.M with { AverageDailyVolume = 100 } },
        "results" => f with { M = f.M with { DaysToResults = 3 } },
        "ban" => f with { M = f.M with { InFnoBan = true } },
        "surv" => f with { M = f.M with { UnderSurveillance = true } },
        "corr" => f with { M = f.M with { MaxCorrelation = 0.71 } },
        "dq" => f with { M = f.M with { DataQualityOk = false } },
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    [Theory]
    [MemberData(nameof(FailingRows))]
    public void Each_check_can_reject_on_its_own(string code, string key)
    {
        var broken = Break(Facts.Good(), key);

        broken.Result(code).Outcome.ShouldBe(CheckOutcome.Fail, code);
        var run = broken.Run();
        run.Verdict.ShouldBe(CheckVerdict.Rejected);
        run.AuthorisesSend.ShouldBeFalse();
        run.Results.Where(r => r.Outcome != CheckOutcome.Pass).Select(r => r.Code).ShouldContain(code);
    }

    [Fact]
    public void A_stale_feed_is_unavailable_and_a_stream_down_with_an_old_poll_fails()
    {
        var f = Facts.Good();
        (f with { M = f.M with { Quote = new Stamped<QuoteFacts>(f.M.Quote!.Value, Facts.Now.AddSeconds(-6)) } }).Result(CheckCodes.FeedFresh).Outcome.ShouldBe(CheckOutcome.Unavailable);
        (f with { M = f.M with { Quote = new Stamped<QuoteFacts>(f.M.Quote!.Value, Facts.Now.AddSeconds(-5)) } }).Result(CheckCodes.FeedFresh).Outcome.ShouldBe(CheckOutcome.Pass);
        var down = f with { S = f.S with { StreamConnected = new Stamped<bool>(false, Facts.Now), LastOrderPollAt = Facts.Now.AddSeconds(-16) } };
        down.Result(CheckCodes.StreamsAlive).Outcome.ShouldBe(CheckOutcome.Fail);
        (down with { S = down.S with { LastOrderPollAt = Facts.Now.AddSeconds(-15) } }).Result(CheckCodes.StreamsAlive).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void The_canary_cap_lifts_only_when_the_setting_is_off_and_an_unreadable_setting_is_unavailable()
    {
        var f = Facts.Good();
        var pending = f with { S = f.S with { CanaryPending = true } };
        pending.Result(CheckCodes.CanaryCap).Outcome.ShouldBe(CheckOutcome.Fail);
        (pending with { I = pending.I with { Quantity = 1, Ticket = new TicketFacts(TicketState.Armed, "owner", false, 1) } }).Result(CheckCodes.CanaryCap).Outcome.ShouldBe(CheckOutcome.Pass);
        (f with { S = f.S with { CanaryPending = null } }).Result(CheckCodes.CanaryCap).Outcome.ShouldBe(CheckOutcome.Unavailable);
    }

    [Fact]
    public void The_freeze_check_passes_by_the_own_cap_when_no_source_exists_and_says_so()
    {
        var r = Facts.Good().Result(CheckCodes.FreezeQuantity);

        r.Outcome.ShouldBe(CheckOutcome.Pass);
        r.Detail.ShouldContain("own cap applies");
    }

    [Fact]
    public void Foreign_activity_is_named_in_the_one_per_symbol_detail()
    {
        var f = Facts.Good();
        var r = (f with { A = f.A with { Book = new Stamped<BookFacts>(new BookFacts(0, 0, false, true, "working order without an XD client id"), Facts.Now) } }).Result(CheckCodes.OnePerSymbol);

        r.Outcome.ShouldBe(CheckOutcome.Fail);
        r.Detail.ShouldContain("working order without an XD client id");
    }

    [Fact]
    public void Limits_come_from_the_registry_inside_its_range_and_an_out_of_range_value_makes_them_unavailable()
    {
        ExecLimits.FromValues(new Dictionary<string, string>()).ShouldBe(ExecLimits.Default);
        ExecLimits.FromValues(new Dictionary<string, string> { ["exec.max_loss_per_trade_inr"] = "50" }).ShouldBeNull();
        ExecLimits.FromValues(new Dictionary<string, string> { ["exec.check_valid_for_s"] = "61" }).ShouldBeNull();
        ExecLimits.FromValues(new Dictionary<string, string> { ["exec.entry_window_start"] = "15:30", ["exec.entry_window_end"] = "15:00" }).ShouldBeNull();
        ExecLimits.FromValues(new Dictionary<string, string> { ["exec.max_spread_ticks"] = "5" })!.MaxSpreadTicks.ShouldBe(5);
    }
}
