using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>The stored outcomes of live tickets in a range of sessions, newest session first, one page (size 1 to 200, range at most 92 days, default the last 30 days). Read only. 422 <c>live.invalid</c> for a bad range, page or size.</summary>
internal sealed class ListLiveOutcomesHandler(ILiveCaptureStore store, TimeProvider time) : IQueryHandler<LiveOutcomesQuery, Result<LiveOutcomesPage>>
{
    public const int MaxSize = 200;
    public const int MaxRangeDays = 92;
    public const int DefaultDays = 30;

    public async ValueTask<Result<LiveOutcomesPage>> HandleAsync(LiveOutcomesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = IndiaTime.Today(time.GetUtcNow());
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-DefaultDays);
        if (to > today)
        {
            return Fail("to must not be after today");
        }

        if (from > to)
        {
            return Fail("from must not be after to");
        }

        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            return Fail($"the range is at most {MaxRangeDays} days");
        }

        if (query.Page < 1 || query.Size is < 1 or > MaxSize)
        {
            return Fail($"page starts at 1 and size is 1 to {MaxSize}");
        }

        var page = await store.ListOutcomesAsync(query.AccountId, from, to, (query.Page - 1) * query.Size, query.Size, cancellationToken);
        return Result.Success(new LiveOutcomesPage([.. page.Items.Select(View)], query.Page, query.Size, page.Total));
    }

    private static Result<LiveOutcomesPage> Fail(string message) => Result.Failure<LiveOutcomesPage>(Error.Validation(LiveErrors.Invalid, message));

    private static LiveOutcomeView View(StoredOutcome s)
    {
        var o = s.Outcome;
        return new LiveOutcomeView(s.ItemId, s.AccountId, s.ForDate, o.Outcome, s.AllocatedQty, o.OrderedQty, o.EntryQty, o.FirstEntryAt, o.Flags, o.MissedR, o.PlanGradeStatus, o.OrderEvidence, o.Protection, o.RejectText, s.WrittenAt);
    }
}
