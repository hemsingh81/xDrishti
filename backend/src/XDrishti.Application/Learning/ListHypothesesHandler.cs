using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Lists the newest view of each hypothesis (spec P6-10). An empty list is an answer.</summary>
internal sealed class ListHypothesesHandler(IHypothesisLog log) : IQueryHandler<ListHypothesesQuery, Result<IReadOnlyList<HypothesisView>>>
{
    public async ValueTask<Result<IReadOnlyList<HypothesisView>>> HandleAsync(ListHypothesesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new List<FieldError>();
        if (query.Limit is < 1 or > 200)
        {
            problems.Add(new FieldError("limit", "The limit is 1 to 200."));
        }

        if (query.State is not null && HypothesisStateText.TryParse(query.State) is null)
        {
            problems.Add(new FieldError("state", "The state is proposed, approved, running, judged, accepted, rejected or expired."));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<IReadOnlyList<HypothesisView>>(LearningErrors.Invalid(problems));
        }

        var rows = await log.ListAsync(query.State?.ToLowerInvariant(), query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<HypothesisView>>([.. rows.Select(r => HypothesisView.Of(r, r.Row.CreatedAt, []))]);
    }
}
