using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Lists the newest view of each lesson (spec P6-16). An empty list is an answer.</summary>
internal sealed class ListLessonsHandler(ILessonLog log) : IQueryHandler<ListLessonsQuery, Result<IReadOnlyList<LessonView>>>
{
    public async ValueTask<Result<IReadOnlyList<LessonView>>> HandleAsync(ListLessonsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new List<FieldError>();
        if (query.Limit is < 1 or > 200)
        {
            problems.Add(new FieldError("limit", "The limit is 1 to 200."));
        }

        if (query.State is not null && LessonStateText.TryParse(query.State) is null)
        {
            problems.Add(new FieldError("state", "The state is open, linked, closed or dismissed."));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<IReadOnlyList<LessonView>>(LearningErrors.Invalid(problems));
        }

        var rows = await log.ListAsync(query.State?.ToLowerInvariant(), query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<LessonView>>([.. rows.Select(r => LessonView.Of(r, r.Row.CreatedAt, []))]);
    }
}
