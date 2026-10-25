using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>The ticket-alert source while price-crossing alerts are not enabled (P7-20 replaces it; default OFF): nothing to say, nothing read.</summary>
internal sealed class NoTicketNoticeSource : ITicketNoticeSource
{
    public ValueTask<IReadOnlyList<TicketNoticeCount>> CountsAsync(IReadOnlyList<long> planIds, DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<TicketNoticeCount>>([]);
}
