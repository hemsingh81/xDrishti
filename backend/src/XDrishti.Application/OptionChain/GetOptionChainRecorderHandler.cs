using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Depth;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Application.OptionChain;

/// <summary>Reads the switch and says which of the three gates (deployment flag, the owner's switch, the service) keeps the option-chain recorder from running, from the switch row, the service settings and the recorder's heartbeat. Reads no chain data.</summary>
internal sealed class GetOptionChainRecorderHandler(IOptionChainSwitchStore switches, IOptionChainHeartbeats heartbeats, ServiceSettingsReader services) : IQueryHandler<GetOptionChainRecorderQuery, OptionChainRecorderView>
{
    public const int HistoryLimit = 50;

    public const string Notice = "Off until the deployment flag, your switch (with your password) and the service are all on. The recorder only stores option chains; nothing reads them and nothing trades on them.";

    public async ValueTask<OptionChainRecorderView> HandleAsync(GetOptionChainRecorderQuery query, CancellationToken cancellationToken)
    {
        var current = await switches.CurrentAsync(cancellationToken);
        var history = await switches.HistoryAsync(HistoryLimit, cancellationToken);
        var status = await heartbeats.GetAsync(cancellationToken);
        var service = (await services.ReadAsync(OptionChainRecorderService.Definition, cancellationToken)).Settings;
        var reasons = new List<string>();
        if (status is null)
        {
            reasons.Add("The feed host has not reported yet (is xd-feed running?).");
        }
        else if (status.Details is { DeploymentEnabled: false })
        {
            reasons.Add("The deployment flag Options__Chain__Enabled is false; it is changed in the deployment and needs a restart.");
        }

        if (current?.Enabled != true)
        {
            reasons.Add("Your switch is OFF.");
        }

        if (!service.Enabled)
        {
            reasons.Add("The service option-chain-recorder is paused on the Services screen.");
        }

        if (status is { IsAlive: true } && Explain(status.State) is { } stateReason)
        {
            reasons.Add(stateReason);
        }

        return new OptionChainRecorderView(
            current?.Enabled == true,
            current?.Id,
            status?.State,
            status?.LastSeenAt,
            reasons,
            Notice,
            [.. history.Select(h => new OptionChainSwitchChangeView(h.Id, h.Enabled, h.Actor, h.Reason, h.ChangedAt))]);
    }

    private static string? Explain(string state) => state switch
    {
        DepthStates.NoToken => "The Data account has no usable token.",
        DepthStates.TokenRefused => "Dhan refused the Data account's token; waiting for a new one.",
        DepthStates.NotSubscribed => "Dhan says the Data APIs are not part of the plan (DH-902); recording ended for the day.",
        DepthStates.DiskBudget => "The stored option chains are over their cap; recording stopped and nothing was deleted.",
        DepthStates.DiskFree => "The free disk space is under its floor; recording stopped and nothing was deleted.",
        DepthStates.NoInstruments => "No option underlyings to record today.",
        ChainStates.RatePaused => "Dhan's rate limit was hit in three slots in a row; requests are paused for 15 minutes.",
        _ => null,
    };
}
