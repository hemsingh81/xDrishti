using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One changelog row (spec P6-08).</summary>
internal sealed class GetChangelogEntryHandler(ILearningChangelog changelog) : IQueryHandler<GetChangelogEntryQuery, Result<ChangelogEntryView>>
{
    public async ValueTask<Result<ChangelogEntryView>> HandleAsync(GetChangelogEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var row = await changelog.FindAsync(query.Id, cancellationToken);
        return row is null ? Result.Failure<ChangelogEntryView>(ChangelogErrors.NotFound(query.Id)) : Result.Success(ChangelogEntryView.Of(row));
    }
}
