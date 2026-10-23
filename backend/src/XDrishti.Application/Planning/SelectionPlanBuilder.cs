using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Planning;

/// <summary>
/// The selection plan builder (spec P7-04, ADR 0088): conditional tickets from the last close (<see cref="ITicketSource"/>), filtered, merged and ranked by P7-01 with the cell statistics. Allocation, the checklist and the smart rules
/// are NOT applied (P7-02 needs accounts and the book, P7-05 an account, P7-06 is not built): every ticket says so in its evidence as unavailable, and no allocation is made. Deterministic by inputs. Registered, but the nightly uses
/// it only when <see cref="IPlanBuilderSettings.UseSelection"/> is true (off by default). Nothing here places an order.
/// </summary>
internal sealed class SelectionPlanBuilder(ITicketSource source, ILogger<SelectionPlanBuilder> logger, SelectionRules? rules = null) : IPlanBuilder
{
    public const string BuilderId = "selection";

    public const string CreatedBy = "system:nightly-selection";

    public const int MaxPrimary = 10;

    public const string Basis = "cell statistics only (shrunk replayed-history estimates); tickets are conditional on tomorrow's bars; portfolio fill, checklist and smart rules not applied";

    /// <summary>The cell statistics were replayed on some version of the strategy; the ticket uses the latest stored version (P7-04).</summary>
    public const string StatsVersionCaveat = "stats may belong to an older strategy version";

    private static readonly string[] NotApplied = ["portfolio_fill", "checklist", "smart_rules", "basket_styles_and_sides", StatsVersionCaveat, "cost", "events", "fno_ban", "surveillance"];

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
        // One ticket per (symbol, cell): a duplicate from the source is ignored, never an exception.
        var drafts = new Dictionary<(string, string), TicketDraft>();
        foreach (var c in found.Candidates)
        {
            drafts.TryAdd((c.Draft.Symbol, c.Cell.Key), c.Draft);
        }

        var seen = new HashSet<(string, string)>();
        var candidates = found.Candidates.Where(c => seen.Add((c.Draft.Symbol, c.Cell.Key))).Select(c => new PlanCandidate(
            c.Draft.Symbol, c.Cell, c.State, c.Stats, request.ForDate, PlannedRewardRisk: c.Draft.RewardRisk, AverageDailyTurnover: c.Draft.AverageDailyTurnover)).ToList();
        var selection = CandidateSelector.Select(candidates, rules ?? new SelectionRules());

        var items = new List<NewPlanItem>();
        foreach (var ranked in selection.Ranked)
        {
            var group = items.Count < MaxPrimary ? TicketGroup.Primary : TicketGroup.Reserve;
            var winner = ranked.Merged.Winner;
            items.Add(Item(items.Count + 1, group, drafts[(winner.Candidate.Symbol, winner.Candidate.Cell.Key)], winner.Candidate, winner.ConservativeExpectancyR, ranked.Caveats, group == TicketGroup.Reserve ? ["below_primary_cut"] : [], ranked.AdvisoryPWin));
        }

        foreach (var held in selection.NotEligible)
        {
            var codes = held.Reasons.Select(r => r.Code + (r.Outcome == FilterOutcome.Unavailable ? ":unavailable" : ":fail")).ToList();
            items.Add(Item(items.Count + 1, TicketGroup.Rejected, drafts[(held.Candidate.Symbol, held.Candidate.Cell.Key)], held.Candidate, held.ConservativeExpectancyR, [], codes.Count == 0 ? [held.Verdict.ToString().ToLowerInvariant()] : codes, null));
        }

        foreach (var dropped in selection.Dropped)
        {
            items.Add(Item(items.Count + 1, TicketGroup.Rejected, drafts[(dropped.Candidate.Symbol, dropped.Candidate.Cell.Key)], dropped.Candidate, null, [], [dropped.Code], null));
        }

        var primary = items.Count(i => i.Group == TicketGroup.Primary);
        var reserve = items.Count(i => i.Group == TicketGroup.Reserve);
        TicketLog.Built(logger, request.ForDate, found.Candidates.Count, found.Rejected, found.Unavailable, primary, reserve, items.Count - primary - reserve);

        var plan = new NewPlan(NightlyRules.PlanKey(request.ForDate, BuilderId), request.ForDate, request.ReviewCutoff, 1, Hash(request, items), Basis, true, CreatedBy, request.Now, items);
        return Result.Success(plan);
    }

    private static NewPlanItem Item(int rank, TicketGroup group, TicketDraft draft, PlanCandidate candidate, double? expectancy, IReadOnlyList<string> caveats, IReadOnlyList<string> reasons, double? advisory)
    {
        var cell = candidate.Cell;
        var stats = candidate.Stats;
        var unavailable = NotApplied.Concat(caveats).Distinct(StringComparer.Ordinal).ToList();
        var evidence = new PlanEvidence(
            cell.Key,
            stats?.N ?? 0,
            Clamp(stats is null ? "no cell statistics stored" : string.Create(CultureInfo.InvariantCulture, $"cell statistics as of {stats.AsOf:yyyy-MM-dd}, window all, replayed history, about 2.1 years, no hold-out, modelled fills"), 300),
            stats is null ? "none" : "shrunk posterior win rate of the cell",
            stats?.WinRatePosterior,
            expectancy,
            unavailable,
            advisory is { } p ? string.Create(CultureInfo.InvariantCulture, $"model P(win) {p:0.###}, advisory only") : null);
        return new NewPlanItem(
            rank, group, draft.Symbol, draft.Side, draft.StrategyId, draft.StrategyVersionHash,
            Clamp(draft.TriggerText, 500), Clamp(draft.StopText, 500), Clamp(draft.TargetText, 500), Clamp(draft.ValidityText, 200), reasons, evidence, []);
    }

    private static string Clamp(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    private static string Hash(PlanBuildRequest request, List<NewPlanItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{BuilderId}|{request.Session:yyyy-MM-dd}|{request.ForDate:yyyy-MM-dd}");
        foreach (var i in items)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n{i.Rank}|{i.Group}|{i.Symbol}|{i.Side}|{i.StrategyVersionHash}|{i.Trigger}|{i.Stop}|{i.Target}|{i.Evidence.CellKey}|{string.Join(',', i.Reasons)}");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
