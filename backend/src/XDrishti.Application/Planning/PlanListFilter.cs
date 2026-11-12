using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Lists;

namespace XDrishti.Application.Planning;

/// <summary>What the dated lists know for one plan build (DATA-01, ADR 0274). Inactive (switch OFF): nothing changes. Active: a date with no stored row answers null (unavailable), never false.</summary>
internal sealed record PlanListFacts(bool Active, ListFlags Flags, bool ReadFailed)
{
    public static PlanListFacts Inactive { get; } = new(false, ListFlags.None, false);

    public bool? BanFor(Guid? instrumentId) => Active && instrumentId is { } id ? Flags.OnBan(id) : null;

    public bool? SurveillanceFor(Guid? instrumentId) => Active && instrumentId is { } id ? Flags.UnderSurveillance(id) : null;

    /// <summary>Both lists covered on their dates.</summary>
    public bool Covered => Active && Flags.BanCovered && Flags.SurveillanceCovered;
}

/// <summary>Reads the switch and, when ON, one as-of answer for the instruments of a plan: the ban list for the plan's date, the surveillance list for the session it is built at. A failing read answers unavailable for everyone.</summary>
internal sealed class PlanListFilter(IListFilterSettings settings, IListFlagsAsOf lists, ILogger<PlanListFilter> logger)
{
    public async ValueTask<PlanListFacts> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly forDate, DateOnly session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        if (!settings.Enabled)
        {
            return PlanListFacts.Inactive;
        }

        try
        {
            return new PlanListFacts(true, await lists.ForAsync([.. instrumentIds.Distinct()], forDate, session, cancellationToken), false);
        }
#pragma warning disable CA1031 // A failing read makes both lists unavailable (the safe path); cancellation is rethrown. Only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            ListFilterLog.ReadFailed(logger, forDate, failure.GetType().Name);
            return new PlanListFacts(true, ListFlags.None, true);
        }
    }
}

internal static partial class ListFilterLog
{
    [LoggerMessage(EventId = 3140, Level = LogLevel.Warning, Message = "plan.list_filters read failed for {ForDate}: {FailureType}; ban and surveillance are unavailable")]
    public static partial void ReadFailed(ILogger logger, DateOnly forDate, string failureType);

    [LoggerMessage(EventId = 3141, Level = LogLevel.Information, Message = "plan.list_filters for {ForDate}: ban covered {BanCovered}, surveillance covered {SurveillanceCovered}, {Banned} banned, {Surveilled} surveilled")]
    public static partial void Applied(ILogger logger, DateOnly forDate, bool banCovered, bool surveillanceCovered, int banned, int surveilled);
}
