using System.Globalization;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Proposals;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>Whether the evaluation is of a new proposal (all limits apply) or of a stored one about to be accepted (it is itself pending and was not rejected, so those two rows do not apply).</summary>
internal enum EvaluationMode
{
    Generate,
    Recheck,
}

/// <summary>A proposal that is pending now, as the checks see it.</summary>
internal sealed record PendingItem(string Kind, string Key, string? CellKey, string? KnobName);

/// <summary>What the engine made of one proposal: its subject, its evidence and every check row, plus the dedupe key.</summary>
internal sealed record ProposalEvaluation(string Subject, ProposalEvidence Evidence, IReadOnlyList<ProposalCheck> Checks, string Key)
{
    public bool Passed => ProposalChecks.AllOk(Checks);
}

/// <summary>
/// The engine side of a proposal (spec P9-08 AC 3 to 8, D2): re-reads every reference through the read side (never from the pack text), builds the evidence, and runs the rule checks. The same code decides a new proposal, a stale accept and an auto accept, so they cannot disagree.
/// Nothing the model wrote enters the evidence or the checks except the payload values it asks for, and those are checked against stored data. Read only.
/// </summary>
internal sealed class ProposalEvaluator(IProposalFacts facts, IProposalReader reader, ProposalOptions options, TimeProvider time)
{
    private const int MaxName = 40;

    public async ValueTask<ProposalEvaluation> EvaluateAsync(string kind, ProposalPayload payload, IReadOnlyList<string> refs, EvaluationMode mode, IReadOnlyList<PendingItem> pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(refs);
        ArgumentNullException.ThrowIfNull(pending);
        return payload switch
        {
            HypothesisPayload h when kind == ProposalKind.Hypothesis => await HypothesisAsync(h, refs, mode, pending, cancellationToken),
            BasketPayload b when ProposalKind.IsBasket(kind) => await BasketAsync(kind, b, refs, mode, pending, cancellationToken),
            _ => throw new ArgumentException("The payload does not fit the kind.", nameof(payload)),
        };
    }

    /// <summary>The references the engine needs for a payload; used to re-read a stored proposal, whose references are not kept (the payload names them all).</summary>
    public static IReadOnlyList<string> RefsOf(ProposalPayload payload) => payload switch
    {
        HypothesisPayload h => [$"cell:{h.CellKey}", $"run:{h.BaselineRunKey}"],
        BasketPayload b => [$"instrument:{b.Symbol}"],
        _ => [],
    };

    private async ValueTask<ProposalEvaluation> HypothesisAsync(HypothesisPayload p, IReadOnlyList<string> refs, EvaluationMode mode, IReadOnlyList<PendingItem> pending, CancellationToken cancellationToken)
    {
        var figures = new List<EvidenceFigure>();
        var population = new SortedSet<string>(StringComparer.Ordinal);
        var resolved = refs.Contains($"cell:{p.CellKey}", StringComparer.Ordinal) && refs.Contains($"run:{p.BaselineRunKey}", StringComparer.Ordinal);
        CellFact? cell = null;
        foreach (var reference in refs)
        {
            if (reference.StartsWith("cell:", StringComparison.Ordinal))
            {
                var found = await facts.CellAsync(reference[5..], cancellationToken);
                resolved &= found is not null;
                if (found is not null && string.Equals(found.Key, p.CellKey, StringComparison.Ordinal))
                {
                    cell = found;
                }
            }
            else if (reference.StartsWith("run:", StringComparison.Ordinal))
            {
                var run = await facts.RunAsync(reference[4..], cancellationToken);
                resolved &= run is not null;
                if (run is not null)
                {
                    population.Add(run.RunKey);
                }
            }
            else
            {
                resolved = false;
            }
        }

        var baseline = await facts.RunAsync(p.BaselineRunKey, cancellationToken);
        var knobKind = Enum.TryParse<ExperimentKnobKind>(p.KnobKind, false, out var parsedKind) && Enum.IsDefined(parsedKind) ? parsedKind : (ExperimentKnobKind?)null;
        var problems = new List<string>();
        if (baseline is not null && knobKind == ExperimentKnobKind.Parameter)
        {
            problems.AddRange(OneVariableRule.BaselineProblems(new ExperimentKnob(p.KnobName, ExperimentKnobKind.Parameter, p.BaselineValue, p.CandidateValue), baseline.Definition));
            if (cell is not null && baseline.StrategyId != cell.Strategy)
            {
                problems.Add("the baseline run is of another strategy than the cell");
            }
        }

        if (cell is not null)
        {
            var enough = cell.Enough;
            figures.Add(new EvidenceFigure("Cell trades", cell.N.ToString(CultureInfo.InvariantCulture), "value", cell.N));
            figures.Add(cell.ExpectancyR is { } e && enough ? new EvidenceFigure("Cell expectancy (R)", ProposalFacts.Number(e), "value", cell.N) : new EvidenceFigure("Cell expectancy (R)", string.Empty, "insufficient", cell.N));
            figures.Add(enough ? new EvidenceFigure("Cell win-rate lower bound", ProposalFacts.Number(cell.WinRateLower), "value", cell.N) : new EvidenceFigure("Cell win-rate lower bound", string.Empty, "insufficient", cell.N));
        }

        var open = await facts.OpenHypothesesAsync(cancellationToken);
        var key = ProposalKey.Hypothesis(p);
        var duplicate = open.Any(o => Same(o.CellKey, p.CellKey) && Same(o.KnobName, p.KnobName)) || pending.Any(x => x.Kind == ProposalKind.Hypothesis && (x.Key == key || (Same(x.CellKey, p.CellKey) && Same(x.KnobName, p.KnobName))));
        var generate = mode == EvaluationMode.Generate;
        var blocked = generate && await reader.RejectedSinceAsync(key, time.GetUtcNow().AddDays(-options.RepeatBlockDays), cancellationToken);
        var f = new HypothesisProposalFacts(
            resolved && cell is not null && baseline is not null, p.KnobKind, baseline is not null, problems, p.BaselineValue, p.CandidateValue, options.MaxKnobMovePct, cell?.N, ProposalChecks.MinHypothesisTrades, open.Count, HypothesisLifecycle.DefaultMaxOpen,
            generate ? pending.Count(x => x.Kind == ProposalKind.Hypothesis) : 0, options.MaxPendingHypotheses, duplicate, blocked);
        var checks = ProposalChecks.Hypothesis(f);
        var subject = Clip($"{p.CellKey}: {p.KnobName} {KnobBounds.Normal(p.BaselineValue) ?? p.BaselineValue} to {KnobBounds.Normal(p.CandidateValue) ?? p.CandidateValue}", ProposalAnswerReader.MaxSubject);
        var evidence = new ProposalEvidence(cell?.Basis ?? "unavailable", [.. population], cell is null ? null : $"as of {cell.AsOf:yyyy-MM-dd}", cell?.N ?? 0, figures, [Learning.CellStatsView.LimitsText, ProposalWording.TrialNote]);
        return new ProposalEvaluation(subject, evidence, checks, key);
    }

