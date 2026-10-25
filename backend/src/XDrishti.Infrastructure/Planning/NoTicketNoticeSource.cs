using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>The ticket-notice source while price-crossing alerts are not enabled or not built (P7-20 replaces it; default OFF): nothing to say, nothing read.</summary>
internal sealed class NoTicketNoticeSource : ITicketNoticeSource
{
    public ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<PlanNotice>>([]);
}
