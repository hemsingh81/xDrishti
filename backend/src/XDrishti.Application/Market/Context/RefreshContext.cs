using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>Writes the context days that have become final since the last run (P3-01d): derived from the stored daily rows of NIFTY 50 and INDIA VIX, insert-only, idempotent. A missing prerequisite writes nothing and is named in the answer.</summary>
public sealed record RefreshContextCommand : ICommand;

/// <summary>What a refresh did: days written, days still waiting for their VIX leg, days skipped, the newest stored day, and the missing prerequisite with its fix (null when none).</summary>
public sealed record ContextRefreshed(int Written, int Pending, int Skipped, DateOnly? LastDate, string? Missing, string? Fix);

internal sealed class RefreshContextHandler(
    IContextSettings settings,
    IContextBarSource bars,
    IContextDayStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<RefreshContextHandler> logger) : ICommandHandler<RefreshContextCommand, ContextRefreshed>
{
    public async ValueTask<Result<ContextRefreshed>> HandleAsync(RefreshContextCommand command, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Result.Success(new ContextRefreshed(0, 0, 0, null, ContextPrerequisites.Disabled, "Data:Context:Enabled is false."));
        }

        var legs = await bars.ReadAsync(settings.IndexSymbols, settings.VixSymbols, cancellationToken);
        if (ContextPrerequisites.Missing(legs) is { } missing)
        {
            ContextLog.PrerequisiteMissing(logger, missing.Code);
            return Result.Success(new ContextRefreshed(0, 0, 0, null, missing.Code, missing.Fix));
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var existing = await store.ListAsync(cancellationToken);
        var derived = ContextDerivation.Derive(
            legs.IndexLegs, legs.VixLegs, calendar, lastClosed, existing.Select(r => r.Date).ToHashSet(), new ContextDerivationOptions(settings.VixGraceSessions, settings.MinDayFraction));
        var written = derived.Records.Count == 0 ? 0 : await store.InsertAsync(legs.Index!.Id, derived.Records, cancellationToken);
        DateOnly? last = existing.Count > 0 ? existing[^1].Date : null;
        if (derived.Records.Count > 0 && (last is null || derived.Records[^1].Date > last))
        {
            last = derived.Records[^1].Date;
        }

        var skipped = derived.Skipped.Count(s => s.Reason != ContextSkipReason.VixThin);
        var lastText = last?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";
        ContextLog.Refreshed(logger, written, derived.Pending.Count, skipped, lastText);
        return Result.Success(new ContextRefreshed(written, derived.Pending.Count, skipped, last, null, null));
    }
}
