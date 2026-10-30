using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Execution;

/// <summary>The owner's answer to a break: <c>accept_broker</c> (adopt the broker's figure as true) or <c>dismiss_foreign</c> (a foreign item, remembered as explained for the day). The note is the owner's own text, at most 300 characters.</summary>
public sealed record ExecutionBreakResolution(DateTimeOffset At, string By, string Resolution, string Note);

/// <summary>One break of a reconciliation run with its resolution, if any. Kind is one of the eight of spec P12-06.</summary>
public sealed record ExecutionBreakView(long Id, long RunId, Guid AccountId, string Kind, string Severity, string? Symbol, long? IntentId, string Detail, DateTimeOffset At, ExecutionBreakResolution? Resolution);

/// <summary>One reconciliation run. A run that could not read the broker is <c>Readable</c> false and carries a break: it is never clean.</summary>
public sealed record ExecutionReconRunView(long Id, DateTimeOffset At, Guid AccountId, DateOnly SessionDate, string Kind, bool Readable, int Orders, int Trades, int Positions, int Breaks);

public sealed record ExecutionReconciliationView(IReadOnlyList<ExecutionReconRunView> Runs, IReadOnlyList<ExecutionBreakView> Breaks);

/// <summary>Reads the runs and breaks of the execution service (spec P12-06): read only, newest first, at most 200 of each.</summary>
public sealed record GetExecutionReconciliationQuery(DateOnly? Date, Guid? AccountId) : IQuery<ExecutionReconciliationView>;

public interface IExecutionReconReader
{
    Task<ExecutionReconciliationView> ReadAsync(DateOnly? date, Guid? accountId, int take, CancellationToken cancellationToken);

    Task<ExecutionBreakView?> FindBreakAsync(long id, CancellationToken cancellationToken);
}

/// <summary>Writes the owner's resolution row as the runtime role (the only INSERT it holds on the reconciliation tables). The database trigger checks again; its refusal comes back as text, an unexpected failure throws.</summary>
public interface IExecutionBreakResolutions
{
    /// <summary>Null when the row was written; otherwise the database's refusal text.</summary>
    Task<string?> ResolveAsync(long breakId, string actor, string resolution, string note, CancellationToken cancellationToken);
}

public static class ExecutionBreakErrors
{
    public const string Unknown = "execution.break_unknown";
    public const string Resolved = "execution.break_resolved";
    public const string Invalid = "execution.resolution_invalid";
}

internal sealed class GetExecutionReconciliationHandler(IExecutionReconReader reader) : IQueryHandler<GetExecutionReconciliationQuery, ExecutionReconciliationView>
{
    public const int Take = 200;

    public async ValueTask<ExecutionReconciliationView> HandleAsync(GetExecutionReconciliationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await reader.ReadAsync(query.Date, query.AccountId, Take, cancellationToken);
    }
}

/// <summary>The owner's resolution of one break. The endpoint checked the live-writes gate, then the fresh password for this act; the actor was built from the session.</summary>
public sealed record ResolveExecutionBreakCommand(string Actor, long BreakId, string? Resolution, string? Note) : ICommand;

/// <summary>
/// The rules of a resolution, pure: the owner actor, a known resolution word, <c>dismiss_foreign</c> only for a foreign conflict, a note of at most 300 characters without secret-like words. Every refusal is a word and all of them are returned together. The audit is a log line with ids and
/// codes and no free text; the note stays in the insert-only row.
/// </summary>
public static partial class BreakResolutionRules
{
    [GeneratedRegex("(token|secret|password|apikey|authorization)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Forbidden();

    public static IReadOnlyList<string> Refusals(string kind, string actor, string? resolution, string? note)
    {
        var refused = new List<string>();
        if (!actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length)
        {
            refused.Add("owner_required");
        }

        if (resolution is not ("accept_broker" or "dismiss_foreign"))
        {
            refused.Add("resolution_unknown");
        }
        else if (resolution == "dismiss_foreign" && kind != "foreign_conflict")
        {
            refused.Add("dismiss_foreign_only_for_foreign");
        }

        if ((note?.Length ?? 0) > 300 || Forbidden().IsMatch(note ?? string.Empty))
        {
            refused.Add("note_invalid");
        }

        return refused;
    }
}

internal sealed partial class ResolveExecutionBreakHandler(IExecutionReconReader reader, IExecutionBreakResolutions resolutions, ILogger<ResolveExecutionBreakHandler> logger) : ICommandHandler<ResolveExecutionBreakCommand, ExecutionBreakView>
{
    public async ValueTask<Result<ExecutionBreakView>> HandleAsync(ResolveExecutionBreakCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await reader.FindBreakAsync(command.BreakId, cancellationToken) is not { } found)
        {
            return Result.Failure<ExecutionBreakView>(Error.NotFound(ExecutionBreakErrors.Unknown, "There is no such break."));
        }

        if (found.Resolution is not null)
        {
            return Result.Failure<ExecutionBreakView>(Error.Conflict(ExecutionBreakErrors.Resolved, "The break is already resolved."));
        }

        var refusals = BreakResolutionRules.Refusals(found.Kind, command.Actor, command.Resolution, command.Note);
        if (refusals.Count > 0)
        {
            var words = string.Join(", ", refusals);
            LogRefused(logger, found.Id, words);
            return Result.Failure<ExecutionBreakView>(Error.Validation(ExecutionBreakErrors.Invalid, "The break is not resolved: " + words + "."));
        }

        if (await resolutions.ResolveAsync(command.BreakId, command.Actor, command.Resolution!, command.Note?.Trim() ?? string.Empty, cancellationToken) is { } refused)
        {
            LogRefused(logger, found.Id, "database");
            return refused.Contains("already resolved", StringComparison.Ordinal)
                ? Result.Failure<ExecutionBreakView>(Error.Conflict(ExecutionBreakErrors.Resolved, "The break is already resolved."))
                : Result.Failure<ExecutionBreakView>(Error.Validation(ExecutionBreakErrors.Invalid, "The break is not resolved: " + refused));
        }

        LogResolved(logger, found.Id, found.Kind, command.Resolution!);
        return Result.Success(await reader.FindBreakAsync(command.BreakId, cancellationToken) ?? found);
    }

    [LoggerMessage(EventId = 6675, Level = LogLevel.Information, Message = "execution.break_resolved break={BreakId} kind={Kind} resolution={Resolution}")]
    private static partial void LogResolved(ILogger logger, long breakId, string kind, string resolution);

    [LoggerMessage(EventId = 6676, Level = LogLevel.Information, Message = "execution.break_resolution_refused break={BreakId} reasons={Reasons}")]
    private static partial void LogRefused(ILogger logger, long breakId, string reasons);
}
