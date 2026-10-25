using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Audit;

/// <summary>The merged audit, newest first, keyset-paged (spec P8-22). Reads only; a store that cannot be read is named in the answer and never hidden.</summary>
internal sealed class ListAuditHandler(AuditReader reader, IAuditAccountNames accountNames, TimeProvider clock, ILogger<ListAuditHandler> logger) : IQueryHandler<ListAuditQuery, Result<AuditPageView>>
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public static IReadOnlyList<string> StandingLimits { get; } =
    [
        "Only what is recorded is shown: changes made straight in the database, file changes, reads, and anything older than these records are absent.",
        "Secret-shaped text is masked by pattern; an unusual shape could still appear.",
    ];

    public async ValueTask<Result<AuditPageView>> HandleAsync(ListAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parsed = AuditCriteria.Parse(query.Filters, clock.GetUtcNow());
        var problems = new List<FieldError>();
        if (query.Limit is < 1 or > MaxLimit)
        {
            problems.Add(new FieldError("limit", $"The limit is 1 to {MaxLimit}."));
        }

        AuditPosition? cursor = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (AuditPosition.TryDecode(query.Cursor, out var decoded))
            {
                cursor = decoded;
            }
            else
            {
                problems.Add(new FieldError("cursor", "The cursor is not valid."));
            }
        }

        if (parsed.IsFailure || problems.Count > 0)
        {
            var fields = new List<FieldError>(parsed.IsFailure ? parsed.Error.Fields ?? [] : []);
            fields.AddRange(problems);
            return Result.Failure<AuditPageView>(AuditErrors.Invalid(fields));
        }

        var criteria = parsed.Value;
        var limit = query.Limit ?? DefaultLimit;
        var page = await reader.PageAsync(criteria, cursor, limit, query.OwnerEmail, cancellationToken);

        var ids = page.Items.Select(i => i.View.AccountId).OfType<Guid>().Distinct().ToList();
        var names = ids.Count == 0 ? new Dictionary<Guid, string>() : await SafeNamesAsync(ids, cancellationToken);
        var items = page.Items.Select(i => i.View.AccountId is { } id && names.TryGetValue(id, out var name) ? i.View with { AccountName = name } : i.View).ToList();

        var states = AuditSources.All.Select(s => StateOf(s, criteria, page.Scans)).ToList();
        var limits = new List<string>(StandingLimits);
        if (page.Scans.Any(s => s.Horizon is not null))
        {
            limits.Add($"The search covered the newest {AuditReader.MaxScan:N0} rows of a store; narrow the date range, or load more to continue.");
        }

        var capped = page.Scans.Any(s => s.Horizon is not null);
        var unavailable = states.Count(s => s.State == "unavailable");
        var hasText = criteria.Text is not null;
        AuditReadLog.Read(logger, items.Count, unavailable, criteria.HasFilters, hasText, capped);
        return Result.Success(new AuditPageView(new AuditWindowView(criteria.Window.From, criteria.Window.To), items, page.Next?.Encode(), states, limits));
    }

    private async ValueTask<IReadOnlyDictionary<Guid, string>> SafeNamesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        try
        {
            return await accountNames.NamesAsync(ids, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuditReadLog.SourceFailed(logger, "account_names", ex.GetType().Name);
            return new Dictionary<Guid, string>();
        }
    }

    private static AuditSourceState StateOf(AuditSource source, AuditCriteria criteria, IReadOnlyList<AuditScan> scans)
    {
        var code = AuditSources.Code(source);
        var scan = scans.FirstOrDefault(s => s.Source == source);
        return scan is null
            ? new AuditSourceState(code, "empty", criteria.Sources.Contains(source) ? "not_available" : "not_requested")
            : scan.Failed ? new AuditSourceState(code, "unavailable", "read_failed")
            : scan.Scanned == 0 ? new AuditSourceState(code, "empty", "no_rows")
            : new AuditSourceState(code, "value", null);
    }
}
