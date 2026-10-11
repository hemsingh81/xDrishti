using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Auth;

/// <summary>The latest authentication events, newest first.</summary>
public sealed record GetAuditQuery(int Take = GetAuditQuery.DefaultTake) : IQuery<IReadOnlyList<AuditEventResponse>>
{
    public const int DefaultTake = 50;

    public const int MaxTake = 200;
}

public sealed record AuditEventResponse(DateTimeOffset At, string Event, string? Email, string? Ip, string? UserAgent);

internal sealed class GetAuditHandler(IAuditLog audit) : IQueryHandler<GetAuditQuery, IReadOnlyList<AuditEventResponse>>
{
    public async ValueTask<IReadOnlyList<AuditEventResponse>> HandleAsync(GetAuditQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, GetAuditQuery.MaxTake);
        var events = await audit.ListLatestAsync(take, cancellationToken);
        return [.. events.Select(e => new AuditEventResponse(e.At, e.Event.ToString(), e.Email, e.Ip, e.UserAgent))];
    }
}
