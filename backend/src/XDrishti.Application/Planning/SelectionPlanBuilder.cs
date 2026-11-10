using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Planning;

/// <summary>
/// The selection plan builder (specs P7-04 and P7-09, ADRs 0088 and 0092): conditional tickets from the last close (<see cref="ITicketSource"/>), then in this order the P7-01 selection (filters, merge, rank), the P7-06 smart rules
/// (market gate, correlation-aware selection, equity throttle), the P7-02 portfolio fill, and for each primary ticket the P7-07 allocation to the paper Trading accounts and the P7-05 checklist. Every input with no data source is passed as
/// unavailable (a gate with no data flags the plan; an unavailable fill limit blocks the pick), never as a pass. Everything stays Proposed: nothing here approves a ticket, arms it or places an order. Deterministic by inputs.
/// Registered, but the nightly uses it only when <see cref="IPlanBuilderSettings.UseSelection"/> is true (off by default).
/// </summary>
internal sealed class SelectionPlanBuilder(
    ITicketSource source,
    IPlanContextSource context,
    ILogger<SelectionPlanBuilder> logger,
    SelectionRules? rules = null,
    SmartRulesOptions? smartOptions = null,
    FillConstraints? fillConstraints = null,
    PlanEventFilter? eventFilter = null,
    IAutoScopeSource? scopeSource = null) : IPlanBuilder
{
    public const string BuilderId = "selection";

    public const string CreatedBy = "system:nightly-selection";

    /// <summary>What the numbers rest on (after <see cref="PlanWording.Proposed"/>).</summary>
    public const string Basis = "Cell statistics only (shrunk replayed-history estimates); tickets are conditional on tomorrow's bars; sizes use the close as entry.";

    /// <summary>The longest basis the plan table stores.</summary>
    public const int MaxBasisLength = 500;

    /// <summary>The cell statistics were replayed on some version of the strategy; the ticket uses the latest stored version (P7-04).</summary>
    public const string StatsVersionCaveat = "stats may belong to an older strategy version";

    private static readonly string[] AlwaysWithEvents = ["basket_styles_and_sides", StatsVersionCaveat, "cost", "events", "fno_ban", "surveillance"];

    // "events" is listed as not applied unless the event-risk filter ran for this plan (P15-06); then its figures are in the basis and the filter outcomes are on the items.
    private static readonly string[] AlwaysWithoutEvents = [.. AlwaysWithEvents.Where(a => a != "events")];

    public string Id => BuilderId;

    public async ValueTask<Result<NewPlan>> BuildAsync(PlanBuildRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = await source.ReadAsync(request.Session, request.ForDate, cancellationToken);
        if (read.IsFailure)
        {
            TicketLog.SourceFailed(logger, request.ForDate, read.Error.Code);
            return Result.Failure<NewPlan>(read.Error);
        }

        var found = read.Value;
        var scope = scopeSource is null ? null : await scopeSource.ResolveAsync(cancellationToken); // AU-16: the owner's scope is a filter on the candidates; nothing after it changes
        // One ticket per (symbol, cell): a duplicate from the source is ignored, never an exception.
        var drafts = new Dictionary<(string, string), TicketDraft>();
        var ids = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<PlanCandidate>();
        foreach (var c in AutoScopeFilter.Apply(found.Candidates, scope))
        {
            if (drafts.TryAdd((c.Draft.Symbol, c.Cell.Key), c.Draft))
            {
                candidates.Add(new PlanCandidate(c.Draft.Symbol, c.Cell, c.State, c.Stats, request.ForDate, PlannedRewardRisk: c.Draft.RewardRisk, AverageDailyTurnover: c.Draft.AverageDailyTurnover));
                ids.TryAdd(c.Draft.Symbol, c.InstrumentId);
            }
        }

        // P15-06: with the switch ON the calendar answers `ExcludedByEvent` (true inside N sessions, false outside, null unavailable); with it OFF nothing changes.
        var events = eventFilter is null ? PlanEventFacts.Inactive : await eventFilter.ReadAsync([.. ids.Values.Where(v => v is not null).Select(v => v!.Value)], request.ForDate, request.Now, cancellationToken);
        if (events.Active)
        {
            candidates = [.. candidates.Select(c => c with { ExcludedByEvent = events.ExcludedFor(ids.GetValueOrDefault(c.Symbol)) })];
            var excluded = candidates.Count(c => c.ExcludedByEvent == true);
            var kept = candidates.Count(c => c.ExcludedByEvent == false);
            EventFilterLog.Excluded(logger, request.ForDate, events.Days, excluded, kept);
        }

        var always = events.Active ? AlwaysWithoutEvents : AlwaysWithEvents;
        var selection = CandidateSelector.Select(candidates, rules ?? new SelectionRules());

        var wanted = selection.Ranked.Select(r => r.Merged.Winner.Candidate.Symbol).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(s => new ContextInstrument(ids.GetValueOrDefault(s), s)).ToList();
        var contextRead = await context.ReadAsync(new PlanContextRequest(request.Session, request.ForDate, wanted), cancellationToken);
        if (contextRead.IsFailure)
        {
            PlanWiringLog.ContextFailed(logger, request.ForDate, contextRead.Error.Code);
            return Result.Failure<NewPlan>(contextRead.Error);
        }

        var ctx = contextRead.Value;
        var smart = SmartRulesEngine.Apply(
            selection.Ranked,
            request.ForDate,
            new SmartInputs(ctx.Gate, ctx.TakenTrades, ctx.Correlations, ctx.Book?.Select(b => new OpenPosition(b.Symbol, b.Side)).ToList()),
            smartOptions ?? new SmartRulesOptions());

        var accountRisk = ctx.Accounts.Where(a => a.Mode == Domain.Accounts.TradingMode.Paper && a.RiskPerTradePct is { } r && double.IsFinite(r) && r > 0).Select(a => a.RiskPerTradePct!.Value).ToList();
        double? ticketRisk = accountRisk.Count == 0 ? null : accountRisk.Max() * smart.RiskMultiplier;
        var fill = PortfolioFiller.Fill(
            smart.Primary.Select(r => Facts(r, ticketRisk, ctx)),
            ctx.Book,
            ctx.Correlations?.Matrix,
            fillConstraints ?? new FillConstraints());

        var flagCodes = smart.Flags.Select(f => f.Code).Distinct(StringComparer.Ordinal).ToList();
        var items = new List<NewPlanItem>();
        var wired = PlanWiring.Wire([.. fill.Primary.Select(d => (d.Candidate, drafts[Key(d.Candidate.Ranked.Merged.Winner.Candidate)]))], ctx, smart.RiskMultiplier, events.Active ? (symbol => events.DaysFor(ids.GetValueOrDefault(symbol))) : null);
        for (var i = 0; i < fill.Primary.Count; i++)
        {
            var winner = fill.Primary[i].Candidate.Ranked.Merged.Winner;
            items.Add(Item(items.Count + 1, TicketGroup.Primary, drafts[Key(winner.Candidate)], winner.Candidate, winner.ConservativeExpectancyR, fill.Primary[i].Candidate.Ranked.Caveats, [], fill.Primary[i].Candidate.Ranked.AdvisoryPWin, Applied(always, flagCodes, filled: true, sized: true), [.. wired[i].Standings, PlanWiring.IndicativeEntry, .. (ctx.Book is { Count: 0 } && !ctx.BookFromLedger ? [PlanWording.OpenBookAssumedEmpty] : (string[])[])], wired[i].Allocations));
        }

        var reserve = smart.Reserve.Select(r => new ReserveEntry(r.Ranked, [r.Code], false))
            .Concat(fill.Reserve.Select(d => new ReserveEntry(d.Candidate.Ranked, Codes(d), true)))
            .OrderBy(x => x.Ranked.Rank).ThenBy(x => x.Ranked.Merged.Winner.Candidate.Symbol, StringComparer.Ordinal).ThenBy(x => x.Ranked.Merged.Winner.Candidate.Cell.Key, StringComparer.Ordinal);
        foreach (var (ranked, reasons, filled) in reserve)
        {
            var winner = ranked.Merged.Winner;
            items.Add(Item(items.Count + 1, TicketGroup.Reserve, drafts[Key(winner.Candidate)], winner.Candidate, winner.ConservativeExpectancyR, ranked.Caveats, reasons, ranked.AdvisoryPWin, Applied(always, flagCodes, filled, sized: false), [], []));
        }

        foreach (var d in fill.Rejected)
        {
            var winner = d.Candidate.Ranked.Merged.Winner;
            items.Add(Item(items.Count + 1, TicketGroup.Rejected, drafts[Key(winner.Candidate)], winner.Candidate, winner.ConservativeExpectancyR, d.Candidate.Ranked.Caveats, Codes(d), d.Candidate.Ranked.AdvisoryPWin, Applied(always, flagCodes, filled: true, sized: false), [], []));
        }

        foreach (var held in selection.NotEligible)
        {
            var codes = held.Reasons.Select(r => r.Code + (r.Outcome == FilterOutcome.Unavailable ? ":unavailable" : ":fail")).ToList();
            items.Add(Item(items.Count + 1, TicketGroup.Rejected, drafts[Key(held.Candidate)], held.Candidate, held.ConservativeExpectancyR, [], codes.Count == 0 ? [held.Verdict.ToString().ToLowerInvariant()] : codes, null, NotRun(always), [], []));
        }

        foreach (var dropped in selection.Dropped)
        {
            items.Add(Item(items.Count + 1, TicketGroup.Rejected, drafts[Key(dropped.Candidate)], dropped.Candidate, null, [], [dropped.Code], null, NotRun(always), [], []));
        }

        var primary = items.Count(i => i.Group == TicketGroup.Primary);
        var reserved = items.Count(i => i.Group == TicketGroup.Reserve);
        TicketLog.Built(logger, request.ForDate, found.Candidates.Count, found.Rejected, found.Unavailable, primary, reserved, items.Count - primary - reserved);
        var allocationRows = items.Where(i => i.Group == TicketGroup.Primary).Sum(i => i.Allocations.Count);
        PlanWiringLog.Wired(logger, request.ForDate, smart.Flags.Count, ctx.Accounts.Count, allocationRows);

        var basis = BasisText(smart, ctx, events.Note);
        var plan = new NewPlan(NightlyRules.PlanKey(request.ForDate, BuilderId), request.ForDate, request.ReviewCutoff, 1, Hash(request, basis, items), basis, true, CreatedBy, request.Now, items)
        {
            BasisFlags = PlanBasisFlags.From(smart, ctx.Book is null ? PlanBasisFlags.OpenBookUnavailable : ctx.Book.Count == 0 && !ctx.BookFromLedger ? PlanBasisFlags.OpenBookAssumedEmpty : PlanBasisFlags.OpenBookKnown, [.. fill.Primary.Select(d => d.Candidate.Ranked)]) with { ScopeHash = scope?.Hash },
        };
        return Result.Success(plan);
    }

    private sealed record ReserveEntry(RankedCandidate Ranked, IReadOnlyList<string> Reasons, bool Filled);

    private static (string, string) Key(PlanCandidate c) => (c.Symbol, c.Cell.Key);

    private static FillCandidate Facts(RankedCandidate ranked, double? risk, PlanContext ctx)
    {
        var symbol = ranked.Merged.Winner.Candidate.Symbol;
        var facts = ctx.Instruments.GetValueOrDefault(symbol.ToUpperInvariant());
        return new FillCandidate(ranked, risk, facts?.Sector, facts?.Baskets, facts?.HeldLong);
    }

    private static List<string> Codes(FillDecision decision)
    {
        var codes = decision.Reasons.Select(c => c.Code + (c.Outcome == FilterOutcome.Unavailable ? ":unavailable" : ":fail")).ToList();
        return codes.Count == 0 ? ["not_taken"] : codes;
    }

    // What was not applied to an item that never reached the smart rules and the fill (rejected by the selection, or lost in the merge).
    private static List<string> NotRun(string[] always) => [.. always, "portfolio_fill", "checklist", SmartRulesNotRun, "allocation"];

    /// <summary>The marker on an item that never reached the smart rules and the fill (held, not eligible or dropped in the merge); the shadow record reads it to set such an item aside as edge evidence.</summary>
    internal const string SmartRulesNotRun = "smart_rules";

    /// <summary>The advisory note of a plan item that has a model figure, <c>model P(win) 0.712, advisory only</c> (three decimals at most). The one place the words are written; the shadow record parses against it (<see cref="TryReadAdvisoryNote"/>).</summary>
    internal static string AdvisoryNote(double pWin) => string.Create(CultureInfo.InvariantCulture, $"{AdvisoryNotePrefix}{pWin:0.###}{AdvisoryNoteSuffix}");

    /// <summary>The probability inside <paramref name="note"/> when the note is exactly what <see cref="AdvisoryNote"/> writes for it, else false: no other text, no more than three decimals.</summary>
    internal static bool TryReadAdvisoryNote(string? note, out double pWin)
    {
        pWin = 0;
        if (note is null || !note.StartsWith(AdvisoryNotePrefix, StringComparison.Ordinal) || !note.EndsWith(AdvisoryNoteSuffix, StringComparison.Ordinal) || note.Length <= AdvisoryNotePrefix.Length + AdvisoryNoteSuffix.Length)
        {
            return false;
        }

        var figure = note.AsSpan(AdvisoryNotePrefix.Length, note.Length - AdvisoryNotePrefix.Length - AdvisoryNoteSuffix.Length);
        return double.TryParse(figure, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out pWin) && string.Equals(AdvisoryNote(pWin), note, StringComparison.Ordinal);
    }

    private const string AdvisoryNotePrefix = "model P(win) ";

    private const string AdvisoryNoteSuffix = ", advisory only";

    // The smart-rule flags that apply to everything the rules and the fill saw, plus what was not run for the item.
    private static List<string> Applied(string[] always, List<string> flagCodes, bool filled, bool sized)
    {
        var list = new List<string>(always);
        list.AddRange(flagCodes);
        if (!filled)
        {
            list.Add("portfolio_fill");
        }

        if (!sized)
        {
            list.Add("checklist");
            list.Add("allocation");
        }

        return list;
    }

    private static NewPlanItem Item(int rank, TicketGroup group, TicketDraft draft, PlanCandidate candidate, double? expectancy, IReadOnlyList<string> caveats, IReadOnlyList<string> reasons, double? advisory, List<string> notApplied, IReadOnlyList<string> standings, IReadOnlyList<NewAllocation> allocations)
    {
        var cell = candidate.Cell;
        var stats = candidate.Stats;
        var (levels, levelCode) = LevelsOf(draft);
        var unavailable = notApplied.Concat(standings).Concat(caveats).Concat(levelCode is null ? [] : [levelCode]).Distinct(StringComparer.Ordinal).ToList();
        var evidence = new PlanEvidence(
            cell.Key,
            stats?.N ?? 0,
            Clamp(stats is null ? "no cell statistics stored" : string.Create(CultureInfo.InvariantCulture, $"cell statistics as of {stats.AsOf:yyyy-MM-dd}, window all, replayed history, about 2.1 years, no hold-out, modelled fills"), 300),
            stats is null ? "none" : "shrunk posterior win rate of the cell",
            stats?.WinRatePosterior,
            expectancy,
            unavailable,
            advisory is { } p ? AdvisoryNote(p) : null,
            stats?.WinRateLower,
            stats?.IndependentN,
            stats?.AsOf);
        return new NewPlanItem(
            rank, group, draft.Symbol, draft.Side, draft.StrategyId, draft.StrategyVersionHash,
            Clamp(draft.TriggerText, 500), Clamp(draft.StopText, 500), Clamp(draft.TargetText, 500), Clamp(draft.ValidityText, 200), reasons, evidence, allocations, levels);
    }

    // The numbers of the draft, or the reason there are none (P7-19): never a guess, the text is unchanged either way.
    internal static (TicketLevels? Levels, string? Code) LevelsOf(TicketDraft draft)
    {
        if (draft.IndicativeEntry is not { } entry)
        {
            return (null, null);
        }

        var entryRef = (decimal)entry;
        if (draft.StopLevel is not { } stop || draft.StopAnchor is not { } anchor)
        {
            return (TicketLevels.Create(draft.Side, entryRef, draft.Session, null, null, null, null).Value, TicketLevels.NotNumericCode);
        }

        var target = draft.TargetLevel is { } t && draft.RewardRisk is not null ? (decimal?)t : null;
        var created = TicketLevels.Create(draft.Side, entryRef, draft.Session, (decimal)stop, anchor, target, target is null ? null : (decimal?)draft.RewardRisk);
        return created.IsSuccess
            ? (created.Value, null)
            : (TicketLevels.Create(draft.Side, entryRef, draft.Session, null, null, null, null).Value, created.Error.Code == TicketLevels.ImplausibleCode ? TicketLevels.ImplausibleCode : TicketLevels.NotNumericCode);
    }

    // The proposal sentence first, then what the numbers rest on, then the banner: the gate flag first (the engine adds it first), the rest as they come, the honest-limit notes last. Cut at the column's length, never silently longer.
    internal static string BasisText(SmartPlan smart, PlanContext ctx, string? eventsNote = null)
    {
        var parts = new List<string> { PlanWording.Proposed, Basis };
        if (eventsNote is not null)
        {
            parts.Add(eventsNote + "."); // the event-filter figures right after the basis: N, the coverage and the unavailable count (P15-06)
        }

        parts.AddRange(ctx.Notes); // the open-book sentence first: it says why the fill took nothing, or on what it rests
        parts.AddRange(smart.Banner);
        if (Math.Abs(smart.RiskMultiplier - 1.0) > 1e-9)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Risk multiplier {smart.RiskMultiplier:0.###} applied to every size (market gate / equity throttle)."));
        }

        if (ctx.MarketNote is { } marketNote)
        {
            parts.Add(marketNote); // last: a cut of the column takes this sentence before any flag line
        }

        return Clamp(string.Join(' ', parts), MaxBasisLength);
    }

    private static string Clamp(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    private static string Hash(PlanBuildRequest request, string basis, List<NewPlanItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{BuilderId}|{request.Session:yyyy-MM-dd}|{request.ForDate:yyyy-MM-dd}|{basis}");
        foreach (var i in items)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n{i.Rank}|{i.Group}|{i.Symbol}|{i.Side}|{i.StrategyVersionHash}|{i.Trigger}|{i.Stop}|{i.Target}|{i.Evidence.CellKey}|{string.Join(',', i.Reasons)}|{string.Join(',', i.Evidence.Unavailable)}|{string.Join(',', i.Allocations.Select(a => a.AccountId.ToString("N", CultureInfo.InvariantCulture) + ":" + a.Quantity.ToString(CultureInfo.InvariantCulture)))}");
            if (i.Levels is { } l)
            {
                sb.Append(CultureInfo.InvariantCulture, $"|L:{l.EntryRef}|{l.Stop}|{l.Anchor?.Code()}|{l.Target}|{l.RewardRisk}|{l.Session:yyyy-MM-dd}");
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
