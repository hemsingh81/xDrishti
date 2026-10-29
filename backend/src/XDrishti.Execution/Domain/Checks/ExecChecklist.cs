using System.Globalization;

namespace XDrishti.Execution.Domain.Checks;

/// <summary>
/// The pre-send checklist (spec P12-03, ADR 0159): one pure function judging an entry intent against 34 checks (26 Hard, 8 Soft) in a fixed order, with no short-circuit. An input that is missing, not finite, impossible or older than its maximum
/// age makes its check <see cref="CheckOutcome.Unavailable"/>; <see cref="CheckOutcome.Unavailable"/> never passes and no Hard check can be overridden. The function reads nothing and sends nothing; it can only make a send stricter.
/// Exits and protective stops are not judged here (they never reach this list).
/// </summary>
public static class ExecChecklist
{
    // "At the limit passes" must survive floating-point arithmetic (the P7-05 tolerance).
    private const double Epsilon = 1e-9;

    private static readonly TimeSpan Funds = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Profile = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaticIp = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Stream = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);

    private delegate CheckResult Check(Ctx c);

    private sealed record Ctx(IntentFacts I, AccountFacts A, MarketFacts M, SystemFacts S, ExecLimits L, DateTimeOffset Now);

    private static readonly (string Code, Check Run)[] Table =
    [
        (CheckCodes.GateOpen, GateOpen), (CheckCodes.NoHalt, NoHalt), (CheckCodes.BrokerKillSwitchOff, KillSwitch), (CheckCodes.TokenValid, Token),
        (CheckCodes.StaticIpOk, Ip), (CheckCodes.SessionWindow, Window), (CheckCodes.FeedFresh, Feed), (CheckCodes.StreamsAlive, Streams),
        (CheckCodes.TicketValid, Ticket), (CheckCodes.LevelsValid, Levels), (CheckCodes.QuantityValid, QuantityOk), (CheckCodes.ProductSegment, ProductSegment),
        (CheckCodes.InstrumentTradable, Tradable), (CheckCodes.LtpCollar, Collar), (CheckCodes.CircuitBand, Circuit), (CheckCodes.SpreadDepth, SpreadDepth),
        (CheckCodes.FreezeQuantity, Freeze), (CheckCodes.MarginFunds, Margin), (CheckCodes.RiskPerTrade, Risk), (CheckCodes.ExposureHeadroom, Exposure),
        (CheckCodes.OpenPositions, OpenPositions), (CheckCodes.EntriesToday, EntriesToday), (CheckCodes.DailyLoss, DailyLoss), (CheckCodes.OnePerSymbol, OnePerSymbol),
        (CheckCodes.OrderBudget, OrderBudget), (CheckCodes.CanaryCap, Canary),
        (CheckCodes.SectorExposure, Sector), (CheckCodes.HoldingsConflict, Holdings), (CheckCodes.AdvShare, Adv), (CheckCodes.ResultsWithinDays, Results),
        (CheckCodes.FnoBan, Ban), (CheckCodes.Surveillance, SurveillanceCheck), (CheckCodes.Correlation, CorrelationCheck), (CheckCodes.DataQuality, DataQualityCheck),
    ];

    /// <summary>
    /// Judges the intent. <c>Full</c> and <c>Advisory</c> evaluate all 34 (advisory shows <c>gate_open</c> as not applicable and is never an authorisation); <c>Fast</c> evaluates the seven time-critical checks only.
    /// A <paramref name="confirmation"/> counts only in <c>semi_auto</c>, from an <c>owner:</c> actor, and only when it names exactly the Soft codes that did not pass in this run.
    /// </summary>
    public static CheckRun Evaluate(CheckRunKind kind, IntentFacts intent, AccountFacts account, MarketFacts market, SystemFacts system, ExecLimits limits, DateTimeOffset now, CheckConfirmation? confirmation = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(limits);

        var ctx = new Ctx(intent, account, market, system, limits, now);
        var results = new List<CheckResult>(Table.Length);
        foreach (var (code, run) in Table)
        {
            if (kind == CheckRunKind.Fast && !CheckCodes.Fast.Contains(code))
            {
                continue;
            }

            results.Add(kind == CheckRunKind.Advisory && code == CheckCodes.GateOpen
                ? new CheckResult(code, CheckClass.Hard, CheckOutcome.Pass, null, null, "not applicable: an advisory run never authorises a send", null, "system")
                : run(ctx));
        }

        var hardBad = results.Any(r => r.Class == CheckClass.Hard && r.Outcome != CheckOutcome.Pass);
        var softBad = results.Where(r => r.Class == CheckClass.Soft && r.Outcome != CheckOutcome.Pass).Select(r => r.Code).Order(StringComparer.Ordinal).ToArray();
        var verdict = hardBad ? CheckVerdict.Rejected
            : softBad.Length == 0 ? CheckVerdict.Cleared
            : Covers(confirmation, softBad, system.Mode) ? CheckVerdict.ClearedByConfirmation
            : CheckVerdict.Rejected;

        var validFor = TimeSpan.FromSeconds(limits.CheckValidForS);
        return new CheckRun(kind, now, now + validFor, verdict, limits.Hash, CheckRun.HashOf(results), results, verdict == CheckVerdict.ClearedByConfirmation ? confirmation : null);
    }

    /// <summary>
    /// Whether a confirmation may be recorded for <paramref name="run"/> (spec criterion 4): a full run in <c>semi_auto</c> with no Hard non-pass, an <c>owner:</c> actor, a reason, and exactly the run's Soft non-pass codes.
    /// Returns the refusal code <c>execution.confirm_refused</c> reason, or null when the confirmation is acceptable.
    /// </summary>
    public static string? RefuseConfirmation(CheckRun run, CheckConfirmation confirmation, ExecMode? mode)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(confirmation);
        if (mode != ExecMode.SemiAuto)
        {
            return "mode_not_semi_auto";
        }

        if (run.Kind != CheckRunKind.Full)
        {
            return "run_not_full";
        }

        if (run.HardNonPass.Count > 0 || confirmation.Codes.Any(c => CheckCodes.ClassOf(c) == CheckClass.Hard || !CheckCodes.All.Contains(c)))
        {
            return "hard_check_not_confirmable";
        }

        if (!OwnerActor(confirmation.Actor) || string.IsNullOrWhiteSpace(confirmation.Reason))
        {
            return "actor_or_reason_invalid";
        }

        return SameSet(confirmation.Codes, run.SoftNonPass) && run.SoftNonPass.Count > 0 ? null : "codes_do_not_match_run";
    }

    private static bool Covers(CheckConfirmation? confirmation, string[] softBad, ModeAnswer? mode) =>
        confirmation is not null && mode is { EffectiveMode: ExecMode.SemiAuto } && OwnerActor(confirmation.Actor) && !string.IsNullOrWhiteSpace(confirmation.Reason)
        && confirmation.Codes.All(c => CheckCodes.ClassOf(c) == CheckClass.Soft) && SameSet(confirmation.Codes, softBad);

    private static bool OwnerActor(string actor) => actor.StartsWith("owner:", StringComparison.Ordinal) && actor.Length > "owner:".Length;

    private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) => a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------------------------

    private static bool AtMost(double value, double limit) => value <= limit + Epsilon;

    private static bool AtLeast(double value, double limit) => value >= limit - Epsilon;

    private static bool Below(double value, double limit) => value < limit - Epsilon;

    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static double? Pos(double? v) => v is { } x && double.IsFinite(x) && x > 0 ? x : null;

    private static double? NonNeg(double? v) => v is { } x && double.IsFinite(x) && x >= 0 ? x : null;

    private static double? Any(double? v) => v is { } x && double.IsFinite(x) ? x : null;

    /// <summary>The value when the stamp exists, is not in the future and is no older than <paramref name="max"/>; otherwise null. <paramref name="age"/> is the age in ms when known.</summary>
    private static T? Fresh<T>(Stamped<T>? s, DateTimeOffset now, TimeSpan max, out long? age)
    {
        age = null;
        if (s is null)
        {
            return default;
        }

        var span = now - s.At;
        age = (long)span.TotalMilliseconds;
        return span < TimeSpan.Zero || span > max ? default : s.Value;
    }

    private static bool IsFresh<T>(Stamped<T>? s, DateTimeOffset now, TimeSpan max) => s is not null && now - s.At >= TimeSpan.Zero && now - s.At <= max;

    private static CheckResult Un(string code, string why, string source, long? age = null) => new(code, CheckCodes.ClassOf(code), CheckOutcome.Unavailable, null, null, "unavailable: " + why, age, source);

    private static CheckResult Judge(string code, bool pass, double? value, double? limit, string detail, string source, long? age = null) =>
        new(code, CheckCodes.ClassOf(code), pass ? CheckOutcome.Pass : CheckOutcome.Fail, value, limit, detail, age, source);

    private static bool IsBuy(string? side) => side == "BUY";

    private static bool Side(string? side) => side is "BUY" or "SELL";

    // The risk of this instruction in rupees, or null when entry, stop or quantity cannot be used.
    private static double? RiskInr(IntentFacts i) =>
        Pos(i.Entry) is { } e && Pos(i.Stop) is { } s && WholeQty(i.Quantity) is { } q ? Math.Abs(e - s) * q : null;

    private static double? WholeQty(double? q) => Pos(q);

    private static long? Age(params long?[] ages) => ages.Any(a => a is not null) ? ages.Where(a => a is not null).Max() : null;

    // ---- gates ---------------------------------------------------------------------------------------------------------------------------------------------

    private static CheckResult GateOpen(Ctx c)
    {
        const string code = CheckCodes.GateOpen;
        if (c.S.Mode is null)
        {
            return Un(code, "no mode answer for the account", "system");
        }

        var d = AccountGate.MayAct(c.S.Mode, c.S.Environment);
        return Judge(code, d.Open, null, null, d.Open ? "execution enabled, executing mode, environment set" : "gate closed: " + d.Reason, "system");
    }

    private static CheckResult NoHalt(Ctx c)
    {
        const string code = CheckCodes.NoHalt;
        if (c.S.ExecutionHalt == HaltReading.Halted || c.S.LiveHalt == HaltReading.Halted)
        {
            return Judge(code, false, null, null, "an active halt exists", "execution_db");
        }

        return c.S.ExecutionHalt is not HaltReading.Clear || c.S.LiveHalt is not HaltReading.Clear
            ? Un(code, "a halt store is not readable", "execution_db")
            : Judge(code, true, null, null, "no active halt", "execution_db");
    }

    private static CheckResult KillSwitch(Ctx c)
    {
        var v = Fresh(c.S.KillSwitchOff, c.Now, Funds, out var age);
        return c.S.KillSwitchOff is null || !IsFresh(c.S.KillSwitchOff, c.Now, Funds)
            ? Un(CheckCodes.BrokerKillSwitchOff, "kill switch state missing or older than 60 s", "broker", age)
            : Judge(CheckCodes.BrokerKillSwitchOff, v, null, null, v ? "kill switch is DEACTIVATE" : "broker kill switch is active", "broker", age);
    }

    private static CheckResult Token(Ctx c)
    {
        var expiry = Fresh(c.S.TokenExpiry, c.Now, Profile, out var age);
        if (!IsFresh(c.S.TokenExpiry, c.Now, Profile))
        {
            return Un(CheckCodes.TokenValid, "token expiry missing or read more than 5 min ago", "broker", age);
        }

        var remaining = (expiry - c.Now).TotalMinutes;
        return Judge(CheckCodes.TokenValid, AtLeast(remaining, c.L.TokenMinRemainingMin), remaining, c.L.TokenMinRemainingMin, "token valid for " + N(remaining) + " min, needs " + N(c.L.TokenMinRemainingMin), "broker", age);
    }

    private static CheckResult Ip(Ctx c)
    {
        var v = Fresh(c.S.StaticIpAllowed, c.Now, StaticIp, out var age);
        return !IsFresh(c.S.StaticIpAllowed, c.Now, StaticIp)
            ? Un(CheckCodes.StaticIpOk, "static IP state missing or older than 15 min", "owner_attestation", age)
            : Judge(CheckCodes.StaticIpOk, v, null, null, v ? "orders allowed for the IP" : "orders not allowed for the detected IP", "owner_attestation", age);
    }

    private static CheckResult Window(Ctx c)
    {
        if (c.M.TradingDay is not { } day)
        {
            return Un(CheckCodes.SessionWindow, "calendar does not say whether today is a trading day", "calendar");
        }

        var ist = TimeOnly.FromDateTime(c.Now.ToOffset(IstOffset).DateTime);
        var inside = day && ist >= c.L.EntryWindowStart && ist <= c.L.EntryWindowEnd;
        return Judge(CheckCodes.SessionWindow, inside, ist.Hour * 60 + ist.Minute, null, day ? "IST " + ist.ToString("HH:mm", CultureInfo.InvariantCulture) + ", window " + c.L.EntryWindowStart.ToString("HH:mm", CultureInfo.InvariantCulture) + " to " + c.L.EntryWindowEnd.ToString("HH:mm", CultureInfo.InvariantCulture) : "not a trading day", "calendar");
    }

    private static QuoteFacts? Quote(Ctx c, TimeSpan max, out long? age)
    {
        var q = Fresh(c.M.Quote, c.Now, max, out age);
        return q is not null && IsFresh(c.M.Quote, c.Now, max) && Pos(q.Ltp) is not null ? q : null;
    }

    private static TimeSpan FeedAge(Ctx c) => TimeSpan.FromSeconds(c.L.FeedMaxAgeS);

    private static CheckResult Feed(Ctx c)
    {
        var q = Quote(c, FeedAge(c), out var age);
        return q is null
            ? Un(CheckCodes.FeedFresh, "no usable live quote within " + N(c.L.FeedMaxAgeS) + " s", "broker", age)
            : Judge(CheckCodes.FeedFresh, true, age, c.L.FeedMaxAgeS * 1000, "quote is " + age + " ms old", "broker", age);
    }

    private static CheckResult Streams(Ctx c)
    {
        var connected = IsFresh(c.S.StreamConnected, c.Now, Stream) && c.S.StreamConnected!.Value;
        var pollAge = c.S.LastOrderPollAt is { } p ? c.Now - p : (TimeSpan?)null;
        var pollFresh = pollAge is { } a && a >= TimeSpan.Zero && a <= Stream;
        if (c.S.StreamConnected is null && c.S.LastOrderPollAt is null)
        {
            return Un(CheckCodes.StreamsAlive, "neither the order stream state nor an order-book poll is known", "broker");
        }

        return Judge(CheckCodes.StreamsAlive, connected || pollFresh, null, null, connected ? "order stream connected" : pollFresh ? "order book polled within 15 s" : "stream down and no poll within 15 s", "broker", pollAge is { } pa ? (long)pa.TotalMilliseconds : null);
    }

    // ---- ticket and shape ----------------------------------------------------------------------------------------------------------------------------------

    private static CheckResult Ticket(Ctx c)
    {
        const string code = CheckCodes.TicketValid;
        if (c.I.Ticket is not { } t || WholeQty(c.I.Quantity) is not { } qty)
        {
            return Un(code, "the plan ticket or the quantity is not known", "plan");
        }

        if (t.State is not (TicketState.Armed or TicketState.Approved))
        {
            return Judge(code, false, null, null, "ticket is " + t.State, "plan");
        }

        if (!(t.ApprovalKind == "owner" || (t.ApprovalKind == "auto" && t.AutoSnapshotPresent)))
        {
            return Judge(code, false, null, null, "no owner approval, or an auto approval without its settings snapshot", "plan");
        }

        return Any(t.AllocatedQuantity) is not { } alloc
            ? Judge(code, false, null, null, "no allocation for this account", "plan")
            : Judge(code, Math.Abs(alloc - qty) < Epsilon, qty, alloc, "quantity " + N(qty) + ", allocation " + N(alloc), "plan");
    }

    private static CheckResult Levels(Ctx c)
    {
        const string code = CheckCodes.LevelsValid;
        var i = c.I;
        if (Pos(i.Entry) is not { } e || Pos(i.Stop) is not { } s || Pos(i.Target) is not { } t || Pos(i.Tick) is not { } tick || !Side(i.Side))
        {
            return Un(code, "entry, stop, target, tick or side is missing or impossible", "intent");
        }

        bool OnTick(double x) => Math.Abs((x / tick) - Math.Round(x / tick)) < 1e-6;
        var buy = IsBuy(i.Side);
        var sideOk = buy ? s < e && e < t : t < e && e < s;
        var distance = Math.Abs(e - s);
        var reward = Math.Abs(t - e);
        var rr = distance > 0 ? reward / distance : 0;
        var ok = OnTick(e) && OnTick(s) && OnTick(t) && sideOk && AtLeast(distance, 2 * tick) && AtLeast(distance, e * 0.002) && AtLeast(rr, 1.5);
        return Judge(code, ok, rr, 1.5, "stop distance " + N(distance) + ", reward/risk " + N(rr) + (sideOk ? string.Empty : ", stop or target on the wrong side") + (OnTick(e) && OnTick(s) && OnTick(t) ? string.Empty : ", a level is off the tick"), "intent");
    }

    private static CheckResult QuantityOk(Ctx c)
    {
        const string code = CheckCodes.QuantityValid;
        if (Pos(c.I.Quantity) is not { } q || Pos(c.I.Entry) is not { } e)
        {
            return Un(code, "quantity or entry price is missing or impossible", "intent");
        }

        var notional = q * e;
        var ok = Math.Abs(q - Math.Round(q)) < Epsilon && AtMost(q, c.L.MaxOrderQty) && AtMost(notional, c.L.MaxOrderValueInr);
        return Judge(code, ok, q, c.L.MaxOrderQty, "quantity " + N(q) + " (cap " + N(c.L.MaxOrderQty) + "), value " + N(notional) + " (cap " + N(c.L.MaxOrderValueInr) + ")", "intent");
    }

    private static CheckResult ProductSegment(Ctx c)
    {
        const string code = CheckCodes.ProductSegment;
        if (c.I.Segment is null || c.I.Product is null || !Side(c.I.Side))
        {
            return Un(code, "segment, product or side is missing", "intent");
        }

        if (c.I.Side == "SELL")
        {
            return Judge(code, false, null, null, "short_not_supported: entries are BUY only", "intent");
        }

        return Judge(code, c.I.Segment == "NSE_EQ" && c.I.Product == "INTRADAY", null, null, c.I.Segment + " " + c.I.Product + " BUY", "intent");
    }

    private static CheckResult Tradable(Ctx c)
    {
        if (c.M.Instrument is not { } m || string.IsNullOrWhiteSpace(c.I.SecurityId))
        {
            return Un(CheckCodes.InstrumentTradable, "the master does not answer for the security id", "master");
        }

        return Judge(CheckCodes.InstrumentTradable, m.IsinMatches && m.TradingAllowed && !m.Suspended, null, null, m.IsinMatches ? m.TradingAllowed && !m.Suspended ? "tradable" : "trading not allowed or suspended" : "symbol and ISIN do not match the master", "master");
    }

    // ---- prices --------------------------------------------------------------------------------------------------------------------------------------------

    private static CheckResult Collar(Ctx c)
    {
        const string code = CheckCodes.LtpCollar;
        var q = Quote(c, FeedAge(c), out var age);
        if (q is null || Pos(c.I.Entry) is not { } e || Pos(c.I.Stop) is not { } s || Pos(c.I.Target) is not { } t || Pos(c.I.Tick) is not { } tick || !Side(c.I.Side))
        {
            return Un(code, "live price or the levels are missing, stale or impossible", "broker", age);
        }

        var pct = Math.Abs(e - q.Ltp) / q.Ltp * 100;
        var buy = IsBuy(c.I.Side);
        var between = buy ? AtLeast(q.Ltp, s + (2 * tick)) && q.Ltp < t - Epsilon : AtMost(q.Ltp, s - (2 * tick)) && q.Ltp > t + Epsilon;
        return Judge(code, AtMost(pct, c.L.PriceBandPct) && between, pct, c.L.PriceBandPct, "entry is " + N(pct) + "% from LTP " + N(q.Ltp) + (between ? string.Empty : "; LTP has passed a level"), "broker", age);
    }

    private static CheckResult Circuit(Ctx c)
    {
        const string code = CheckCodes.CircuitBand;
        var q = Quote(c, FeedAge(c), out var age);
        if (q is null || Pos(q.LowerCircuit) is not { } lo || Pos(q.UpperCircuit) is not { } hi || lo >= hi || Pos(c.I.Entry) is not { } e || Pos(c.I.Stop) is not { } s || Pos(c.I.Target) is not { } t || Pos(c.I.Tick) is not { } tick || !Side(c.I.Side))
        {
            return Un(code, "circuit limits or the levels are missing, stale or impossible", "broker", age);
        }

        var stopOk = IsBuy(c.I.Side) ? s > lo : s < hi;
        var ok = AtLeast(e, lo + (2 * tick)) && AtMost(e, hi - (2 * tick)) && stopOk && t < hi - Epsilon && t > lo + Epsilon;
        return Judge(code, ok, e, e < lo + (2 * tick) ? lo : hi, "entry " + N(e) + ", band " + N(lo) + " to " + N(hi) + " (two ticks kept clear of an edge)", "broker", age);
    }

    private static CheckResult SpreadDepth(Ctx c)
    {
        const string code = CheckCodes.SpreadDepth;
        var q = Quote(c, FeedAge(c), out var age);
        if (q is null || Pos(q.Bid) is not { } bid || Pos(q.Ask) is not { } ask || ask < bid || Pos(c.I.Tick) is not { } tick || Pos(c.I.Quantity) is not { } qty || Pos(q.OpposingDepth) is not { } depth)
        {
            return Un(code, "bid, ask, depth, tick or quantity is missing, stale or impossible", "broker", age);
        }

        var spread = ask - bid;
        var allowed = Math.Max(c.L.MaxSpreadTicks * tick, c.L.MaxSpreadPct / 100 * q.Ltp);
        var need = c.L.MinDepthMultiple * qty;
        return Judge(code, AtMost(spread, allowed) && AtLeast(depth, need), spread, allowed, "spread " + N(spread) + " (allowed " + N(allowed) + "), depth " + N(depth) + " (needs " + N(need) + ")", "broker", age);
    }

    private static CheckResult Freeze(Ctx c)
    {
        const string code = CheckCodes.FreezeQuantity;
        if (Pos(c.I.Quantity) is not { } q)
        {
            return Un(code, "quantity is missing or impossible", "intent");
        }

        if (c.M.FreezeQuantity is null)
        {
            return Judge(code, AtMost(q, c.L.MaxOrderQty), q, c.L.MaxOrderQty, "own cap applies (no exchange freeze source for cash equity)", "settings");
        }

        if (Pos(c.M.FreezeQuantity) is not { } freeze)
        {
            return Un(code, "the freeze quantity is not a usable number", "master");
        }

        var limit = Math.Min(c.L.MaxOrderQty, freeze);
        return Judge(code, AtMost(q, limit), q, limit, "quantity " + N(q) + ", limit " + N(limit), "master");
    }

    // ---- money and risk ------------------------------------------------------------------------------------------------------------------------------------

    private static CheckResult Margin(Ctx c)
    {
        const string code = CheckCodes.MarginFunds;
        var funds = Fresh(c.A.FundsAvailable, c.Now, Funds, out var a1);
        var margin = Fresh(c.A.Margin, c.Now, Funds, out var a2);
        var age = Age(a1, a2);
        if (!IsFresh(c.A.FundsAvailable, c.Now, Funds) || !IsFresh(c.A.Margin, c.Now, Funds) || margin is null || NonNeg(funds) is not { } f || NonNeg(margin.TotalMargin) is not { } need
            || NonNeg(c.A.ReservedMargin) is not { } reserved || Pos(c.A.Capital) is not { } capital || Pos(c.I.Entry) is not { } e || Pos(c.I.Quantity) is not { } q)
        {
            return Un(code, "funds, margin, reserved margin, capital or the instruction is missing, stale or impossible", "broker", age);
        }

        var room = f - reserved;
        var notional = e * q;
        var ok = !margin.InsufficientBalance && AtMost(need, room) && AtMost(notional, capital);
        return Judge(code, ok, need, room, "margin " + N(need) + ", free " + N(room) + (margin.InsufficientBalance ? ", broker says insufficient balance" : string.Empty) + ", notional " + N(notional) + " of capital " + N(capital), "broker", age);
    }

    private static ProfileFacts? Prof(Ctx c) =>
        c.A.Profile is { } p && double.IsFinite(p.RiskPerTradePct) && p.RiskPerTradePct > 0 && double.IsFinite(p.DailyLossR) && p.DailyLossR > 0 && double.IsFinite(p.DailyLossPct) && p.DailyLossPct > 0
        && double.IsFinite(p.MaxTotalRiskPct) && p.MaxTotalRiskPct > 0 && p.MaxOpenPositions >= 1 && p.MaxNewEntriesPerDay >= 1 && p.MaxPerSector >= 1 ? p : null;

    private static CheckResult Risk(Ctx c)
    {
        const string code = CheckCodes.RiskPerTrade;
        if (Prof(c) is not { } p || Pos(c.A.Capital) is not { } capital || RiskInr(c.I) is not { } risk)
        {
            return Un(code, "profile, capital or the levels are missing or impossible", "intent");
        }

        var limit = Math.Min(capital * p.RiskPerTradePct / 100, c.L.MaxLossPerTradeInr);
        return Judge(code, AtMost(risk, limit), risk, limit, "risk " + N(risk) + ", limit " + N(limit), "intent");
    }

    private static CheckResult Exposure(Ctx c)
    {
        const string code = CheckCodes.ExposureHeadroom;
        var book = Fresh(c.A.Book, c.Now, Funds, out var age);
        if (!IsFresh(c.A.Book, c.Now, Funds) || book is null || Prof(c) is not { } p || Pos(c.A.Capital) is not { } capital || NonNeg(c.A.IntentOpenRiskInr) is not { } intents || NonNeg(book.PositionRiskInr) is not { } positions || RiskInr(c.I) is not { } risk)
        {
            return Un(code, "positions, open intent risk, profile, capital or the levels are missing, stale or impossible", "broker", age);
        }

        var total = intents + positions + risk;
        var limit = capital * p.MaxTotalRiskPct / 100;
        return Judge(code, AtMost(total, limit), total, limit, "open risk with this order " + N(total) + ", limit " + N(limit), "broker", age);
    }

    private static CheckResult OpenPositions(Ctx c)
    {
        const string code = CheckCodes.OpenPositions;
        var book = Fresh(c.A.Book, c.Now, Funds, out var age);
        if (!IsFresh(c.A.Book, c.Now, Funds) || book is null || book.OpenPositions < 0 || c.A.OpenIntentEntries is not { } own || own < 0 || Prof(c) is not { } p)
        {
            return Un(code, "positions, our open entries or the profile are missing, stale or impossible", "broker", age);
        }

        var open = Math.Max(book.OpenPositions, own);
        return Judge(code, open < p.MaxOpenPositions, open, p.MaxOpenPositions, open + " open, limit " + p.MaxOpenPositions + " (reaching it fails)", "broker", age);
    }

    private static CheckResult EntriesToday(Ctx c)
    {
        const string code = CheckCodes.EntriesToday;
        if (c.A.EntriesToday is not { } n || n < 0 || Prof(c) is not { } p)
        {
            return Un(code, "entries today or the profile is missing or impossible", "execution_db");
        }

        return Judge(code, n < p.MaxNewEntriesPerDay, n, p.MaxNewEntriesPerDay, n + " entries sent today, limit " + p.MaxNewEntriesPerDay, "execution_db");
    }

    private static CheckResult DailyLoss(Ctx c)
    {
        const string code = CheckCodes.DailyLoss;
        var pnl = Fresh(c.A.TodayPnl, c.Now, Funds, out var age);
        if (!IsFresh(c.A.TodayPnl, c.Now, Funds) || Any(pnl) is not { } p || Prof(c) is not { } prof || Pos(c.A.Capital) is not { } capital)
        {
            return Un(code, "today's result, the profile or capital is missing, stale or impossible", "broker", age);
        }

        var budget = capital * prof.RiskPerTradePct / 100;
        var limit = Math.Min(prof.DailyLossR * budget, prof.DailyLossPct / 100 * capital);
        var loss = Math.Max(0, -p);
        return Judge(code, Below(loss, limit), loss, limit, "loss today " + N(loss) + ", limit " + N(limit) + " (reaching it fails)", "broker", age);
    }

    private static CheckResult OnePerSymbol(Ctx c)
    {
        const string code = CheckCodes.OnePerSymbol;
        var book = Fresh(c.A.Book, c.Now, Funds, out var age);
        if (!IsFresh(c.A.Book, c.Now, Funds) || book is null || c.A.IntentInSymbol is not { } own)
        {
            return Un(code, "broker book or our intents are missing or older than 60 s", "broker", age);
        }

        var busy = own || book.PositionInSymbol || book.WorkingOrderInSymbol;
        return Judge(code, !busy, null, null, busy ? book.ForeignDetail ?? "an intent, position or working order exists in the instrument" : "nothing else in the instrument", "broker", age);
    }

    private static CheckResult OrderBudget(Ctx c)
    {
        const string code = CheckCodes.OrderBudget;
        if (c.S.OrdersToday is not { } n || n < 0 || c.S.LimiterHasRoom is not { } room)
        {
            return Un(code, "the day's order count or the limiter state is not known", "execution_db");
        }

        return Judge(code, n < c.L.MaxOrdersPerDay && room, n, c.L.MaxOrdersPerDay, n + " orders today, cap " + N(c.L.MaxOrdersPerDay) + (room ? string.Empty : ", rate limiter has no room"), "execution_db");
    }

    private static CheckResult Canary(Ctx c)
    {
        const string code = CheckCodes.CanaryCap;
        if (c.S.CanaryPending is not { } pending)
        {
            return Un(code, "the canary setting cannot be read", "settings");
        }

        if (!pending)
        {
            return Judge(code, true, null, null, "canary orders recorded; cap lifted", "settings");
        }

        return Pos(c.I.Quantity) is not { } q
            ? Un(code, "quantity is missing or impossible", "intent")
            : Judge(code, q <= 1 + Epsilon, q, 1, "canary pending: at most 1 share (quantity " + N(q) + ")", "settings");
    }

    // ---- Soft ----------------------------------------------------------------------------------------------------------------------------------------------

    private static CheckResult Sector(Ctx c)
    {
        const string code = CheckCodes.SectorExposure;
        if (string.IsNullOrWhiteSpace(c.A.Sector) || c.A.SectorPositions is not { } n || n < 0 || Prof(c) is not { } p)
        {
            return Un(code, "sector, its count or the sector limit is not known", "plan");
        }

        var after = n + 1;
        return Judge(code, after <= p.MaxPerSector, after, p.MaxPerSector, after + " in the sector with this order, limit " + p.MaxPerSector, "plan");
    }

    private static CheckResult Holdings(Ctx c)
    {
        const string code = CheckCodes.HoldingsConflict;
        if (!Side(c.I.Side))
        {
            return Un(code, "the side is not BUY or SELL", "intent");
        }

        var sell = c.I.Side == "SELL";
        if (c.A.OpenTradeExists == true)
        {
            return Judge(code, false, null, null, "a trade in the instrument is already open", "plan");
        }

        if (sell && c.A.HeldLongAnyAccount == true)
        {
            return Judge(code, false, null, null, "SELL on an instrument held long", "plan");
        }

        return c.A.OpenTradeExists is null || (sell && c.A.HeldLongAnyAccount is null)
            ? Un(code, "open trades or holdings are not known", "plan")
            : Judge(code, true, null, null, "no conflict with holdings or open trades", "plan");
    }

    private static CheckResult Adv(Ctx c)
    {
        if (Pos(c.I.Quantity) is not { } q || Pos(c.M.AverageDailyVolume) is not { } adv)
        {
            return Un(CheckCodes.AdvShare, "quantity or average daily volume is not known", "master");
        }

        var pct = q / adv * 100;
        return Judge(CheckCodes.AdvShare, AtMost(pct, ExecLimits.MaxAdvPct), pct, ExecLimits.MaxAdvPct, "quantity is " + N(pct) + "% of the average daily volume", "master");
    }

    private static CheckResult Results(Ctx c) =>
        c.M.DaysToResults is not { } d || d < 0
            ? Un(CheckCodes.ResultsWithinDays, "no results date for the instrument (no data source yet)", "events")
            : Judge(CheckCodes.ResultsWithinDays, d > ExecLimits.ResultsWithinDays, d, ExecLimits.ResultsWithinDays, "results in " + d + " days", "events");

    private static CheckResult Ban(Ctx c) => Flag(CheckCodes.FnoBan, c.M.InFnoBan, "in the F&O ban list", "not in the F&O ban list", "no F&O ban list (no data source yet)", "events");

    private static CheckResult SurveillanceCheck(Ctx c) => Flag(CheckCodes.Surveillance, c.M.UnderSurveillance, "under exchange surveillance", "not under surveillance", "surveillance status is not known", "master");

    private static CheckResult DataQualityCheck(Ctx c) => c.M.DataQualityOk switch
    {
        null => Un(CheckCodes.DataQuality, "no validation status for the instrument", "validation"),
        true => Judge(CheckCodes.DataQuality, true, null, null, "latest validation is clean", "validation"),
        false => Judge(CheckCodes.DataQuality, false, null, null, "latest validation of the instrument failed", "validation"),
    };

    private static CheckResult CorrelationCheck(Ctx c)
    {
        if (c.M.MaxCorrelation is not { } r || !double.IsFinite(r) || r is < -1 or > 1)
        {
            return Un(CheckCodes.Correlation, "correlation with the book is not computed yet (P7-06)", "smart_rules");
        }

        return Judge(CheckCodes.Correlation, AtMost(r, ExecLimits.MaxCorrelationAllowed), r, ExecLimits.MaxCorrelationAllowed, "highest correlation " + N(r), "smart_rules");
    }

    private static CheckResult Flag(string code, bool? bad, string failText, string passText, string unavailableText, string source) => bad switch
    {
        null => Un(code, unavailableText, source),
        true => Judge(code, false, null, null, failText, source),
        false => Judge(code, true, null, null, passText, source),
    };
}
