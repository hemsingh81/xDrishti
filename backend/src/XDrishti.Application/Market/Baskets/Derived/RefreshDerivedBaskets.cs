using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Forms the derived baskets (BK-03) from our own daily bars after the end-of-day run: each enabled basket that is due on the last closed session (or on <paramref name="AsOf"/>) gets one immutable dated version, effective from the next
/// session. <paramref name="Manual"/> (refresh now) only names the actor: a weekly or monthly basket is formed on its due dates only, never on another day. Fails with <c>baskets.derived_disabled</c> before reading anything while <c>Baskets:System:Derived:Enabled</c> is off. It never places an order.
/// </summary>
public sealed record RefreshDerivedBasketsCommand(string Actor, bool Manual, DateOnly? AsOf = null) : ICommand;

internal sealed class RefreshDerivedBasketsHandler(
    IDerivedSettings settings,
    IDerivedVersionStore store,
    IDerivedUniverseReader universe,
    IDerivedRunLock runLock,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<RefreshDerivedBasketsHandler> logger) : ICommandHandler<RefreshDerivedBasketsCommand, DerivedRun>
{
    public async ValueTask<Result<DerivedRun>> HandleAsync(RefreshDerivedBasketsCommand command, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Result.Failure<DerivedRun>(BasketErrors.DerivedDisabled);
        }

        await using var held = await runLock.TryAcquireAsync(cancellationToken);
        if (held is null)
        {
            return Result.Failure<DerivedRun>(BasketErrors.DerivedRunning);
        }

        var now = clock.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var asOf = command.AsOf ?? calendar.LastClosedSession(now);
        var outcomes = new List<DerivedBasketOutcome>();
        var due = new List<DerivedDefinitionRow>();
        foreach (var definition in (await store.ListDefinitionsAsync(cancellationToken)).Where(d => d.Enabled))
        {
            var catalogue = DerivedCatalogue.Get(definition.DefinitionNo);
            if (DerivedSchedule.WhyNotRunnable(catalogue) is { } why)
            {
                outcomes.Add(Skipped(definition, why));
            }
            else if (!DerivedSchedule.IsDue(catalogue.Cadence, calendar, asOf))
            {
                outcomes.Add(Skipped(definition, "not_due"));
            }
            else
            {
                due.Add(definition);
            }
        }

        if (due.Count > 0)
        {
            var bars = await universe.ReadAsync(asOf.AddDays(-DerivedComputation.LookbackDays), asOf, cancellationToken);
            var effective = DerivedSchedule.NextSession(calendar, asOf);
            foreach (var definition in due)
            {
                outcomes.Add(await FormAsync(definition, bars, asOf, effective, command.Actor, now, cancellationToken));
            }
        }

        var applied = outcomes.Count(o => o.Outcome == VersionOutcomes.Applied);
        return Result.Success(new DerivedRun(asOf, outcomes, $"{applied} applied, {outcomes.Count(o => o.Outcome == VersionOutcomes.Unchanged)} unchanged, {outcomes.Count(o => o.Outcome == "skipped")} skipped as of {asOf:yyyy-MM-dd}"));
    }

    private async ValueTask<DerivedBasketOutcome> FormAsync(
        DerivedDefinitionRow definition, IReadOnlyList<UniverseInstrument> bars, DateOnly asOf, DateOnly effective, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var parameters = DerivedComputation.ParamsOf(definition);
        if (parameters.IsFailure)
        {
            return Skipped(definition, "invalid_params");
        }

        if (definition.DefinitionNo == 13 && bars.All(b => b.Bars.Flags is null))
        {
            return Skipped(definition, "flags_unavailable"); // the dated lists of BK-05 are not all recorded yet: a missing flag is not a pass, and the members stay as they were
        }

        if (await store.ExistsAsync(definition.BasketId, asOf, parameters.Value.Hash, cancellationToken))
        {
            return Row(definition, VersionOutcomes.Unchanged, null, 0, 0, 0);
        }

        var previous = await store.PreviousAsync(definition.BasketId, asOf, cancellationToken);
        var scoresKept = settings.ScoresKeptBeyondMembers;
        var built = DerivedComputation.Build(definition, parameters.Value, bars, asOf, effective, null, previous, scoresKept, BasketProvenance.Derived, now, actor);
        if (built.IsFailure)
        {
            return Skipped(definition, built.Error.Code);
        }

        var version = built.Value;
        if (version.UniverseSize == 0)
        {
            return Skipped(definition, "no_universe");
        }

        var written = await store.WriteAsync(version, cancellationToken);
        var outcome = written.Outcome == DerivedWriteResult.Behind ? Skipped(definition, "behind_published_version") : Row(definition, written.Outcome, null, version.Scores.Count(s => s.IsMember), version.UniverseSize, version.ExcludedCount);
        var milliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        DerivedLog.Formed(logger, definition.BasketId, asOf, outcome.Outcome, outcome.Members, outcome.Universe, outcome.Excluded, milliseconds);
        return outcome;
    }

    private static DerivedBasketOutcome Skipped(DerivedDefinitionRow definition, string reason) => Row(definition, "skipped", reason, 0, 0, 0);

    private static DerivedBasketOutcome Row(DerivedDefinitionRow definition, string outcome, string? reason, int members, int universe, int excluded) =>
        new(definition.BasketId, definition.BasketName, outcome, reason, members, universe, excluded);
}
