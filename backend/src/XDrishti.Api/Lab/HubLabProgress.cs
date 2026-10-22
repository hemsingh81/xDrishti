using Microsoft.AspNetCore.SignalR;
using XDrishti.Api.Live;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.Lab;

/// <summary>The progress of Lab runs sent to every connected screen over the hub (spec P5-02, D5). The hub already needs the owner's session, so "every connection" is the owner. A screen that was not connected reads the run by GET.</summary>
internal sealed class HubLabProgress(IHubContext<EventsHub> hub) : ILabProgress
{
    public async ValueTask ProgressAsync(LabRunProgress progress, CancellationToken cancellationToken) =>
        await hub.Clients.All.SendAsync(LiveHubContract.LabRunProgressEvent, progress, cancellationToken);

    public async ValueTask FinishedAsync(LabRunFinished finished, CancellationToken cancellationToken) =>
        await hub.Clients.All.SendAsync(LiveHubContract.LabRunFinishedEvent, finished, cancellationToken);
}
