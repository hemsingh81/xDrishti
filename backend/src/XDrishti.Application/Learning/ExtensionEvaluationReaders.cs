using System.Text.Json;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>Lists the stored evaluations of one family, newest first, at most 100 (P15-10). Read only.</summary>
internal sealed class ListExtensionEvaluationsHandler(IExtensionEvaluationStore store) : IQueryHandler<ListExtensionEvaluationsQuery, Result<ExtensionEvaluationList>>
{
    public const int Limit = 100;

    public async ValueTask<Result<ExtensionEvaluationList>> HandleAsync(ListExtensionEvaluationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!ExtensionFamilies.IsKnown(query.Family) && !PairFamilies.IsKnown(query.Family))
        {
            return Result.Failure<ExtensionEvaluationList>(ExtensionEvaluationErrors.InvalidFamily(query.Family));
        }

        var records = await store.ListAsync(query.Family!, Limit, cancellationToken);
        return Result.Success(new ExtensionEvaluationList(query.Family!, [.. records.Select(ExtensionEvaluationMapper.ToView)]));
    }
}

/// <summary>One stored evaluation with its evidence block (P15-10). Read only.</summary>
internal sealed class GetExtensionEvaluationHandler(IExtensionEvaluationStore store) : IQueryHandler<GetExtensionEvaluationQuery, Result<ExtensionEvaluationView>>
{
    public async ValueTask<Result<ExtensionEvaluationView>> HandleAsync(GetExtensionEvaluationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var record = query.Id.Length == 64 ? await store.GetAsync(query.Id, cancellationToken) : null;
        return record is null ? Result.Failure<ExtensionEvaluationView>(ExtensionEvaluationErrors.Missing(query.Id)) : Result.Success(ExtensionEvaluationMapper.ToView(record));
    }
}

internal static class ExtensionEvaluationMapper
{
    public static ExtensionEvaluationView ToView(ExtensionEvaluationRecord r) => new(
        r.EvaluationId, r.Family, r.Protocol, r.ExtensionVersion, r.VersionKey, r.BaseVersion, r.Verdict, r.Trials, r.Factor, r.FoldUnit, r.FoldsTotal, r.FoldsEvaluable, r.Rows, r.Flips, r.Missing, r.Limits,
        Parse(r.EstimatesJson), Parse(r.EvidenceJson), Parse(r.InputsJson), r.HoldOutRead);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
