using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>The halts of the live accounts in a range (default the last 30 days; at most 92), newest first. Read only.</summary>
internal sealed class GetLiveHaltsHandler(ILiveHaltStore halts, TimeProvider time) : IQueryHandler<GetLiveHaltsQuery, Result<IReadOnlyList<LiveHaltView>>>
{
    public const int MaxDays = 92;

    public async ValueTask<Result<IReadOnlyList<LiveHaltView>>> HandleAsync(GetLiveHaltsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = IndiaTime.Today(time.GetUtcNow());
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-GetRampHandler.HaltDays);
        if (from > to || to.DayNumber - from.DayNumber > MaxDays)
        {
            return Result.Failure<IReadOnlyList<LiveHaltView>>(Error.Validation(LiveErrors.Invalid, $"from must not be after to, and the range is at most {MaxDays} days"));
        }

        return Result.Success<IReadOnlyList<LiveHaltView>>([.. (await halts.ListAsync(query.AccountId, from, to, cancellationToken)).Where(h => h.Event == HaltCodes.Opened).Select(GetRampHandler.HaltView)]);
    }
}
