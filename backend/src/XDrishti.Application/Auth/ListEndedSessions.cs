using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;

namespace XDrishti.Application.Auth;

/// <summary>
/// Which of these sessions have ended (logged out, revoked, idle for 30 minutes or older than 12 hours; an unknown session counts as ended). For long-lived connections (the live hub) that must close with their session. It only reads: unlike
/// <see cref="VerifySessionQuery"/> it does not slide the idle window, so an open connection alone does not keep a session alive - the screen's own requests do.
/// </summary>
public sealed record ListEndedSessionsQuery(IReadOnlyCollection<Guid> SessionIds) : IQuery<IReadOnlyList<Guid>>;

internal sealed class ListEndedSessionsHandler(ISessionStore sessions, TimeProvider clock) : IQueryHandler<ListEndedSessionsQuery, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> HandleAsync(ListEndedSessionsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ended = new List<Guid>();
        foreach (var id in query.SessionIds.Distinct())
        {
            var found = await sessions.FindByIdAsync(id, cancellationToken);
            if (found is null || found.Session.Evaluate(now) != SessionStatus.Active)
            {
                ended.Add(id);
            }
        }

        return ended;
    }
}
