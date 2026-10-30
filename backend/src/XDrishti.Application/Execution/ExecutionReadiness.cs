using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Execution;

/// <summary>One check of the readiness verdict. Outcome is <c>pass</c>, <c>fail</c> or <c>unavailable</c>; only <c>pass</c> is ever shown as good. The text never holds a token, a client id or an address.</summary>
public sealed record ExecutionReadinessCheck(string Code, string Outcome, string Text, int? AgeSeconds);

/// <summary>The state recorded at 09:10:30 India time for one session. Late means the service was not running then; the state is then <c>unavailable</c>.</summary>
public sealed record ExecutionDayStateView(DateOnly SessionDate, string State, bool Late, DateTimeOffset At, IReadOnlyList<string> Reasons);

/// <summary>The owner's claim, from Dhan's web tool, that orders are allowed for the address. A claim, not a verification (spec D5).</summary>
public sealed record ExecutionIpAttestationView(long Id, Guid AccountId, string Ip, bool OrdersAllowed, string Note, DateTimeOffset At);

/// <summary>The newest SEBI re-check of P11-05, read only.</summary>
public sealed record ExecutionRecheckView(string Outcome, DateOnly DecidedOn, DateOnly NextReviewOn);

/// <summary>
/// One account's readiness: <c>State</c> is <c>ready</c>, <c>not_ready</c> or <c>unavailable</c> (also when no check was ever recorded: nothing is ready until it says so). ReadyAt is the start of the current unbroken run of ready checks of the session. IpModifyAfter is the first day the
/// IP can be changed at Dhan again. SendsPerSecondMax is the highest number of order sends within one second recorded in <c>broker_calls</c> for the date, or null when there is none.
/// </summary>
public sealed record ExecutionReadinessAccount(
    Guid AccountId, string Name, string State, IReadOnlyList<ExecutionReadinessCheck> Checks, DateTimeOffset? CheckedAt, DateTimeOffset? TokenExpiresAt, DateTimeOffset? ReadyAt, DateOnly? IpModifyAfter,
    ExecutionIpAttestationView? Attestation, ExecutionRecheckView? LastRecheck, IReadOnlyList<ExecutionDayStateView> DayStates, int? SendsPerSecondMax);

public sealed record ExecutionReadinessView(DateOnly Date, IReadOnlyList<ExecutionReadinessAccount> Accounts);

/// <summary>Reads the readiness record (spec P12-11 criterion 11): read only. Without a date it is the current session of India.</summary>
public sealed record GetExecutionReadinessQuery(DateOnly? Date) : IQuery<Result<ExecutionReadinessView>>;

public interface IExecutionReadinessReader
{
    /// <summary>The accounts that have a readiness record or are at assisted, with the last 20 day states; <see cref="ExecutionReadinessAccount.LastRecheck"/> is left empty for the handler.</summary>
    Task<IReadOnlyList<ExecutionReadinessAccount>> ReadAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <summary>P11-05's newest <c>sebi_recheck</c> record, through a port so that this slice reads it and never stores it again.</summary>
public interface IComplianceRecords
{
    ValueTask<ExecutionRecheckView?> LatestSebiRecheckAsync(CancellationToken cancellationToken);
}

public interface IExecutionIpAttestations
{
    Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Inserts the row; the database stamps the time. Returns it.</summary>
    Task<ExecutionIpAttestationView> InsertAsync(Guid accountId, string ip, bool ordersAllowed, string note, string actor, CancellationToken cancellationToken);
}

public static class ExecutionReadinessErrors
{
    public const string Invalid = "execution.values_invalid";
    public const string NotFound = "execution.not_found";
}

internal sealed class GetExecutionReadinessHandler(IExecutionReadinessReader reader, IComplianceRecords compliance, TimeProvider clock) : IQueryHandler<GetExecutionReadinessQuery, Result<ExecutionReadinessView>>
{
    public async ValueTask<Result<ExecutionReadinessView>> HandleAsync(GetExecutionReadinessQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var date = query.Date ?? IndiaTime.Today(clock.GetUtcNow());
        var recheck = await compliance.LatestSebiRecheckAsync(cancellationToken);
        var accounts = await reader.ReadAsync(date, cancellationToken);
        return Result.Success(new ExecutionReadinessView(date, [.. accounts.Select(a => a with { LastRecheck = recheck })]));
    }
}

/// <summary>The owner records an attestation (spec P12-11 criterion 9): a session and the CSRF token, the actor from the session. It never contacts the broker; <c>ordersAllowed=false</c> is recorded and keeps <c>ip_attested</c> failing.</summary>
public sealed record RecordIpAttestationCommand(string Actor, Guid? AccountId, string? Ip, bool? OrdersAllowed, string? Note) : ICommand;

internal sealed partial class RecordIpAttestationHandler(IExecutionIpAttestations attestations, ILogger<RecordIpAttestationHandler> logger) : ICommandHandler<RecordIpAttestationCommand, ExecutionIpAttestationView>
{
    public const int MaxNote = 200;

