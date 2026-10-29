using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

/// <summary>Criterion 2: a missing, non-finite, impossible or stale input makes exactly the checks that read it unavailable, the verdict is Rejected, and nothing can override it.</summary>
public sealed class UnavailableTableTests
{
    private static readonly double?[] NumberDefects = [null, double.NaN, double.PositiveInfinity, -1];

    private static readonly TimeSpan Old = TimeSpan.FromHours(1);

    /// <summary>One input: how to put a defect into it and which checks read it.</summary>
    internal sealed record Row(string Name, Func<Facts, Facts> Break, string[] Expected);

    private static Row N(string name, Func<Facts, double?, Facts> set, double? defect, params string[] expected) =>
        new(name + "=" + (defect?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"), f => set(f, defect), expected);

    private static IEnumerable<Row> Numeric(string name, Func<Facts, double?, Facts> set, double?[] defects, params string[] expected) => defects.Select(d => N(name, set, d, expected));

    private static QuoteFacts GoodQuote(Facts f) => (f.M.Quote ?? Facts.Good().M.Quote)!.Value;

    private static Stamped<QuoteFacts> Q(Facts f, Func<QuoteFacts, QuoteFacts> change) => new(change(GoodQuote(f)), Facts.Now);

    internal static IEnumerable<Row> Rows()
    {
        var all = NumberDefects;
        var noNeg = all.Take(3).ToArray();
        var rows = new List<Row>();

        rows.AddRange(Numeric("entry", (f, v) => f with { I = f.I with { Entry = v } }, all, CheckCodes.LevelsValid, CheckCodes.QuantityValid, CheckCodes.LtpCollar, CheckCodes.CircuitBand, CheckCodes.MarginFunds, CheckCodes.RiskPerTrade, CheckCodes.ExposureHeadroom));
        rows.AddRange(Numeric("stop", (f, v) => f with { I = f.I with { Stop = v } }, all, CheckCodes.LevelsValid, CheckCodes.LtpCollar, CheckCodes.CircuitBand, CheckCodes.RiskPerTrade, CheckCodes.ExposureHeadroom));
        rows.AddRange(Numeric("target", (f, v) => f with { I = f.I with { Target = v } }, all, CheckCodes.LevelsValid, CheckCodes.LtpCollar, CheckCodes.CircuitBand));
        rows.AddRange(Numeric("quantity", (f, v) => f with { I = f.I with { Quantity = v } }, all, CheckCodes.TicketValid, CheckCodes.QuantityValid, CheckCodes.SpreadDepth, CheckCodes.FreezeQuantity, CheckCodes.MarginFunds, CheckCodes.RiskPerTrade, CheckCodes.ExposureHeadroom, CheckCodes.AdvShare));
        rows.AddRange(Numeric("tick", (f, v) => f with { I = f.I with { Tick = v } }, all, CheckCodes.LevelsValid, CheckCodes.LtpCollar, CheckCodes.CircuitBand, CheckCodes.SpreadDepth));
        rows.AddRange(Numeric("capital", (f, v) => f with { A = f.A with { Capital = v } }, all, CheckCodes.MarginFunds, CheckCodes.RiskPerTrade, CheckCodes.ExposureHeadroom, CheckCodes.DailyLoss));
        rows.AddRange(Numeric("ltp", (f, v) => f with { M = f.M with { Quote = Q(f, q => q with { Ltp = v ?? double.NaN }) } }, all, CheckCodes.FeedFresh, CheckCodes.LtpCollar, CheckCodes.CircuitBand, CheckCodes.SpreadDepth));
        rows.AddRange(Numeric("bid", (f, v) => f with { M = f.M with { Quote = Q(f, q => q with { Bid = v ?? double.NaN }) } }, all, CheckCodes.SpreadDepth));
        rows.AddRange(Numeric("upper_circuit", (f, v) => f with { M = f.M with { Quote = Q(f, q => q with { UpperCircuit = v ?? double.NaN }) } }, all, CheckCodes.CircuitBand));
        rows.AddRange(Numeric("funds", (f, v) => f with { A = f.A with { FundsAvailable = new Stamped<double>(v ?? double.NaN, Facts.Now) } }, all, CheckCodes.MarginFunds));
        rows.AddRange(Numeric("reserved_margin", (f, v) => f with { A = f.A with { ReservedMargin = v } }, all, CheckCodes.MarginFunds));
        rows.AddRange(Numeric("intent_open_risk", (f, v) => f with { A = f.A with { IntentOpenRiskInr = v } }, all, CheckCodes.ExposureHeadroom));
        rows.AddRange(Numeric("today_pnl", (f, v) => f with { A = f.A with { TodayPnl = new Stamped<double>(v ?? double.NaN, Facts.Now) } }, noNeg, CheckCodes.DailyLoss));
        rows.AddRange(Numeric("adv", (f, v) => f with { M = f.M with { AverageDailyVolume = v } }, all, CheckCodes.AdvShare));
        rows.AddRange(Numeric("correlation", (f, v) => f with { M = f.M with { MaxCorrelation = v } }, [null, double.NaN, double.PositiveInfinity, 2], CheckCodes.Correlation));
        rows.AddRange(Numeric("freeze", (f, v) => f with { M = f.M with { FreezeQuantity = v } }, [double.NaN, double.PositiveInfinity, -1], CheckCodes.FreezeQuantity));

        // Stamped inputs: missing, older than the maximum age, and stamped in the future.
        foreach (var (name, expected, set) in new (string, string[], Func<Facts, DateTimeOffset?, Facts>)[]
        {
            ("quote", [CheckCodes.FeedFresh, CheckCodes.LtpCollar, CheckCodes.CircuitBand, CheckCodes.SpreadDepth], (f, at) => f with { M = f.M with { Quote = at is null ? null : new Stamped<QuoteFacts>(GoodQuote(f), at.Value) } }),
            ("kill_switch", [CheckCodes.BrokerKillSwitchOff], (f, at) => f with { S = f.S with { KillSwitchOff = at is null ? null : new Stamped<bool>(true, at.Value) } }),
            ("token_expiry", [CheckCodes.TokenValid], (f, at) => f with { S = f.S with { TokenExpiry = at is null ? null : new Stamped<DateTimeOffset>(Facts.Now.AddHours(2), at.Value) } }),
            ("static_ip", [CheckCodes.StaticIpOk], (f, at) => f with { S = f.S with { StaticIpAllowed = at is null ? null : new Stamped<bool>(true, at.Value) } }),
            ("funds", [CheckCodes.MarginFunds], (f, at) => f with { A = f.A with { FundsAvailable = at is null ? null : new Stamped<double>(100_000, at.Value) } }),
            ("margin", [CheckCodes.MarginFunds], (f, at) => f with { A = f.A with { Margin = at is null ? null : new Stamped<MarginFacts>(new MarginFacts(2_000, false), at.Value) } }),
            ("book", [CheckCodes.ExposureHeadroom, CheckCodes.OpenPositions, CheckCodes.OnePerSymbol], (f, at) => f with { A = f.A with { Book = at is null ? null : new Stamped<BookFacts>(new BookFacts(0, 0, false, false, null), at.Value) } }),
            ("today_pnl_stamp", [CheckCodes.DailyLoss], (f, at) => f with { A = f.A with { TodayPnl = at is null ? null : new Stamped<double>(0, at.Value) } }),
        })
        {
            rows.Add(new Row(name + " missing", f => set(f, null), expected));
            rows.Add(new Row(name + " stale", f => set(f, Facts.Now - Old), expected));
            rows.Add(new Row(name + " future", f => set(f, Facts.Now + TimeSpan.FromMinutes(5)), expected));
        }

        rows.Add(new Row("mode missing", f => f with { S = f.S with { Mode = null } }, [CheckCodes.GateOpen]));
        rows.Add(new Row("execution halt unreadable", f => f with { S = f.S with { ExecutionHalt = HaltReading.Unreadable } }, [CheckCodes.NoHalt]));
        rows.Add(new Row("live halt missing", f => f with { S = f.S with { LiveHalt = null } }, [CheckCodes.NoHalt]));
        rows.Add(new Row("stream and poll unknown", f => f with { S = f.S with { StreamConnected = null, LastOrderPollAt = null } }, [CheckCodes.StreamsAlive]));
        rows.Add(new Row("trading day unknown", f => f with { M = f.M with { TradingDay = null } }, [CheckCodes.SessionWindow]));
        rows.Add(new Row("ticket missing", f => f with { I = f.I with { Ticket = null } }, [CheckCodes.TicketValid]));
        rows.Add(new Row("instrument missing", f => f with { M = f.M with { Instrument = null } }, [CheckCodes.InstrumentTradable]));
        rows.Add(new Row("security id missing", f => f with { I = f.I with { SecurityId = null } }, [CheckCodes.InstrumentTradable]));
        rows.Add(new Row("side missing", f => f with { I = f.I with { Side = null } }, [CheckCodes.LevelsValid, CheckCodes.ProductSegment, CheckCodes.LtpCollar, CheckCodes.CircuitBand, CheckCodes.HoldingsConflict]));
        rows.Add(new Row("segment missing", f => f with { I = f.I with { Segment = null } }, [CheckCodes.ProductSegment]));
        rows.Add(new Row("product missing", f => f with { I = f.I with { Product = null } }, [CheckCodes.ProductSegment]));
        rows.Add(new Row("canary setting unreadable", f => f with { S = f.S with { CanaryPending = null } }, [CheckCodes.CanaryCap]));
        rows.Add(new Row("orders today unknown", f => f with { S = f.S with { OrdersToday = null } }, [CheckCodes.OrderBudget]));
        rows.Add(new Row("limiter state unknown", f => f with { S = f.S with { LimiterHasRoom = null } }, [CheckCodes.OrderBudget]));
        rows.Add(new Row("entries today unknown", f => f with { A = f.A with { EntriesToday = null } }, [CheckCodes.EntriesToday]));
        rows.Add(new Row("own open entries unknown", f => f with { A = f.A with { OpenIntentEntries = null } }, [CheckCodes.OpenPositions]));
        rows.Add(new Row("own intent in symbol unknown", f => f with { A = f.A with { IntentInSymbol = null } }, [CheckCodes.OnePerSymbol]));
        rows.Add(new Row("depth unknown", f => f with { M = f.M with { Quote = Q(f, q => q with { OpposingDepth = null }) } }, [CheckCodes.SpreadDepth]));
        rows.Add(new Row("profile missing", f => f with { A = f.A with { Profile = null } }, [CheckCodes.RiskPerTrade, CheckCodes.ExposureHeadroom, CheckCodes.OpenPositions, CheckCodes.EntriesToday, CheckCodes.DailyLoss, CheckCodes.SectorExposure]));
        rows.Add(new Row("profile risk pct zero", f => f with { A = f.A with { Profile = (f.A.Profile ?? Facts.Good().A.Profile!) with { RiskPerTradePct = 0 } } }, [CheckCodes.RiskPerTrade, CheckCodes.ExposureHeadroom, CheckCodes.OpenPositions, CheckCodes.EntriesToday, CheckCodes.DailyLoss, CheckCodes.SectorExposure]));
        rows.Add(new Row("sector missing", f => f with { A = f.A with { Sector = null } }, [CheckCodes.SectorExposure]));
        rows.Add(new Row("sector count unknown", f => f with { A = f.A with { SectorPositions = null } }, [CheckCodes.SectorExposure]));
        rows.Add(new Row("open trade unknown", f => f with { A = f.A with { OpenTradeExists = null } }, [CheckCodes.HoldingsConflict]));
        rows.Add(new Row("results date unknown", f => f with { M = f.M with { DaysToResults = null } }, [CheckCodes.ResultsWithinDays]));
        rows.Add(new Row("ban list unknown", f => f with { M = f.M with { InFnoBan = null } }, [CheckCodes.FnoBan]));
        rows.Add(new Row("surveillance unknown", f => f with { M = f.M with { UnderSurveillance = null } }, [CheckCodes.Surveillance]));
        rows.Add(new Row("validation status unknown", f => f with { M = f.M with { DataQualityOk = null } }, [CheckCodes.DataQuality]));
        return rows;
    }

    public static TheoryData<string> Names() => [.. Rows().Select(r => r.Name)];

    [Fact]
    public void The_table_has_at_least_80_rows()
    {
        Rows().Count().ShouldBeGreaterThanOrEqualTo(80);
        Rows().Select(r => r.Name).Distinct().Count().ShouldBe(Rows().Count());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_defective_input_makes_exactly_its_checks_unavailable_and_never_clears(string name)
    {
        var row = Rows().Single(r => r.Name == name);

        var run = row.Break(Facts.Good()).Run();

        run.Results.Where(r => r.Outcome == CheckOutcome.Unavailable).Select(r => r.Code).Order().ShouldBe(row.Expected.Order(), name);
        run.Verdict.ShouldBe(CheckVerdict.Rejected, name);
        run.AuthorisesSend.ShouldBeFalse();
    }

    [Fact]
    public void Even_an_owner_confirmation_cannot_clear_an_unavailable_hard_input_or_an_unavailable_soft_one_it_does_not_name()
    {
        var f = Facts.Good();
        var hard = f with { S = f.S with { KillSwitchOff = null } };
        hard.Run(confirmation: new CheckConfirmation("owner:hem", "go", [CheckCodes.FnoBan], Facts.Now)).Verdict.ShouldBe(CheckVerdict.Rejected);
        hard.Run(confirmation: new CheckConfirmation("owner:hem", "go", [CheckCodes.BrokerKillSwitchOff], Facts.Now)).Verdict.ShouldBe(CheckVerdict.Rejected);
    }
}
