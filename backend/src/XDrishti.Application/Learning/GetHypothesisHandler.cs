using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One hypothesis with its history (spec P6-10).</summary>
internal sealed class GetHypothesisHandler(IHypothesisLog log) : IQueryHandler<GetHypothesisQuery, Result<HypothesisView>>
{
    public async ValueTask<Result<HypothesisView>> HandleAsync(GetHypothesisQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var history = await log.HistoryAsync(query.Id, cancellationToken);
        if (history.Count == 0)
        {
            return Result.Failure<HypothesisView>(HypothesisErrors.NotFound(query.Id));
        }

        var steps = history.Select(h => new HypothesisStepView(h.Id, h.Row.State, h.Row.Reason, h.Row.Actor, h.Row.CreatedAt)).ToList();
        return Result.Success(HypothesisView.Of(history[^1], history[0].Row.CreatedAt, steps));
    }
}