    public async ValueTask<Result<ExecutionIpAttestationView>> HandleAsync(RecordIpAttestationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.Actor.StartsWith("owner:", StringComparison.Ordinal) || command.Actor.Length <= "owner:".Length)
        {
            return Result.Failure<ExecutionIpAttestationView>(Error.Forbidden("execution.owner_only", "Only the signed-in owner can record an attestation."));
        }

        var note = command.Note?.Trim() ?? string.Empty;
        if (command.AccountId is not { } account || command.OrdersAllowed is not { } allowed || !IpLiteral.TryNormalize(command.Ip, out var ip) || note.Length > MaxNote || note.Any(char.IsControl) || note.Contains('<', StringComparison.Ordinal) || note.Contains('>', StringComparison.Ordinal))
        {
            LogRefused(logger, "values");
            return Result.Failure<ExecutionIpAttestationView>(Error.Validation(ExecutionReadinessErrors.Invalid, "An attestation needs an account, an IP (one IPv4 or IPv6 address), ordersAllowed true or false and a plain-text note of at most 200 characters."));
        }

        if (!await attestations.AccountExistsAsync(account, cancellationToken))
        {
            LogRefused(logger, "account");
            return Result.Failure<ExecutionIpAttestationView>(Error.NotFound(ExecutionReadinessErrors.NotFound, "There is no such account."));
        }

        var row = await attestations.InsertAsync(account, ip, allowed, note, command.Actor, cancellationToken);
        var hash = Convert.ToHexStringLower(SHA256.HashData(account.ToByteArray()))[..8];
        LogRecorded(logger, hash, allowed);
        return Result.Success(row);
    }

    [LoggerMessage(EventId = 6747, Level = LogLevel.Information, Message = "execution.ip_attested account={Account} ordersAllowed={OrdersAllowed}")]
    private static partial void LogRecorded(ILogger logger, string account, bool ordersAllowed);

    [LoggerMessage(EventId = 6748, Level = LogLevel.Information, Message = "execution.ip_attestation_refused reason={Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}

/// <summary>The owner's check now: leaves a <c>readiness_now</c> command row. The execution service probes within a second and the card reads the result; the instruction carries no data.</summary>
public sealed record RequestReadinessCheckCommand(string Actor) : ICommand;

public sealed record ExecutionReadinessRequested(Guid CommandId);

internal sealed partial class RequestReadinessCheckHandler(IExecutionCommands commands, ILogger<RequestReadinessCheckHandler> logger) : ICommandHandler<RequestReadinessCheckCommand, ExecutionReadinessRequested>
{
    public async ValueTask<Result<ExecutionReadinessRequested>> HandleAsync(RequestReadinessCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.Actor.StartsWith("owner:", StringComparison.Ordinal) || command.Actor.Length <= "owner:".Length)
        {
            return Result.Failure<ExecutionReadinessRequested>(Error.Forbidden("execution.owner_only", "Only the signed-in owner can ask for a check."));
        }

        var id = await commands.EnqueueAsync("readiness_now", null, "{}", command.Actor, cancellationToken);
        LogRequested(logger);
        return Result.Success(new ExecutionReadinessRequested(id));
    }

    [LoggerMessage(EventId = 6749, Level = LogLevel.Information, Message = "execution.readiness_requested")]
    private static partial void LogRequested(ILogger logger);
}
