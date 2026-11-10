using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// An owner act (step-up): recomputes one derived basket for every due date of a closed range from bars cut at each date, as <c>recomputed</c> versions with closed membership windows. Only a range that ends before the first published
/// version of the basket (<c>baskets.backfill_overlap</c> otherwise), never overwriting a version, at most <c>MaxBackfillDays</c> trading days. The adjustments are today's restated series, which the answer says.
/// </summary>
public sealed record BackfillDerivedBasketCommand(Guid BasketId, DateOnly? From, DateOnly? To, string Actor) : ICommand;

internal sealed class BackfillDerivedBasketHandler(
    IDerivedSettings settings,
    IDerivedVersionStore store,
    IDerivedUniverseReader universe,
    IDerivedRunLock runLock,
    ITradingCalendar tradingCalendar,
    TimeProvider clock) : ICommandHandler<BackfillDerivedBasketCommand, DerivedRun>
{
    public const string RestatedNote = "The versions are computed from today's adjusted bars (corporate actions restated retroactively), so they can differ slightly from what was knowable on each date; they are labelled recomputed.";

    public async ValueTask<Result<DerivedRun>> HandleAsync(BackfillDerivedBasketCommand command, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Result.Failure<DerivedRun>(BasketErrors.DerivedDisabled);
        }

        if (command.From is not { } from || command.To is not { } to || to < from)
        {
            return Result.Failure<DerivedRun>(BasketErrors.Invalid("from", "Send from and to as dates, from not after to."));
        }

        var definition = (await store.ListDefinitionsAsync(cancellationToken)).FirstOrDefault(d => d.BasketId == command.BasketId);
        if (definition is null)
        {
            return Result.Failure<DerivedRun>(BasketErrors.NotFound);
        }

        var catalogue = DerivedCatalogue.Get(definition.DefinitionNo);
        if (DerivedSchedule.WhyNotRunnable(catalogue) is { } why)
        {
            return Result.Failure<DerivedRun>(BasketErrors.Invalid("basketId", $"This basket cannot be formed yet ({why})."));
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        if (to > calendar.LastClosedSession(clock.GetUtcNow()))
        {
            return Result.Failure<DerivedRun>(BasketErrors.Invalid("to", "The range must end on a session that has closed."));
        }

        var days = calendar.TradingDaysBetween(from.AddDays(-1), to);
        if (days > settings.MaxBackfillDays)
        {
            return Result.Failure<DerivedRun>(BasketErrors.Invalid("to", $"A backfill covers at most {settings.MaxBackfillDays} trading days; this range has {days}."));
        }

        var parameters = DerivedComputation.ParamsOf(definition);
        if (parameters.IsFailure)
        {
            return Result.Failure<DerivedRun>(parameters.Error);
        }

        var dates = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (DerivedSchedule.IsDue(catalogue.Cadence, calendar, date))
            {
                dates.Add(date);
            }
        }

        if (dates.Count == 0)
        {
            return Result.Failure<DerivedRun>(BasketErrors.Invalid("from", "No date of this range is due for the basket's cadence."));
        }

        // The last window ends where the next due version would take effect; that must be no later than the first published version.
        var windowEnd = DerivedSchedule.NextSession(calendar, DerivedSchedule.NextDue(catalogue.Cadence, calendar, dates[^1]));
        var (firstPublished, _) = await store.PublishedRangeAsync(definition.BasketId, cancellationToken);
        if (firstPublished is { } first && windowEnd > first)
        {
            return Result.Failure<DerivedRun>(BasketErrors.BackfillOverlap);
        }

        await using var held = await runLock.TryAcquireAsync(cancellationToken);
        if (held is null)
        {
            return Result.Failure<DerivedRun>(BasketErrors.DerivedRunning);
        }

        var bars = await universe.ReadAsync(from.AddDays(-DerivedComputation.LookbackDays), to, cancellationToken);
        var now = clock.GetUtcNow();
        PreviousVersion? previous = null;
        var written = 0;
        var unchanged = 0;
        for (var i = 0; i < dates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asOf = dates[i];
            if (await store.ExistsAsync(definition.BasketId, asOf, parameters.Value.Hash, cancellationToken))
            {
                unchanged++;
                continue;
            }

            var validTo = i + 1 < dates.Count ? DerivedSchedule.NextSession(calendar, dates[i + 1]) : windowEnd;
            var built = DerivedComputation.Build(definition, parameters.Value, bars, asOf, DerivedSchedule.NextSession(calendar, asOf), validTo, previous, settings.ScoresKeptBeyondMembers, BasketProvenance.Recomputed, now, command.Actor);
            if (built.IsFailure || built.Value.UniverseSize == 0)
            {
                continue;
            }

            var result = await store.WriteAsync(built.Value, cancellationToken);
            if (result.Outcome == VersionOutcomes.Applied)
            {
                written++;
                previous = DerivedComputation.AsPrevious(built.Value);
            }
        }

        var outcome = new DerivedBasketOutcome(definition.BasketId, definition.BasketName, written > 0 ? VersionOutcomes.Applied : VersionOutcomes.Unchanged, null, 0, 0, 0);
        return Result.Success(new DerivedRun(to, [outcome], $"{written} recomputed versions written, {unchanged} existed already, from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}", RestatedNote));
    }
}
