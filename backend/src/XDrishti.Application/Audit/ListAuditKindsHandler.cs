using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Audit;

/// <summary>The kinds each store can hold, from the kind tables, for the filter lists (spec P8-22). No I/O.</summary>
internal sealed class ListAuditKindsHandler : IQueryHandler<ListAuditKindsQuery, AuditKindsView>
{
    public ValueTask<AuditKindsView> HandleAsync(ListAuditKindsQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AuditKindsView(
            [.. AuditSources.All.SelectMany(s => AuditKinds.KindsOf(s).Select(k => new AuditKindView(AuditSources.Code(s), k.Kind, AuditKinds.Label(s, k.Kind), k.Category, AuditKinds.OutcomeOf(s, k.Kind))))],
            AuditKinds.Categories,
            AuditKinds.Outcomes,
            AuditActors.Filterable));
}
