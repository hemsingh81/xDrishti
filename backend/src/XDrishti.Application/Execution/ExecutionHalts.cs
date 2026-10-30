using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Execution;

/// <summary>Reads the halt record of the execution service (spec P12-05): read only, newest first. <see cref="Active"/> keeps only halts with neither a release nor an automatic clear.</summary>
public sealed record GetExecutionHaltsQuery(bool Active, Guid? AccountId, int Take) : IQuery<Result<IReadOnlyList<ExecutionHaltView>>>;

/// <summary>How a halt ended: by the owner (release) or automatically (a clear of a code that may clear itself). The release reason is the owner's own text; the automatic clear carries none.</summary>
public sealed record ExecutionHaltEnd(DateTimeOffset At, string By, string How, string? Reason);

/// <summary>One halt. Scope is <c>system</c> or <c>account</c>; level is <c>entries</c>, <c>cancel</c>, <c>exit_all</c> or <c>freeze</c>; End is null while it is active.</summary>
public sealed record ExecutionHaltView(long Id, DateTimeOffset At, string Scope, Guid? AccountId, string Level, string Trigger, string Source, string Actor, string Reason, ExecutionHaltEnd? End);

/// <summary>Everything the release rules judge, read now from the record.</summary>
public sealed record HaltReleaseFacts(ExecutionHaltView Halt, bool OpenPositions, bool OpenProtectionAlarms, int BreaksOnRecord = 0, int UnresolvedBreaks = 0);

public interface IExecutionHaltReader
{
    Task<IReadOnlyList<ExecutionHaltView>> ReadAsync(bool active, Guid? accountId, int take, CancellationToken cancellationToken);

    /// <summary>The halt and the facts for its release, or null when there is no such halt.</summary>
    Task<HaltReleaseFacts?> FindAsync(long id, CancellationToken cancellationToken);
}

/// <summary>Leaves an instruction for the execution service as a row in <c>execution.commands</c> (the API's only write into that schema); returns its id.</summary>
public interface IExecutionCommands
{
    Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken);
}

/// <summary>Writes the owner's release row as the runtime role. The database trigger checks again what a database can; a refusal comes back as the reasons, an unexpected failure throws.</summary>
public interface IExecutionHaltReleases
{
    /// <summary>Null when the row was written; otherwise the database's refusal text.</summary>
    Task<string?> ReleaseAsync(long haltId, string actor, string reason, CancellationToken cancellationToken);
}

internal sealed class GetExecutionHaltsHandler(IExecutionHaltReader reader) : IQueryHandler<GetExecutionHaltsQuery, Result<IReadOnlyList<ExecutionHaltView>>>
{
    public const int MaxTake = 200;

    public async ValueTask<Result<IReadOnlyList<ExecutionHaltView>>> HandleAsync(GetExecutionHaltsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Take is < 1 or > MaxTake
            ? Result.Failure<IReadOnlyList<ExecutionHaltView>>(Error.Validation("execution.halts_take", $"take must be 1 to {MaxTake}."))
            : Result.Success(await reader.ReadAsync(query.Active, query.AccountId, query.Take, cancellationToken));
    }
}

public static class ExecutionHaltErrors
{
    public const string Unknown = "execution.halt_unknown";
    public const string Invalid = "execution.halt_invalid";
    public const string ReleaseRefused = "execution.release_refused";
}

/// <summary>
/// The release rules the database cannot judge alone (spec P12-05 criterion 7). Pure: every refusal is a word, and all of them are returned together. The database trigger checks the exit_all, freeze and drawdown rules again; this adds the owner actor, the reason and
/// the protection rule (a protection halt is released only with no open protection alarm). A halt that has already ended cannot be released again.
/// </summary>
public static class HaltReleaseRules
{
    private static readonly string[] ProtectionCodes = ["protection_breach", "flatten_incomplete", "oversold", "protection_unknown"];

    public static IReadOnlyList<string> Refusals(HaltReleaseFacts facts, string actor, string? reason)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var refused = new List<string>();
        if (facts.Halt.End is not null)
        {
            refused.Add("already_ended");
        }

        if (!actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length)
        {
            refused.Add("owner_required");
        }

        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > 300)
        {
            refused.Add("reason_required");
        }
        else if (facts.Halt.Trigger == "drawdown" && text.Length < 20)
        {
            refused.Add("drawdown_review_too_short");
        }

        if (facts.Halt.Level == "freeze" && !(facts.Halt.Trigger == "reconciliation_break" && facts.BreaksOnRecord > 0 && facts.UnresolvedBreaks == 0))
        {
            // A freeze ends only after the reconciliation breaks of its account are all resolved by the owner (P12-06); a freeze of any other cause stays until the cause has its own release rule.
            refused.Add("freeze_needs_reconciliation");
        }

        if (facts.Halt.Level == "exit_all" && facts.OpenPositions)
        {
            refused.Add("exit_all_not_flat");
        }

        if (ProtectionCodes.Contains(facts.Halt.Trigger) && facts.OpenProtectionAlarms)
        {
            refused.Add("protection_unresolved");
        }

        return refused;
    }
}

