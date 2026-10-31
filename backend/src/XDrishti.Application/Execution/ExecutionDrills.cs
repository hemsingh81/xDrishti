using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Execution;

/// <summary>One stored drill (spec P12-05 criterion 10): passed, how long the halt took to be seen in milliseconds, and the detail of what was proved. No order was sent in any case.</summary>
public sealed record ExecutionDrillView(long Id, DateTimeOffset At, Guid AccountId, bool Passed, int LatencyMs, string Detail);

/// <summary>
/// One drill the owner asked for: the command row and, once the execution service carried it out, its answer. <see cref="Result"/> is <c>done</c> or <c>refused</c>; <see cref="Code"/> is <c>execution.drill_passed</c>, <c>execution.drill_failed</c>,
/// <c>execution.drill_refused:&lt;why&gt;</c> or <c>execution.drill_unavailable</c> (the service runs no drill outside the sandbox). Both null while the service has not answered.
/// </summary>
public sealed record ExecutionDrillRequestView(Guid CommandId, Guid? AccountId, DateTimeOffset At, string? Result, string? Code, DateTimeOffset? AnsweredAt);

/// <summary>The drills newest first (at most 50), the latest 20 requests with their answers, the age after which the mode ladder no longer counts a drill (<see cref="MaxAgeDays"/>) and the database clock.</summary>
public sealed record ExecutionDrillsView(IReadOnlyList<ExecutionDrillView> Drills, IReadOnlyList<ExecutionDrillRequestView> Requests, int MaxAgeDays, DateTimeOffset ServerNow);

/// <summary>The drill record and the drill commands, read as the runtime role (SELECT only).</summary>
public interface IExecutionDrillReader
{
    Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ExecutionDrillView>> DrillsAsync(Guid? accountId, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExecutionDrillRequestView>> RequestsAsync(Guid? accountId, int take, CancellationToken cancellationToken);
}

public static class ExecutionDrillErrors
{
    public const string Refused = "execution.drill_refused";
    public const string UnknownAccount = "execution.account_unknown";
    public const string CommandKind = "drill";

    /// <summary>The age after which the ladder gate no longer counts a drill (P12-07 gate table).</summary>
    public const int MaxAgeDays = 30;
}

/// <summary>The drills and the requests. Read only.</summary>
public sealed record GetExecutionDrillsQuery(Guid? AccountId) : IQuery<ExecutionDrillsView>;

internal sealed class GetExecutionDrillsHandler(IExecutionDrillReader reader) : IQueryHandler<GetExecutionDrillsQuery, ExecutionDrillsView>
{
    public async ValueTask<ExecutionDrillsView> HandleAsync(GetExecutionDrillsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ExecutionDrillsView(
            await reader.DrillsAsync(query.AccountId, 50, cancellationToken), await reader.RequestsAsync(query.AccountId, 20, cancellationToken), ExecutionDrillErrors.MaxAgeDays, await reader.NowAsync(cancellationToken));
    }
}

/// <summary>The owner's "Run drill" (spec P12-05 criterion 10). The endpoint checked the session, the CSRF token and a fresh password and built the actor from the session.</summary>
public sealed record RequestExecutionDrillCommand(string Actor, Guid? AccountId) : ICommand;

/// <summary>The command row left for the execution service; the drill row appears when the service has run it (read the drills to see it).</summary>
public sealed record ExecutionDrillStarted(Guid CommandId);

/// <summary>
/// Leaves a <c>drill</c> command for the execution service, which runs the drill only in the sandbox composition with a transport that refuses everything, so no order can leave in any case. Refused here, before anything is written, when the actor
/// is not the owner, the account is not a listed account, or any halt is in force (409 <c>execution.drill_refused</c>: the drill raises and clears a halt of its own and must not run beside another). The service checks the halts and the
/// account's book again. The payload carries nothing; an audit event names the account.
/// </summary>
internal sealed partial class RequestExecutionDrillHandler(
    IExecutionFactsReader facts, IExecutionCommands commands, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<RequestExecutionDrillHandler> logger) : ICommandHandler<RequestExecutionDrillCommand, ExecutionDrillStarted>
{
    public async ValueTask<Result<ExecutionDrillStarted>> HandleAsync(RequestExecutionDrillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModeActRules.IsOwner(command.Actor))
        {
            return Refuse(Error.Forbidden(LimitsErrors.OwnerOnly, "Only the signed-in owner can run a drill."));
        }

        if (command.AccountId is not { } account)
        {
            return Refuse(Error.Validation(LimitsErrors.ValuesInvalid, "accountId is required."));
        }

        var record = await facts.ReadAsync(cancellationToken);
        if (record.Accounts.All(a => a.AccountId != account))
        {
            return Refuse(Error.NotFound(ExecutionDrillErrors.UnknownAccount, "There is no such account."));
        }

        if (record.HaltActive)
        {
            return Refuse(Error.Conflict(ExecutionDrillErrors.Refused, "A halt is in force: the drill runs only when no halt is active. Release it first or wait until it clears."));
        }

        var id = await commands.EnqueueAsync(ExecutionDrillErrors.CommandKind, account, "{}", command.Actor, cancellationToken);
        LogRequested(logger, id);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.ExecutionDrillRequested, account, $"command={id}", command.Actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // The command stands; the audit cross-index is a second write. Only the type of the failure is logged.
            SettingsReading.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(new ExecutionDrillStarted(id));
    }

    private Result<ExecutionDrillStarted> Refuse(Error error)
    {
        LogRefused(logger, error.Code);
        return Result.Failure<ExecutionDrillStarted>(error);
    }

    [LoggerMessage(EventId = 6783, Level = LogLevel.Information, Message = "execution.drill_requested command={CommandId}")]
    private static partial void LogRequested(ILogger logger, Guid commandId);

    [LoggerMessage(EventId = 6784, Level = LogLevel.Information, Message = "execution.drill_request_refused code={Code}")]
    private static partial void LogRefused(ILogger logger, string code);
}
