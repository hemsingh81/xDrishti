using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Limits;

namespace XDrishti.Application.Execution;

/// <summary>One stored snapshot of the global execution switches (spec P12-07 criterion 13, ADR 0174). The <c>values</c> object is not shown: this route does not change it.</summary>
public sealed record ExecutionSettingsRowView(long Id, bool ExecutionEnabled, bool AutoLiveEnabled, string Actor, string Reason, DateTimeOffset ChangedAt, DateTimeOffset EffectiveFrom, string SnapshotHash);

/// <summary>
/// The two global switches: the row in force (null: never written, both OFF), the raise waiting for its <c>effectiveFrom</c> (null: none), the latest 20 rows, and <see cref="AutoLiveLocked"/> (always true: no route turns <c>auto_live_enabled</c>
/// on in this build). <see cref="ServerNow"/> is the database clock.
/// </summary>
public sealed record ExecutionSettingsView(ExecutionSettingsRowView? InForce, ExecutionSettingsRowView? Pending, IReadOnlyList<ExecutionSettingsRowView> History, bool AutoLiveLocked, DateTimeOffset ServerNow);

/// <summary>A row to append. <see cref="EffectiveFrom"/> is the next-session time for switching execution on (the trigger refuses an earlier one) and is stamped by the database for anything else.</summary>
public sealed record NewExecutionSettings(bool ExecutionEnabled, string Actor, string Reason, DateTimeOffset EffectiveFrom);

/// <summary>
/// The settings record as the runtime role: SELECT on <c>execution.settings</c> and INSERT of the owner's snapshot, which the trigger <c>execution.check_settings</c> judges again (owner, reason, <c>auto_live_enabled</c> OFF, the cooling-off of a raise,
/// the values carried over). An insert answers the new id or the database's refusal.
/// </summary>
public interface IExecutionSettingsAccess
{
    Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken);

    /// <summary>The newest rows, newest first.</summary>
    Task<IReadOnlyList<ExecutionSettingsRowView>> RowsAsync(int take, CancellationToken cancellationToken);

    Task<(long? Id, string? Refused)> InsertAsync(NewExecutionSettings row, CancellationToken cancellationToken);
}

public static class ExecutionSettingsErrors
{
    public const string AutoLiveLocked = "execution.auto_live_locked";
}

internal static partial class SettingsReading
{
    public const int HistorySize = 20;

    /// <summary>The row in force (the newest whose effective time has passed) and the raise waiting after it, the same rule as the database.</summary>
    public static ExecutionSettingsView View(IReadOnlyList<ExecutionSettingsRowView> newestFirst, DateTimeOffset now)
    {
        var inForce = newestFirst.FirstOrDefault(r => r.EffectiveFrom <= now);
        var pending = newestFirst.TakeWhile(r => r != inForce).FirstOrDefault(r => r.EffectiveFrom > now);
        return new ExecutionSettingsView(inForce, pending, newestFirst, AutoLiveLocked: true, now);
    }

    [LoggerMessage(EventId = 6780, Level = LogLevel.Information, Message = "execution.settings_changed settings={Id} execution_enabled={On} kind={Kind}")]
    public static partial void Changed(ILogger logger, long id, bool on, string kind);

