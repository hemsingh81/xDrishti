using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The only route from the cell lifecycle to the readiness scorecard (spec CF-11 D6, ADR 0221). For a move from Candidate to Paper (gate R) or from Paper to Active (gate S) it builds the strategy's scorecard, stores a <c>gate</c> snapshot that the changelog row cites, and
/// answers: with the owner's enforce switch OFF (the default) a failed gate is a warning and the move goes on; ON it is refused with the failing rows.
/// <para>Failures read as the stricter side. A scorecard that cannot be built is not a pass (the gate fails with no rows named). An enforce switch that cannot be read counts as ON, so a store that is down never lets a move through unchecked; a gate snapshot that cannot be
/// stored is logged and the move carries no snapshot id (the gate decision itself does not depend on it). The gate never moves a cell, never raises size and places no order; it is called by the owner's lifecycle move and by nothing that chooses what to approve.</para>
/// </summary>
internal sealed class ReadinessGate(ScorecardReader reader, IReadinessSettings settings, IReadinessSnapshotStore store, ReadinessOptions options, TimeProvider clock, ILogger<ReadinessGate> logger) : IReadinessGate
{
    public async ValueTask<ReadinessGateDecision> CheckAsync(ReadinessGateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gate = request.Kind == ReadinessGateKind.ResearchToPaper ? ReadinessGates.R : ReadinessGates.S;
        var enforced = await EnforcedAsync(gate, cancellationToken);
        ScorecardView? card = null;
        string? unreadable = null;
        try
        {
            var strategy = CellId.TryParse(request.Cell)?.Strategy ?? throw new InvalidOperationException("The cell key is not a cell key.");
            var latest = await reader.LatestVersionAsync(strategy, cancellationToken);
            card = await reader.BuildAsync(strategy, latest.IsSuccess ? latest.Value : null, cancellationToken);
        }
#pragma warning disable CA1031 // A scorecard that cannot be built (a store that is down, an unreadable result) is a gate that does not pass, never an exception into the owner's move.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            unreadable = failure.GetType().Name;
            ReadinessLog.GateUnreadable(logger, gate, unreadable);
        }

        var view = card is null ? null : request.Kind == ReadinessGateKind.ResearchToPaper ? card.GateR : card.GateS;
        var passed = view is { Passed: true };
        List<int> failing = view is null ? [] : [.. view.Failing.Select(r => r.Number)];
        var snapshot = card is null ? null : await StoreAsync(request, card, gate, passed, enforced, cancellationToken);
        var message = Message(gate, card, view, unreadable);
        var refused = !passed && enforced;
        ReadinessLog.Gate(logger, gate, passed ? 1 : 0, enforced ? 1 : 0, refused ? 1 : 0, failing.Count);
        return new ReadinessGateDecision(passed, enforced, refused, gate, snapshot, failing, message, Evidence(gate, passed, enforced, snapshot, failing));
    }

    private async ValueTask<bool> EnforcedAsync(string gate, CancellationToken cancellationToken)
    {
        try
        {
            return (await settings.CurrentAsync(cancellationToken)).Enforce;
        }
#pragma warning disable CA1031 // The switch that cannot be read is read as ON: the stricter reading, so that a store that is down never lets a move through unchecked.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            ReadinessLog.SwitchUnreadable(logger, gate, failure.GetType().Name);
            return true;
        }
    }

    private async ValueTask<long?> StoreAsync(ReadinessGateRequest request, ScorecardView card, string gate, bool passed, bool enforced, CancellationToken cancellationToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { gate, request.Actor, request.Cell, passed, enforced, card });
            return await store.InsertAsync(new NewReadinessSnapshot("strategy", card.Strategy, card.Version, IndiaTime.Today(clock.GetUtcNow()), "gate", card.Score, card.Label, body, options.SnapshotHash()), cancellationToken);
        }
#pragma warning disable CA1031 // The gate decision does not depend on its record; a snapshot that cannot be stored is logged and the move carries no snapshot id.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            ReadinessLog.SnapshotNotStored(logger, gate, failure.GetType().Name);
            return null;
        }
    }

    private static string Message(string gate, ScorecardView? card, ReadinessGateView? view, string? unreadable)
    {
        if (card is null || view is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the scorecard could not be built ({unreadable}); a gate that cannot be read is not a pass.");
        }

        if (view.Passed)
        {
            return string.Create(CultureInfo.InvariantCulture, $"gate {gate} passed (score {card.Score:0.#}, {card.LabelText}).");
        }

        var rows = view.Failing.Count == 0
            ? string.Empty
            : "rows not passing: " + string.Join("; ", view.Failing.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Number} {r.Name} ({r.Status})"))) + ". ";
        var label = view.LabelOk ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"the label is {card.LabelText} (score {card.Score:0.#}), not Ready for the next phase. ");
        return (rows + label).TrimEnd();
    }

    private static List<string> Evidence(string gate, bool passed, bool enforced, long? snapshot, List<int> failing)
    {
        var ids = new List<string>(2);
        if (snapshot is { } id)
        {
            ids.Add(string.Create(CultureInfo.InvariantCulture, $"readiness-snapshot:{id}"));
        }

        var rows = failing.Count == 0 ? "none" : string.Join('.', failing);
        ids.Add($"readiness-gate-{gate}:{(passed ? "pass" : "fail")}:{(enforced ? "enforce-on" : "enforce-off")}:rows-{rows}");
        return ids;
    }
}
