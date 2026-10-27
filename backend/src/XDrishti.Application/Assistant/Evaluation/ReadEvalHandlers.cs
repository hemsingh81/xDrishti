using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>One run with its cases (spec P9-09 Contract). Read only; works while the assistant is off.</summary>
internal sealed class GetEvalHandler(IEvalStore store) : IQueryHandler<GetEvalQuery, Result<EvalDetailView>>
{
    public async ValueTask<Result<EvalDetailView>> HandleAsync(GetEvalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await store.GetAsync(query.Id, cancellationToken);
        if (run is null)
        {
            return Result.Failure<EvalDetailView>(EvalErrors.NotFound(query.Id));
        }

        var cases = await store.CasesAsync(run.Id, cancellationToken);
        var baseline = await store.BaselineOfAsync(run.Id, cancellationToken);
        return Result.Success(new EvalDetailView(EvalMapping.Run(run, EvalMapping.SummaryOf(run), baseline is not null), [.. cases.Select(EvalMapping.Case)], run.PromptVersionsJson, run.ParamsJson));
    }
}

/// <summary>The runs, newest first (spec P9-09 Contract). The cases are not listed: one run is read with its id.</summary>
internal sealed class ListEvalsHandler(IEvalStore store) : IQueryHandler<ListEvalsQuery, Result<IReadOnlyList<EvalRunView>>>
{
    public async ValueTask<Result<IReadOnlyList<EvalRunView>>> HandleAsync(ListEvalsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > ListEvalsQuery.MaxLimit)
        {
            return Result.Failure<IReadOnlyList<EvalRunView>>(EvalErrors.Invalid($"The limit is 1 to {ListEvalsQuery.MaxLimit}."));
        }

        var runs = await store.ListAsync(query.Limit, cancellationToken);
        var baseline = await store.CurrentBaselineAsync(cancellationToken);
        return Result.Success<IReadOnlyList<EvalRunView>>([.. runs.Select(r => EvalMapping.Run(r, EvalMapping.SummaryOf(r), baseline?.RunId == r.Id))]);
    }
}
