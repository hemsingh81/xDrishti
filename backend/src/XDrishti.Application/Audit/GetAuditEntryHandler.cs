using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Audit;

/// <summary>One audit row with its detail fields (spec P8-22). An unknown prefix, a malformed id or a missing row is the same not-found, never a failure.</summary>
internal sealed class GetAuditEntryHandler(IEnumerable<IAuditSource> sources, IAuditAccountNames accountNames, ILogger<GetAuditEntryHandler> logger) : IQueryHandler<GetAuditEntryQuery, Result<AuditEntryDetailView>>
{
    public async ValueTask<Result<AuditEntryDetailView>> HandleAsync(GetAuditEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parts = query.Id.Split(':', 2);
        if (parts.Length != 2 || !AuditSources.TryParse(parts[0], out var source) || !AuditSources.IsValidId(source, parts[1]))
        {
            return Result.Failure<AuditEntryDetailView>(AuditErrors.NotFound());
        }

        var store = sources.FirstOrDefault(s => s.Source == source);
        AuditRaw? raw = null;
        if (store is not null)
        {
            try
            {
                raw = await store.GetAsync(parts[1], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AuditReadLog.SourceFailed(logger, parts[0], ex.GetType().Name);
                return Result.Failure<AuditEntryDetailView>(AuditErrors.NotFound());
            }
        }

        if (raw is null)
        {
            return Result.Failure<AuditEntryDetailView>(AuditErrors.NotFound());
        }

        var view = AuditEntryBuilder.Build(raw, query.OwnerEmail);
        if (raw.AccountId is { } id)
        {
            var names = await accountNames.NamesAsync([id], cancellationToken);
            view = names.TryGetValue(id, out var name) ? view with { AccountName = name } : view;
        }

        return Result.Success(new AuditEntryDetailView(view, AuditEntryBuilder.Fields(raw)));
    }
}