    private async ValueTask<ProposalEvaluation> BasketAsync(string kind, BasketPayload p, IReadOnlyList<string> refs, EvaluationMode mode, IReadOnlyList<PendingItem> pending, CancellationToken cancellationToken)
    {
        var symbol = p.Symbol.ToUpperInvariant();
        var instrument = await facts.InstrumentAsync(symbol, cancellationToken);
        var resolved = instrument is not null && refs.Any(r => r.StartsWith("instrument:", StringComparison.Ordinal)) && refs.All(r => r.StartsWith("instrument:", StringComparison.Ordinal)) && refs.Contains($"instrument:{symbol}", StringComparer.Ordinal);
        var basket = await facts.BasketAsync(p.BasketId, cancellationToken);
        var trades = await facts.InstrumentTradesAsync(symbol, cancellationToken);
        var key = ProposalKey.Basket(kind, p);
        var generate = mode == EvaluationMode.Generate;
        var blocked = generate && await reader.RejectedSinceAsync(key, time.GetUtcNow().AddDays(-options.RepeatBlockDays), cancellationToken);
        var member = basket?.MemberSymbols.Contains(symbol, StringComparer.Ordinal) ?? false;
        var f = new BasketProposalFacts(
            resolved, basket is not null, basket is not null && !basket.System, basket?.Archived ?? false, instrument is not null, instrument?.Tracked ?? false, member, trades.State, trades.State == "value" ? trades.N : null, ProposalChecks.MinInstrumentTrades,
            pending.Any(x => x.Key == key), blocked);
        var checks = ProposalChecks.Basket(kind, f);
        var name = UntrustedText.Clean(basket?.Name ?? string.Empty).Text.Trim();
        name = name.Length > MaxName ? name[..MaxName] : name;
        var subject = Clip(kind == ProposalKind.BasketAdd ? $"Add {symbol} to basket {name}" : $"Remove {symbol} from basket {name}", ProposalAnswerReader.MaxSubject);
        var figures = new List<EvidenceFigure>();
        if (trades.State != "unavailable")
        {
            figures.Add(new EvidenceFigure("Instrument trades", trades.N.ToString(CultureInfo.InvariantCulture), "value", trades.N));
            figures.Add(trades.State == "value" && trades.ExpectancyLowerR is { } lower
                ? new EvidenceFigure("Instrument expectancy lower bound (R)", ProposalFacts.Number(lower), "value", trades.N)
                : new EvidenceFigure("Instrument expectancy lower bound (R)", string.Empty, trades.State, trades.N));
        }
        else
        {
            figures.Add(new EvidenceFigure("Instrument trades", string.Empty, "unavailable", null));
        }

        var range = trades.FirstDate is { } first && trades.LastDate is { } last ? $"{first:yyyy-MM-dd} to {last:yyyy-MM-dd}" : null;
        var evidence = new ProposalEvidence(trades.Basis.Length == 0 ? "unavailable" : trades.Basis, trades.RunKeys, range, trades.N, figures, [.. trades.Limits.Take(3)]);
        return new ProposalEvaluation(subject, evidence, checks, key);
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
