using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Application.Depth;

/// <summary>Reads the switch and says which of the three gates (deployment flag, the owner's switch, the service) keeps the recorder from running, from the switch row, the service settings and the recorder's heartbeat. Reads no depth data.</summary>
internal sealed class GetDepthRecorderHandler(IDepthSwitchStore switches, IDepthHeartbeats heartbeats, ServiceSettingsReader services) : IQueryHandler<GetDepthRecorderQuery, DepthRecorderView>
{
    public const int HistoryLimit = 50;

    public const string Notice = "Off until the deployment flag, your switch (with your password) and the service are all on. The recorder only stores the order book; nothing reads it and nothing trades on it.";

    public async ValueTask<DepthRecorderView> HandleAsync(GetDepthRecorderQuery query, CancellationToken cancellationToken)
    {
        var current = await switches.CurrentAsync(cancellationToken);
        var history = await switches.HistoryAsync(HistoryLimit, cancellationToken);
        var status = await heartbeats.GetAsync(cancellationToken);
        var service = (await services.ReadAsync(DepthRecorderService.Definition, cancellationToken)).Settings;
        var reasons = new List<string>();
        if (status is null)
        {
            reasons.Add("The feed host has not reported yet (is xd-feed running?).");
        }
        else if (status.Details is { DeploymentEnabled: false })
        {
            reasons.Add("The deployment flag Depth__Enabled is false; it is changed in the deployment and needs a restart.");
        }

        if (current?.Enabled != true)
        {
            reasons.Add("Your switch is OFF.");
        }

        if (!service.Enabled)
        {
            reasons.Add("The service depth-recorder is paused on the Services screen.");
        }

        if (status is { IsAlive: true } && Explain(status.State) is { } stateReason)
        {
            reasons.Add(stateReason);
        }

        return new DepthRecorderView(
            current?.Enabled == true,
            current?.Id,
            status?.State,
            status?.LastSeenAt,
            reasons,
            Notice,
            [.. history.Select(h => new DepthSwitchChangeView(h.Id, h.Enabled, h.Actor, h.Reason, h.ChangedAt))]);
    }

    private static string? Explain(string state) => state switch
    {
        DepthStates.NoToken => "The Data account has no usable token.",
        DepthStates.TokenRefused => "Dhan refused the Data account's token; waiting for a new one.",
        DepthStates.NotSubscribed => "Dhan says the Data APIs plan does not include market depth (806).",
        DepthStates.Flapping => "The connection kept dropping; attempts are paused for a few minutes.",
        DepthStates.DiskBudget => "The stored depth is over its cap; recording stopped and nothing was deleted.",
        DepthStates.DiskFree => "The free disk space is under its floor; recording stopped and nothing was deleted.",
        DepthStates.NoInstruments => "No instruments to record today.",
        _ => null,
    };
}