    [LoggerMessage(EventId = 6781, Level = LogLevel.Information, Message = "execution.settings_change_refused code={Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 6782, Level = LogLevel.Warning, Message = "execution.audit_failed failure={Failure}")]
    public static partial void AuditFailed(ILogger logger, string failure);
}

/// <summary>The global switches. Read only.</summary>
public sealed record GetExecutionSettingsQuery : IQuery<ExecutionSettingsView>;

internal sealed class GetExecutionSettingsHandler(IExecutionSettingsAccess access) : IQueryHandler<GetExecutionSettingsQuery, ExecutionSettingsView>
{
    public async ValueTask<ExecutionSettingsView> HandleAsync(GetExecutionSettingsQuery query, CancellationToken cancellationToken) =>
        SettingsReading.View(await access.RowsAsync(SettingsReading.HistorySize, cancellationToken), await access.NowAsync(cancellationToken));
}

/// <summary>
/// The owner's change of the global switches (spec P12-07 criterion 13 and P12-10 criterion 7, in the reduced form of ADR 0174). The endpoint checked the session and the CSRF token and built the actor from the session; the fresh password is checked
/// here for EVERY change (stricter than the spec, which asks it only for a raise) through the step-up handler without opening the elevation window. <see cref="AutoLiveEnabled"/> true is always refused.
/// </summary>
public sealed record ChangeExecutionSettingsCommand(string Actor, Guid SessionId, ClientInfo Client, bool? ExecutionEnabled, bool? AutoLiveEnabled, string? Reason, string? Password) : ICommand;

/// <summary>
/// Appends one snapshot. Switching <c>execution_enabled</c> from OFF (in force) to ON is a raise: it needs the live-writes gate (checked before the password) and takes effect at 09:00 India time of the next trading day after one full closed
/// session; anything else (OFF, or ON while ON is in force) is in force at once, and an OFF row also supersedes a raise still waiting. <c>auto_live_enabled</c> stays OFF: a request to turn it on is refused before anything is checked or written.
/// Every stored change leaves an audit event. Nothing here can send an order or make an automated mode reachable.
/// </summary>
internal sealed class ChangeExecutionSettingsHandler(
    IExecutionSettingsAccess access,
    ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
    ILiveWritesGate gate,
    ITradingCalendar calendar,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ChangeExecutionSettingsHandler> logger) : ICommandHandler<ChangeExecutionSettingsCommand, ExecutionSettingsView>
{
    public async ValueTask<Result<ExecutionSettingsView>> HandleAsync(ChangeExecutionSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModeActRules.IsOwner(command.Actor))
        {
            return Refuse(Error.Forbidden(LimitsErrors.OwnerOnly, "Only the signed-in owner can change the execution switches."));
        }

        if (command.AutoLiveEnabled == true)
        {
            return Refuse(Error.Conflict(ExecutionSettingsErrors.AutoLiveLocked, "auto_live_enabled stays OFF: no route turns it on in this build."));
        }

        if (command.ExecutionEnabled is not { } on)
        {
            return Refuse(Error.Validation(LimitsErrors.ValuesInvalid, "executionEnabled: true or false."));
        }

        if (!ModeActRules.ReasonOk(command.Reason))
        {
            return Refuse(Error.Validation(LimitsErrors.ValuesInvalid, "reason: 1 to 300 characters without secrets."));
        }

        var now = await access.NowAsync(cancellationToken);
        var inForce = SettingsReading.View(await access.RowsAsync(SettingsReading.HistorySize, cancellationToken), now).InForce;
        var raise = on && inForce?.ExecutionEnabled != true;
        if (raise && !gate.Enabled)
        {
            return Refuse(Error.Forbidden("live.capability_off", "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."));
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            return Refuse(Error.Validation(LimitsErrors.PasswordRequired, "Every change of the execution switches needs your password."));
        }

        var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
        if (checkedPassword.IsFailure)
        {
            return Refuse(checkedPassword.Error);
        }

        var effectiveFrom = raise ? LimitChange.EffectiveFrom(now, await calendar.GetAsync(cancellationToken)) : now;
        var (id, refused) = await access.InsertAsync(new NewExecutionSettings(on, command.Actor, command.Reason!.Trim(), effectiveFrom), cancellationToken);
        if (id is not { } stored)
        {
            return Refuse(Error.Conflict(LimitsErrors.Stale, "The switches were not stored: " + (refused ?? "the database refused the row") + ". Reload and try again."));
        }

        var kind = raise ? "raise" : "lowering";
        SettingsReading.Changed(logger, stored, on, kind);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.ExecutionSettingsChanged, null, $"settings={stored} execution_enabled={(on ? "on" : "off")} kind={kind}", command.Actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // The row stands; the audit cross-index is a second write. Only the type of the failure is logged.
            SettingsReading.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(SettingsReading.View(await access.RowsAsync(SettingsReading.HistorySize, cancellationToken), await access.NowAsync(cancellationToken)));
    }

    private Result<ExecutionSettingsView> Refuse(Error error)
    {
        SettingsReading.Refused(logger, error.Code);
        return Result.Failure<ExecutionSettingsView>(error);
    }
}