/// <summary>The owner's button (spec P12-05 criterion 7): needs a session and the CSRF token, no password (starting a halt is the safe direction); <c>exit_all</c> additionally needs the step-up window because it realises losses. Carried out by xd-exec through a command row.</summary>
public sealed record StartExecutionHaltCommand(string Actor, string Scope, Guid? AccountId, string Level, string? Reason, bool BrokerKillSwitch, bool StepUpActive) : ICommand;

/// <summary>The command row left for the execution service; the halt itself is recorded when xd-exec carries it out (read the halts to see it).</summary>
public sealed record ExecutionHaltStarted(Guid CommandId);

internal sealed partial class StartExecutionHaltHandler(IExecutionCommands commands, ILogger<StartExecutionHaltHandler> logger) : ICommandHandler<StartExecutionHaltCommand, ExecutionHaltStarted>
{
    // The command table refuses a payload with these words anywhere; refuse them here with a clear answer instead of a database error.
    private static readonly Regex Forbidden = new("(token|secret|password|apikey|authorization)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public async ValueTask<Result<ExecutionHaltStarted>> HandleAsync(StartExecutionHaltCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (!command.Actor.StartsWith("owner:", StringComparison.Ordinal) || command.Actor.Length <= "owner:".Length)
        {
            return Result.Failure<ExecutionHaltStarted>(Error.Forbidden("execution.owner_only", "Only the signed-in owner can start a halt."));
        }

        var scopeOk = command.Scope == "system" ? command.AccountId is null : command.Scope == "account" && command.AccountId is not null;
        if (!scopeOk || command.Level is not ("entries" or "cancel" or "exit_all") || reason.Length is 0 or > 300 || Forbidden.IsMatch(reason))
        {
            return Result.Failure<ExecutionHaltStarted>(Error.Validation(ExecutionHaltErrors.Invalid, "A halt needs scope system (no account) or account (with one), level entries, cancel or exit_all, and a reason of 1 to 300 characters without secrets."));
        }

        if (command.Level == "exit_all" && !command.StepUpActive)
        {
            return Result.Failure<ExecutionHaltStarted>(AuthErrors.StepUpRequired);
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["scope"] = command.Scope, ["level"] = command.Level, ["reason"] = reason, ["brokerKillSwitch"] = command.BrokerKillSwitch });
        var id = await commands.EnqueueAsync("halt", command.AccountId, payload, command.Actor, cancellationToken);
        LogRequested(logger, command.Scope, command.Level);
        return Result.Success(new ExecutionHaltStarted(id));
    }

    [LoggerMessage(EventId = 6660, Level = LogLevel.Information, Message = "execution.halt_requested scope={Scope} level={Level}")]
    private static partial void LogRequested(ILogger logger, string scope, string level);
}

/// <summary>The owner's release: the gate and the fresh password were checked by the endpoint; the actor was built from the session. The audit is a log line with the ids and codes and no free text; the reason stays in the insert-only release row.</summary>
public sealed record ReleaseExecutionHaltCommand(string Actor, long HaltId, string? Reason) : ICommand;

internal sealed partial class ReleaseExecutionHaltHandler(IExecutionHaltReader reader, IExecutionHaltReleases releases, ILogger<ReleaseExecutionHaltHandler> logger) : ICommandHandler<ReleaseExecutionHaltCommand, ExecutionHaltView>
{
    public async ValueTask<Result<ExecutionHaltView>> HandleAsync(ReleaseExecutionHaltCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await reader.FindAsync(command.HaltId, cancellationToken) is not { } facts)
        {
            return Result.Failure<ExecutionHaltView>(Error.NotFound(ExecutionHaltErrors.Unknown, "There is no such halt."));
        }

        var refusals = HaltReleaseRules.Refusals(facts, command.Actor, command.Reason);
        if (refusals.Count > 0)
        {
            var words = string.Join(", ", refusals);
            LogRefused(logger, facts.Halt.Id, words);
            return Result.Failure<ExecutionHaltView>(Error.Conflict(ExecutionHaltErrors.ReleaseRefused, "The halt is not released: " + words + "."));
        }

        if (await releases.ReleaseAsync(command.HaltId, command.Actor, command.Reason!.Trim(), cancellationToken) is { } refused)
        {
            LogRefused(logger, facts.Halt.Id, "database");
            return Result.Failure<ExecutionHaltView>(Error.Conflict(ExecutionHaltErrors.ReleaseRefused, "The halt is not released: " + refused));
        }

        LogReleased(logger, facts.Halt.Id, facts.Halt.Trigger, facts.Halt.Level);
        var after = await reader.FindAsync(command.HaltId, cancellationToken);
        return Result.Success(after?.Halt ?? facts.Halt);
    }

    [LoggerMessage(EventId = 6661, Level = LogLevel.Information, Message = "execution.halt_released halt={HaltId} trigger={Trigger} level={Level}")]
    private static partial void LogReleased(ILogger logger, long haltId, string trigger, string level);

    [LoggerMessage(EventId = 6662, Level = LogLevel.Information, Message = "execution.halt_release_refused halt={HaltId} reasons={Reasons}")]
    private static partial void LogRefused(ILogger logger, long haltId, string reasons);
}
