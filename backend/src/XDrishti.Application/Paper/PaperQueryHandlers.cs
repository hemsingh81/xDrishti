using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>The paper entries (spec P10-01). Read only.</summary>
internal sealed class ListPaperEntriesHandler(IPaperStore store) : IQueryHandler<ListPaperEntriesQuery, Result<PaperEntryList>>
{
    public async ValueTask<Result<PaperEntryList>> HandleAsync(ListPaperEntriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<PaperEntryList>(PaperErrors.Invalid("limit", "The limit is 1 to 200."));
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            return Result.Failure<PaperEntryList>(PaperErrors.Invalid("from", "The first day is after the last day."));
        }

        var items = await store.ListEntriesAsync(query.From, query.To, query.AccountId, query.EpochId, query.Limit, cancellationToken);
        return Result.Success(new PaperEntryList(PaperWording.Basis, PaperWording.Limits, items));
    }
}

/// <summary>The capture rows (spec P10-01). Read only.</summary>
internal sealed class ListCaptureRunsHandler(IPaperStore store) : IQueryHandler<ListCaptureRunsQuery, Result<CaptureRunList>>
{
    public async ValueTask<Result<CaptureRunList>> HandleAsync(ListCaptureRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<CaptureRunList>(PaperErrors.Invalid("limit", "The limit is 1 to 200."));
        }

        return Result.Success(new CaptureRunList(PaperWording.Basis, PaperWording.Limits, await store.ListRunsAsync(query.Limit, cancellationToken)));
    }
}
