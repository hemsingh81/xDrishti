using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Depth;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Application.OptionBackfill;

/// <summary>Reads the switch and says which of the four gates (deployment flag, the owner's switch, the service, the probe acceptance) keeps the backfill from running, from the switch row, the acceptance row, the service settings and the heartbeat. Reads no bar.</summary>
internal sealed class GetOptionBackfillHandler(IOptionBackfillSwitchStore switches, IBackfillProbeAckStore acknowledgements, IOptionBackfillHeartbeats heartbeats, ServiceSettingsReader services) : IQueryHandler<GetOptionBackfillQuery, OptionBackfillView>
{
    public const int HistoryLimit = 50;

    public const string Notice = "Off until the deployment flag, your switch (with your password), your acceptance of the probe result and the service are all on. It only stores expired-option bars, between 16:30 and 08:30; nothing reads them and nothing trades on them.";

    public async ValueTask<OptionBackfillView> HandleAsync(GetOptionBackfillQuery query, CancellationToken cancellationToken)
    {
        var current = await switches.CurrentAsync(cancellationToken);
        var history = await switches.HistoryAsync(HistoryLimit, cancellationToken);
        var ack = await acknowledgements.CurrentAsync(cancellationToken);
        var status = await heartbeats.GetAsync(cancellationToken);
        var service = (await services.ReadAsync(OptionBackfillService.Definition, cancellationToken)).Settings;
        var reasons = new List<string>();
        if (status is null)
        {
            reasons.Add("The feed host has not reported yet (is xd-feed running?).");
        }
        else if (status.Details is { DeploymentEnabled: false })
        {
            reasons.Add("The deployment flag Options__Backfill__Enabled is false; it is changed in the deployment and needs a restart.");
        }

        if (current?.Enabled != true)
        {
            reasons.Add("Your switch is OFF.");
        }

        if (ack is null)
        {
            reasons.Add("You have not accepted the probe result (run the probe, record it in ADR 0177, then accept it here).");
        }

        if (!service.Enabled)
        {
            reasons.Add("The service options-backfill is paused on the Services screen.");
        }

        if (status is { IsAlive: true } && Explain(status.State) is { } stateReason)
        {
            reasons.Add(stateReason);
        }

        var progress = status?.Details?.Progress is { } p
            ? new OptionBackfillProgressView(p.Planned, p.Done, p.Empty, p.NoData, p.GivenUp, p.Left, status.Details.RequestsPerSecond > 0 ? Math.Round(p.Left / status.Details.RequestsPerSecond / 3600d, 1) : null)
            : null;
        return new OptionBackfillView(
            current?.Enabled == true,
            current?.Id,
            ack is null ? null : new BackfillProbeAcceptedView(ack.Id, ack.Actor, ack.Note, ack.AcceptedAt),
            status?.State,
            status?.LastSeenAt,
            progress,
            reasons,
            Notice,
            [.. history.Select(h => new OptionBackfillSwitchChangeView(h.Id, h.Enabled, h.Actor, h.Reason, h.ChangedAt))]);
    }

    private static string? Explain(string state) => state switch
    {
        DepthStates.NoToken => "The Data account has no usable token.",
        DepthStates.TokenRefused => "Dhan refused the Data account's token; waiting for a new one.",
        DepthStates.NotSubscribed => "Dhan says the Data APIs are not part of the plan (DH-902); the backfill ended for the day.",
        DepthStates.DiskBudget => "The stored backfill is over its cap; fetching stopped and nothing was deleted.",
        DepthStates.DiskFree => "The free disk space is under its floor; fetching stopped and nothing was deleted.",
        DepthStates.NoInstruments => "No NIFTY, BANKNIFTY or share targets in the option underlyings.",
        BackfillStates.OutsideWindow => "It is outside the night window (16:30 to 08:30 India time).",
        BackfillStates.DailyCap => "The day's request cap was reached; it goes on the next India day.",
        BackfillStates.RatePaused => "Dhan rate limited five answers in a row; the backfill waits 15 minutes.",
        BackfillStates.StoreFailed => "Three answers in a row could not be stored; the backfill waits 5 minutes.",
        BackfillStates.TooManyFailures => "Five keys in a row were given up; the backfill ended for the day (see the fetch log).",
        BackfillStates.BadRequest => "Dhan said the request itself is wrong (DH-905); the backfill ended for the day (see the probe).",
        BackfillStates.Done => "Every planned key is finished or given up.",
        _ => null,
    };
}
