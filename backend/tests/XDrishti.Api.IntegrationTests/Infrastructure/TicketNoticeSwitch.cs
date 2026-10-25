using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Stands in front of the registered ticket-notice source (P8-15b): with <see cref="Items"/> null it passes through to the real default, so a test sees production behaviour; a test sets <see cref="Items"/> to play P7-20's items.</summary>
public sealed class TicketNoticeSwitch(ITicketNoticeSource inner) : ITicketNoticeSource
{
    public IReadOnlyList<PlanNotice>? Items { get; set; }

    public ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Items is { } items ? ValueTask.FromResult(items) : inner.ListAsync(now, cancellationToken);
}
