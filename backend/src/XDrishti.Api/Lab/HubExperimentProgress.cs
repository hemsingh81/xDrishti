using Microsoft.AspNetCore.SignalR;
using XDrishti.Api.Live;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.Lab;

/// <summary>The progress of replay jobs sent to every connected screen over the hub (spec P8-18 Part B). The hub already needs the owner's session, so "every connection" is the owner. A screen that was not connected reads the job by GET.</summary>
internal sealed class HubExperimentProgress(IHubContext<EventsHub> hub) : IExperimentJobProgress
{
    public async ValueTask ProgressAsync(ExperimentJobProgress progress, CancellationToken cancellationToken) =>
        await hub.Clients.All.SendAsync(LiveHubContract.ExperimentProgressEvent, progress, cancellationToken);

    public async ValueTask FinishedAsync(ExperimentJobFinished finished, CancellationToken cancellationToken) =>
        await hub.Clients.All.SendAsync(LiveHubContract.ExperimentFinishedEvent, finished, cancellationToken);
}
