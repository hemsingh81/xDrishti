using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>One stored weekly digest (spec P7-15). Read only.</summary>
internal sealed class GetDigestHandler(IDigestStore store) : IQueryHandler<GetDigestQuery, Result<DigestView>>
{
    public async ValueTask<Result<DigestView>> HandleAsync(GetDigestQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Week is null)
        {
            var newest = await store.ListAsync(1, cancellationToken);
            return newest.Count == 0
                ? Result.Failure<DigestView>(Error.NotFound(LearningErrors.DigestNotFoundCode, "No weekly digest is stored yet."))
                : Result.Success(BuildDigestHandler.Of(newest[0], false));
        }

        if (DigestWeek.TryParse(query.Week) is not { } week)
        {
            return Result.Failure<DigestView>(LearningErrors.Invalid([new FieldError("week", "The week is written 2026-W43.")]));
        }

        var stored = await store.FindByWeekAsync(week.Key, cancellationToken);
        return stored is null
            ? Result.Failure<DigestView>(Error.NotFound(LearningErrors.DigestNotFoundCode, $"No digest is stored for {week.Key}."))
            : Result.Success(BuildDigestHandler.Of(stored, false));
    }
}

/// <summary>The stored weekly digests, newest week first (spec P7-15). Read only.</summary>
internal sealed class ListDigestsHandler(IDigestStore store) : IQueryHandler<ListDigestsQuery, Result<IReadOnlyList<DigestView>>>
{
    public async ValueTask<Result<IReadOnlyList<DigestView>>> HandleAsync(ListDigestsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 52)
        {
            return Result.Failure<IReadOnlyList<DigestView>>(LearningErrors.Invalid([new FieldError("limit", "The limit is 1 to 52.")]));
        }

        var rows = await store.ListAsync(query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<DigestView>>([.. rows.Select(r => BuildDigestHandler.Of(r, false))]);
    }
}
