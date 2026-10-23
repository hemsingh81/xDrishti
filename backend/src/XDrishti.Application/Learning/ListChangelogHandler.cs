using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>Lists changelog rows, newest first (spec P6-08). An empty list is an answer.</summary>
internal sealed class ListChangelogHandler(ILearningChangelog changelog) : IQueryHandler<ListChangelogQuery, Result<IReadOnlyList<ChangelogEntryView>>>
{
    public async ValueTask<Result<IReadOnlyList<ChangelogEntryView>>> HandleAsync(ListChangelogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new List<FieldError>();
        if (query.SubjectKind is not (null or "cell" or "model"))
        {
            problems.Add(new FieldError("subjectKind", "The subject kind is cell or model."));
        }

        if (query.Subject?.Length > 300)
        {
            problems.Add(new FieldError("subject", "The subject is at most 300 characters."));
        }

        if (query.Limit is < 1 or > 200)
        {
            problems.Add(new FieldError("limit", "The limit is 1 to 200."));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<IReadOnlyList<ChangelogEntryView>>(LearningErrors.Invalid(problems));
        }

        var rows = await changelog.ListAsync(query.SubjectKind, query.Subject, query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<ChangelogEntryView>>([.. rows.Select(ChangelogEntryView.Of)]);
    }
}
