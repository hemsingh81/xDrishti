using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.OpeningBars;

/// <summary>
/// Forms the frozen intraday baskets of a trading day (BK-03b: 4 at 09:20, 12 at 09:30 IST) from the live opening-minute bars and the stored history: each enabled basket whose formation time plus the grace has come, and which has no version for the day yet,
/// gets exactly one frozen version - formed, or <c>not_formed</c> with the reason (<c>late</c>, <c>low_disk</c>, <c>no_live_bars</c>, <c>feed_gap</c>); a later call that day writes nothing. Only bars whose minute starts before the formation time are read, and an instrument
/// whose window has a missing minute is left out by name (<c>opening_minute_missing</c>), never filled forward. Fails with <c>baskets.derived_disabled</c> while <c>Baskets:System:Derived:Enabled</c> is off. It never places an order and never calls the broker.
/// </summary>
public sealed record FreezeOpeningBasketsCommand(DateOnly Day) : ICommand;

/// <summary>What a freeze call did: how many enabled baskets are still waiting for their formation time and what happened to the others.</summary>
public sealed record OpeningFreezeRun(int Waiting, IReadOnlyList<DerivedBasketOutcome> Baskets);

internal sealed class FreezeOpeningBasketsHandler(
    IDerivedSettings derived,
    OpeningBarsSettings settings,
    IDerivedVersionStore store,
    IDerivedUniverseReader universe,
    IOpeningBarsQueries bars,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<FreezeOpeningBasketsHandler> logger) : ICommandHandler<FreezeOpeningBasketsCommand, OpeningFreezeRun>
{
    public const string Actor = "system:opening-bars";

    /// <summary>Added to the grace so that the recorder's own write of the last minute's bar (every second) has gone in before the freeze reads.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new();

    public async ValueTask<Result<OpeningFreezeRun>> HandleAsync(FreezeOpeningBasketsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!derived.Enabled)
        {
            return Result.Failure<OpeningFreezeRun>(BasketErrors.DerivedDisabled);
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var outcomes = new List<DerivedBasketOutcome>();
        if (!calendar.IsTradingDay(command.Day))
        {
            return Result.Success(new OpeningFreezeRun(0, outcomes)); // nothing is due on a holiday or a weekend
        }

        var now = clock.GetUtcNow();
        var asOf = calendar.LastClosedSession(OpeningWindows.At(command.Day, new TimeOnly(9, 0)));
        var waiting = 0;
        foreach (var definition in (await store.ListDefinitionsAsync(cancellationToken)).Where(d => d.Enabled && DerivedCatalogue.Get(d.DefinitionNo).Cadence == DerivedCadence.FrozenIntraday))
        {
            var catalogue = DerivedCatalogue.Get(definition.DefinitionNo);
            var parameters = DerivedComputation.ParamsOf(definition);
            if (parameters.IsFailure)
            {
                outcomes.Add(Row(definition, "skipped", "invalid_params", 0, 0, 0));
                continue;
            }

            var formation = OpeningWindows.At(command.Day, OpeningBarsSettings.Open.AddMinutes(DerivedSchedule.OpeningMinutes(catalogue, parameters.Value)!.Value));
            if (now < formation + settings.Grace + Settle)
            {
                waiting++;
                continue;
            }

            if (await store.FrozenExistsAsync(definition.BasketId, asOf, cancellationToken))
            {
                continue; // formed or not formed already: the day is settled
            }

            var started = clock.GetTimestamp();
            var reason = now > formation + settings.FormBy ? OpeningWindows.Late : OpeningWindows.WholeReason(await bars.ReadEventsAsync(command.Day, cancellationToken), command.Day, formation);
            var write = reason is not null
                ? NotFormed(definition, parameters.Value, asOf, command.Day, formation, reason, now)
                : await FormAsync(definition, parameters.Value, asOf, command.Day, formation, now, cancellationToken);
            var written = await store.WriteAsync(write, cancellationToken);
            var members = write.Scores.Count(s => s.IsMember);
            outcomes.Add(Row(definition, written.Outcome == VersionOutcomes.Applied ? write.Outcome : written.Outcome, reason, members, write.UniverseSize, write.ExcludedCount));
            var milliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            DerivedLog.Formed(logger, definition.BasketId, asOf, write.Outcome, members, write.UniverseSize, write.ExcludedCount, milliseconds);
        }

        return Result.Success(new OpeningFreezeRun(waiting, outcomes));
    }

    private async ValueTask<DerivedVersionWrite> FormAsync(DerivedDefinitionRow definition, DerivedParams parameters, DateOnly asOf, DateOnly day, DateTimeOffset formation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var all = await universe.ReadAsync(asOf.AddDays(-DerivedComputation.LookbackDays), asOf, cancellationToken);
        var (kept, cut) = OpeningWindows.Subscribed(DerivedComputation.UniverseOf(all, asOf), settings.MaxInstruments);
        var today = OpeningWindows.Before(await bars.ReadDayAsync(day, cancellationToken), formation).ToLookup(b => b.InstrumentId);
        var history = (await bars.ReadHistoryAsync([.. kept.Select(k => k.InstrumentId)], asOf.AddDays(-45), asOf, cancellationToken)).ToLookup(b => b.InstrumentId);
        var extra = cut.Select(c => new VersionExclusion(c.InstrumentId, c.Bars.Symbol, ExclusionReason.NotSubscribed)).ToList();
        var engine = new List<UniverseInstrument>();
        foreach (var instrument in kept)
        {
            if (!OpeningWindows.IsComplete(today[instrument.InstrumentId], day, formation))
            {
                extra.Add(new VersionExclusion(instrument.InstrumentId, instrument.Bars.Symbol, ExclusionReason.OpeningMinuteMissing));
                continue;
            }

            var intraday = history[instrument.InstrumentId].Concat(today[instrument.InstrumentId]).Select(Convert).ToArray();
            engine.Add(instrument with { Bars = instrument.Bars with { Intraday = intraday } });
        }

        var previous = await store.PreviousAsync(definition.BasketId, asOf, cancellationToken);
        var built = DerivedComputation.Build(definition, parameters, engine, asOf, day, null, previous, derived.ScoresKeptBeyondMembers, BasketProvenance.Derived, now, Actor, formation.ToUniversalTime(), extra);
        return built.IsSuccess ? built.Value : NotFormed(definition, parameters, asOf, day, formation, built.Error.Code, now);
    }

    private static DerivedVersionWrite NotFormed(DerivedDefinitionRow definition, DerivedParams parameters, DateOnly asOf, DateOnly day, DateTimeOffset formation, string reason, DateTimeOffset now) => new(
        Guid.CreateVersion7(now), definition.BasketId, definition.BasketName, asOf, day, null, formation.ToUniversalTime(), DerivedComputation.ParamsJson(parameters), parameters.Hash, 0, 0, 0,
        JsonSerializer.Serialize(new[] { new VersionExclusion(Guid.Empty, string.Empty, reason) }, Json), BasketProvenance.Derived, VersionOutcomes.NotFormed, [], now, Actor);

    private static IntradayBar Convert(MinuteBar bar) => new(bar.MinuteStart, (double)bar.Open, (double)bar.High, (double)bar.Low, (double)bar.Close, bar.Volume);

    private static DerivedBasketOutcome Row(DerivedDefinitionRow definition, string outcome, string? reason, int members, int universe, int excluded) =>
        new(definition.BasketId, definition.BasketName, outcome, reason, members, universe, excluded);
}
