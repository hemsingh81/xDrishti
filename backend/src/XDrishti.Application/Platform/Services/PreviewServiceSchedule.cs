using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The dry run of a save (ADR 0018 §6): the same checks over the whole set, with the findings (errors and warnings) and the next three starts after now, and nothing stored or audited. The revision is not
/// looked at. <c>services.not_found</c> for an unknown id, <c>services.read_only</c> for the account sync.
/// </summary>
public sealed record PreviewServiceScheduleQuery(string ServiceId, ServiceScheduleInput Input) : IQuery<Result<SchedulePreview>>;

internal sealed class PreviewServiceScheduleHandler(ServiceRegistry registry, ServiceSettingsReader settings, ITradingCalendar calendar, TimeProvider clock)
    : IQueryHandler<PreviewServiceScheduleQuery, Result<SchedulePreview>>
{
    /// <summary>How many coming starts the preview lists.</summary>
    public const int NextCount = 3;

    public async ValueTask<Result<SchedulePreview>> HandleAsync(PreviewServiceScheduleQuery query, CancellationToken cancellationToken)
    {
        if (query.ServiceId == ServiceIds.AccountSync)
        {
            return Result.Failure<SchedulePreview>(ServiceErrors.ReadOnly);
        }

        if (registry.Find(query.ServiceId) is null)
        {
            return Result.Failure<SchedulePreview>(ServiceErrors.NotFound);
        }

        var all = await settings.ReadAllAsync(cancellationToken);
        var service = all.First(s => s.Id == query.ServiceId);
        var issues = ServiceInputChecks.Evaluate(all, service, query.Input, out var parsed);
        var views = issues.Select(ServiceIssueView.From).ToList();
        if (parsed is null)
        {
            return Result.Success(new SchedulePreview(views, [], string.Empty));
        }

        var tradingCalendar = await calendar.GetAsync(cancellationToken);
        var next = issues.Any(i => i.IsError)
            ? []
            : ScheduleCalculator.NextOccurrences(parsed.Schedule, clock.GetUtcNow(), NextCount, tradingCalendar, service.NotBeforeSessionEnd);
        return Result.Success(new SchedulePreview(views, next, ScheduleWords.Describe(parsed.Schedule, registry.NameOf, service.NotBeforeSessionEnd)));
    }
}
