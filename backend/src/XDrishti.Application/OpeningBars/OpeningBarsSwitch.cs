using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OpeningBars;

/// <summary>Reads the owner's opening-bars switch, its history and why the recorder is not running (BK-03b). Reads no bar.</summary>
public sealed record GetOpeningBarsRecorderQuery : IQuery<OpeningBarsRecorderView>;

/// <summary>Switches the opening-minute recorder as the owner: the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint), never from a body. Every call appends a row; none is edited.</summary>
public sealed record ChangeOpeningBarsCommand(string Actor, bool Enabled, string Reason) : ICommand;

/// <summary>The answer of a switch change: the row and the new value.</summary>
public sealed record OpeningBarsSwitched(long Id, bool Enabled);

/// <summary>One change of the switch.</summary>
public sealed record OpeningBarsChangeView(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The recorder as the owner sees it: the stored switch (<c>false</c> when never set), the state word of its last run (null when it never ran), when that was, the reasons it is not running now and the last 50 changes. Same shape as the other recorders' views.</summary>
public sealed record OpeningBarsRecorderView(bool Enabled, long? SwitchId, string? State, DateTimeOffset? LastSeenAt, IReadOnlyList<string> NotRunningBecause, string Notice, IReadOnlyList<OpeningBarsChangeView> History);

internal sealed class GetOpeningBarsRecorderHandler(IOpeningBarsSwitchStore switches, IServiceRunStore runs, ServiceSettingsReader services) : IQueryHandler<GetOpeningBarsRecorderQuery, OpeningBarsRecorderView>
{
    public const int HistoryLimit = 50;

    public const string Notice = "Off until the deployment flag, your switch (with your password) and the service are all on. It records the 1-minute bars of 09:15 to 09:31 so that Gappers and Opening-range wide can be formed from complete minutes; a missed minute leaves the basket not formed. Read-only market data: no order.";

    public async ValueTask<OpeningBarsRecorderView> HandleAsync(GetOpeningBarsRecorderQuery query, CancellationToken cancellationToken)
    {
        var history = await switches.HistoryAsync(HistoryLimit, cancellationToken);
        var service = (await services.ReadAsync(OpeningBarsService.Definition, cancellationToken)).Settings;
        var last = (await runs.GetLatestAsync([OpeningBarsService.Id], cancellationToken)).GetValueOrDefault(OpeningBarsService.Id);
        var on = history.Count > 0 && history[0].Enabled;
        var reasons = new List<string>();
        if (last is null)
        {
            reasons.Add("The recorder has not run yet; the deployment flag OpeningBars__Enabled must be true in the feed host (it is changed in the deployment and needs a restart).");
        }

        if (!on)
        {
            reasons.Add("Your switch is OFF.");
        }

        if (!service.Enabled)
        {
            reasons.Add("The service opening-bars is paused on the Services screen.");
        }

        return new OpeningBarsRecorderView(
            on,
            history.Count > 0 ? history[0].Id : null,
            last?.Status.ToString().ToLowerInvariant(),
            last?.FinishedAt ?? last?.StartedAt,
            reasons,
            Notice,
            [.. history.Select(h => new OpeningBarsChangeView(h.Id, h.Enabled, h.Actor, h.Reason, h.ChangedAt))]);
    }
}

/// <summary>
/// The owner's act of switching the opening-minute recorder (BK-03b, as the tick recorder's). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update) and then one account event
/// <c>OpeningBarsSwitched</c> in its own unit of work (its failure is logged and does not undo the row). The endpoint is responsible for the session and the fresh password. Starts nothing itself: the recorder follows the switch at its next look.
/// </summary>
internal sealed class ChangeOpeningBarsHandler(IOpeningBarsSwitchStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeOpeningBarsHandler> logger) : ICommandHandler<ChangeOpeningBarsCommand, OpeningBarsSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<OpeningBarsSwitched>> HandleAsync(ChangeOpeningBarsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            return Result.Failure<OpeningBarsSwitched>(Error.Forbidden("opening_bars.owner_only", "Only the signed-in owner switches the opening-minute recorder."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<OpeningBarsSwitched>(Error.Validation("opening_bars.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        var row = await store.AppendAsync(command.Enabled, actor, reason, cancellationToken);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.OpeningBarsSwitched, null, $"switch={row.Id} opening_bars={(row.Enabled ? "on" : "off")}", actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            OpeningBarsLog.AuditFailed(logger, failure.GetType().Name); // the row stands; the audit cross-index is a second write
        }

        return Result.Success(new OpeningBarsSwitched(row.Id, row.Enabled));
    }
}
