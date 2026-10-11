using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>The redirect URL the owner registers with the Dhan app (it depends on where xDrishti is reached).</summary>
public sealed record GetDhanRedirectQuery : IQuery<DhanRedirectView>;

internal sealed class GetDhanRedirectHandler(IBrokerSettings settings) : IQueryHandler<GetDhanRedirectQuery, DhanRedirectView>
{
    public ValueTask<DhanRedirectView> HandleAsync(GetDhanRedirectQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DhanRedirectView(DhanLogin.RedirectUrl(settings.PublicBaseUrl)));
}
