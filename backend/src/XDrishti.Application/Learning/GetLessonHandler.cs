using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One lesson with its history (spec P6-16).</summary>
internal sealed class GetLessonHandler(ILessonLog log) : IQueryHandler<GetLessonQuery, Result<LessonView>>
{
    public async ValueTask<Result<LessonView>> HandleAsync(GetLessonQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var history = await log.HistoryAsync(query.Id, cancellationToken);
        if (history.Count == 0)
        {
            return Result.Failure<LessonView>(LessonErrors.NotFound(query.Id));
        }

        var steps = history.Select(h => new LessonStepView(h.Id, h.Row.State, h.Row.Reason, h.Row.Actor, h.Row.CreatedAt)).ToList();
        return Result.Success(LessonView.Of(history[^1], history[0].Row.CreatedAt, steps));
    }
}
