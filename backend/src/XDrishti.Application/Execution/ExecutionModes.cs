using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution;

namespace XDrishti.Application.Execution;

/// <summary>Reads the execution mode of every account with a Trading role and the evidence for its next step (spec P12-07). Read only; writes nothing.</summary>
public sealed record GetExecutionModesQuery : IQuery<ExecutionModesView>;

public sealed record ExecutionModeCheckView(string Code, string Outcome, string Text, int? N, int? Needed);

public sealed record ExecutionLadderStepView(string From, string To, string Verdict, IReadOnlyList<ExecutionModeCheckView> Checks);

public sealed record ExecutionTrailView(long Id, string From, string To, string Kind, string Actor, string Reason, DateTimeOffset EffectiveFrom, DateTimeOffset ChangedAt);

public sealed record ExecutionPromotionRequestView(Guid Id, string ToMode, DateTimeOffset RequestedAt, DateTimeOffset EligibleFrom, DateTimeOffset ExpiresAt, string EvidenceHash);

/// <summary>One account: its configured and effective mode, a change that waits for the next open, the open request, the next step of the ladder (null where there is none) and the latest 20 trail rows.</summary>
public sealed record ExecutionModeAccountView(
    Guid AccountId,
    string Name,
    string ConfiguredMode,
    string EffectiveMode,
    ExecutionTrailView? Pending,
    ExecutionPromotionRequestView? Request,
    ExecutionLadderStepView? Next,
    IReadOnlyList<ExecutionTrailView> Trail);

/// <summary><see cref="ExecutionEnabled"/> is null when the switch could not be read (it then counts as OFF).</summary>
public sealed record ExecutionModesView(bool? ExecutionEnabled, int GatePolicyVersion, IReadOnlyList<ExecutionModeAccountView> Accounts);

public sealed record ModeAccountRow(Guid AccountId, string Name, TradingMode Configured);

public sealed record ModeTrailRow(long Id, Guid AccountId, string From, string To, string Kind, string Actor, string Reason, Guid? RequestId, DateTimeOffset EffectiveFrom, DateTimeOffset ChangedAt);

public sealed record ModeRequestRow(Guid Id, Guid AccountId, string From, string To, DateTimeOffset RequestedAt, DateTimeOffset EligibleFrom, DateTimeOffset ExpiresAt, string EvidenceHash);

public sealed record NewModeRequest(Guid Id, Guid AccountId, string From, string To, string By, string Reason, DateTimeOffset EligibleFrom, DateTimeOffset ExpiresAt, string EvidenceJson, string EvidenceHash);

public sealed record NewModeTrailRow(Guid AccountId, string From, string To, string Kind, string Actor, string Reason, Guid? RequestId, string SnapshotJson, string SnapshotHash, DateTimeOffset EffectiveFrom);

/// <summary>The mode record of the execution schema as the runtime role: SELECT everywhere, INSERT only into the two owner-act tables (the triggers judge order, window and shape). An insert answers null when written, else the database's refusal.</summary>
public interface IExecutionModeStore
{
    Task<IReadOnlyList<ModeAccountRow>> AccountsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ModeTrailRow>> TrailAsync(Guid accountId, int take, CancellationToken cancellationToken);

    Task<ModeRequestRow?> NewestRequestAsync(Guid accountId, CancellationToken cancellationToken);

    Task<ModeRequestRow?> FindRequestAsync(Guid id, CancellationToken cancellationToken);

    Task<string?> InsertRequestAsync(NewModeRequest request, CancellationToken cancellationToken);

    Task<string?> InsertTrailAsync(NewModeTrailRow row, CancellationToken cancellationToken);
}

/// <summary>The facts of the gate that come from stored records. A fact that cannot be read is null; one whose record does not exist yet (the sandbox suite, the static-IP attestation) is null too, so the gate stays closed.</summary>
public interface IExecutionModeFacts
{
    Task<bool?> ExecutionEnabledAsync(CancellationToken cancellationToken);

    Task<ModeGateFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken);
}

public static class ExecutionModeErrors
{
    public const string OwnerOnly = "execution.owner_only";
    public const string ValuesInvalid = "execution.values_invalid";
    public const string NotAdjacent = "execution.mode_not_adjacent";
    public const string GateFailed = "execution.mode_gate_failed";
    public const string CoolingOff = "execution.cooling_off";
    public const string Expired = "execution.request_expired";
    public const string Stale = "execution.stale";
    public const string NotLower = "execution.mode_not_lower";
    public const string UnknownAccount = "execution.account_unknown";
    public const string UnknownRequest = "execution.request_unknown";
}

/// <summary>The effective mode and the pieces every handler needs, in one place: the trail and the configured mode decide, at the instant given.</summary>
internal static class ModeReading
{
    public static (LadderMode Effective, ModeTrailRow? Pending) Read(ModeAccountRow account, IReadOnlyList<ModeTrailRow> trailNewestFirst, DateTimeOffset now)
    {
        var inForce = trailNewestFirst.FirstOrDefault(t => t.EffectiveFrom <= now);
        var pending = trailNewestFirst.TakeWhile(t => t != inForce).FirstOrDefault(t => t.EffectiveFrom > now);
        return (ExecutionModes.Effective(inForce?.To, ExecutionModes.Configured(account.Configured)), pending);
    }

    public static ExecutionTrailView View(ModeTrailRow t) => new(t.Id, t.From, t.To, t.Kind, t.Actor, t.Reason, t.EffectiveFrom, t.ChangedAt);

    public static ExecutionModeCheckView View(GateCheck c) => new(c.Code, c.OutcomeName, c.Text, c.N, c.Needed);

    public static string Snapshot(LadderMode from, LadderMode to, ModeGateResult gate) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["policy"] = ModeGate.PolicyVersion,
            ["from"] = ExecutionModes.Name(from),
            ["to"] = ExecutionModes.Name(to),
            ["eligible"] = gate.Eligible,
            ["checks"] = gate.Checks.Select(c => new Dictionary<string, object?> { ["code"] = c.Code, ["outcome"] = c.OutcomeName, ["n"] = c.N, ["needed"] = c.Needed }).ToList(),
        });
}

/// <summary>The gate facts of an account: the stored ones plus the verdict of the small-live phase check, recomputed from its records now (read only). A phase check that cannot be computed is <c>unavailable</c>.</summary>
internal sealed class ModeEvidence(IExecutionModeFacts facts, IQueryHandler<LivePhaseCheckQuery, Result<LivePhaseCheckView>> phase)
{
    public async Task<ModeGateFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var stored = await facts.ReadAsync(accountId, cancellationToken);
        string? verdict = null;
        try
        {
            var result = await phase.HandleAsync(new LivePhaseCheckQuery(accountId, null), cancellationToken);
            if (result.IsSuccess && result.Value.State == "value")
            {
                verdict = result.Value.Report.ExitVerdict;
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            verdict = null;
        }

        return stored with { LivePhaseVerdict = verdict };
    }
}

internal sealed class GetExecutionModesHandler(IExecutionModeStore store, IExecutionModeFacts facts, ModeEvidence evidence, TimeProvider time) : IQueryHandler<GetExecutionModesQuery, ExecutionModesView>
{
    public async ValueTask<ExecutionModesView> HandleAsync(GetExecutionModesQuery query, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var views = new List<ExecutionModeAccountView>();

        // Shortcut: one trail read, one request read and (for manual_live) one evidence read per account. Fine for the handful of accounts of a single owner; upgrade to one batched query per table when accounts pass ~10 or the p95 of this route passes 80 ms.
        // A PostgresException the store does not map (anything but the mode_refused messages and check, foreign-key or unique violations) deliberately propagates and answers 500: an unknown database failure is never turned into a refusal or a pass.
        foreach (var account in await store.AccountsAsync(cancellationToken))
        {
            var trail = await store.TrailAsync(account.AccountId, 20, cancellationToken);
            var (effective, pending) = ModeReading.Read(account, trail, now);
            ExecutionLadderStepView? next = null;
            if (effective == LadderMode.ManualLive)
            {
                var gate = ModeGate.Evaluate(effective, LadderMode.Assisted, await evidence.ReadAsync(account.AccountId, cancellationToken), now);
                next = new ExecutionLadderStepView(ExecutionModes.Name(effective), "assisted", gate.Eligible ? "eligible" : "not_eligible", [.. gate.Checks.Select(ModeReading.View)]);
            }

            var request = await store.NewestRequestAsync(account.AccountId, cancellationToken);
            var open = request is not null && request.ExpiresAt >= now && request.From == ExecutionModes.Name(effective) && !trail.Any(t => t.RequestId == request.Id)
                ? new ExecutionPromotionRequestView(request.Id, request.To, request.RequestedAt, request.EligibleFrom, request.ExpiresAt, request.EvidenceHash)
                : null;
            views.Add(new ExecutionModeAccountView(account.AccountId, account.Name, ExecutionModes.Name(ExecutionModes.Configured(account.Configured)), ExecutionModes.Name(effective), pending is null ? null : ModeReading.View(pending), open, next, [.. trail.Select(ModeReading.View)]));
        }

        return new ExecutionModesView(await facts.ExecutionEnabledAsync(cancellationToken), ModeGate.PolicyVersion, views);
    }
}

/// <summary>The owner's request to move one step up. The endpoint checked Live:Writes:Enabled, then a fresh password; the actor was built from the session. It stores the evidence and grants nothing.</summary>
public sealed record RequestPromotionCommand(string Actor, Guid AccountId, string? ToMode, string? Reason) : ICommand;

/// <summary>The owner's confirmation of the request, after the cooling-off, with the gate evaluated again.</summary>
public sealed record ConfirmPromotionCommand(string Actor, Guid AccountId, Guid RequestId) : ICommand;

/// <summary>The owner's step down: a session and the CSRF token, no password (it lowers risk).</summary>
public sealed record DemoteModeCommand(string Actor, Guid AccountId, string? ToMode, string? Reason) : ICommand;

internal static partial class ModeActRules
{
    [GeneratedRegex("(token|secret|password|apikey|authorization)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Forbidden();

    public static bool IsOwner(string actor) => actor.StartsWith("owner:", StringComparison.Ordinal) && actor.Length > "owner:".Length;

    public static bool ReasonOk(string? reason) => reason?.Trim() is { Length: > 0 and <= 300 } text && !Forbidden().IsMatch(text);
}

internal sealed partial class RequestPromotionHandler(IExecutionModeStore store, ModeEvidence evidence, ITradingCalendar calendar, TimeProvider time, ILogger<RequestPromotionHandler> logger) : ICommandHandler<RequestPromotionCommand, ExecutionPromotionRequestView>
{
    public async ValueTask<Result<ExecutionPromotionRequestView>> HandleAsync(RequestPromotionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModeActRules.IsOwner(command.Actor))
        {
            return Result.Failure<ExecutionPromotionRequestView>(Error.Forbidden(ExecutionModeErrors.OwnerOnly, "Only the signed-in owner can ask for a promotion."));
        }

        if (ExecutionModes.TryParse(command.ToMode) is not { } to || !ModeActRules.ReasonOk(command.Reason))
        {
            return Result.Failure<ExecutionPromotionRequestView>(Error.Validation(ExecutionModeErrors.ValuesInvalid, "A promotion names a mode (paper, manual_live or assisted) and a reason of 1 to 300 characters without secrets."));
        }

        var now = time.GetUtcNow();
        var found = await Find(store, command.AccountId, now, cancellationToken);
        if (found is null)
        {
            return Result.Failure<ExecutionPromotionRequestView>(Error.NotFound(ExecutionModeErrors.UnknownAccount, "There is no such account with a Trading role."));
        }

        var from = found.Value.Effective;
        if (!ExecutionModes.IsAdjacentPromotion(from, to))
        {
            return Result.Failure<ExecutionPromotionRequestView>(Error.Conflict(ExecutionModeErrors.NotAdjacent, "A promotion moves one step up at a time, from " + ExecutionModes.Name(from) + " to the next."));
        }

        var gate = ModeGate.Evaluate(from, to, await evidence.ReadAsync(command.AccountId, cancellationToken), now);
        var (fromName, toName, shortHash) = (ExecutionModes.Name(from), ExecutionModes.Name(to), gate.Hash[..12]);
        if (!gate.Eligible)
        {
            LogRefused(logger, fromName, toName, shortHash);
            return Result.Failure<ExecutionPromotionRequestView>(Error.Conflict(ExecutionModeErrors.GateFailed, "The evidence is not there: " + string.Join(" ", gate.Reasons)));
        }

        var window = PromotionTiming.Window(now, await calendar.GetAsync(cancellationToken));
        var id = Guid.NewGuid();
        var refused = await store.InsertRequestAsync(new NewModeRequest(id, command.AccountId, ExecutionModes.Name(from), ExecutionModes.Name(to), command.Actor, command.Reason!.Trim(), window.EligibleFrom, window.ExpiresAt, ModeReading.Snapshot(from, to, gate), gate.Hash), cancellationToken);
        if (refused is not null)
        {
            LogRefused(logger, fromName, toName, "database");
            return Result.Failure<ExecutionPromotionRequestView>(Error.Conflict(ExecutionModeErrors.GateFailed, "The request is not stored: " + refused));
        }

        LogRequested(logger, fromName, toName, shortHash);
        return Result.Success(new ExecutionPromotionRequestView(id, ExecutionModes.Name(to), now, window.EligibleFrom, window.ExpiresAt, gate.Hash));
    }

    internal static async Task<(ModeAccountRow Account, LadderMode Effective)?> Find(IExecutionModeStore store, Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = (await store.AccountsAsync(cancellationToken)).FirstOrDefault(a => a.AccountId == accountId);
        if (account is null)
        {
            return null;
        }

        return (account, ModeReading.Read(account, await store.TrailAsync(accountId, 20, cancellationToken), now).Effective);
    }

    [LoggerMessage(EventId = 6700, Level = LogLevel.Information, Message = "execution.mode_requested from={From} to={To} evidence={Evidence}")]
    private static partial void LogRequested(ILogger logger, string from, string to, string evidence);

    [LoggerMessage(EventId = 6701, Level = LogLevel.Information, Message = "execution.mode_request_refused from={From} to={To} evidence={Evidence}")]
    private static partial void LogRefused(ILogger logger, string from, string to, string evidence);
}

internal sealed partial class ConfirmPromotionHandler(IExecutionModeStore store, ModeEvidence evidence, ITradingCalendar calendar, TimeProvider time, ILogger<ConfirmPromotionHandler> logger) : ICommandHandler<ConfirmPromotionCommand, ExecutionTrailView>
{
    public async ValueTask<Result<ExecutionTrailView>> HandleAsync(ConfirmPromotionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModeActRules.IsOwner(command.Actor))
        {
            return Result.Failure<ExecutionTrailView>(Error.Forbidden(ExecutionModeErrors.OwnerOnly, "Only the signed-in owner can confirm a promotion."));
        }

        var now = time.GetUtcNow();
        if (await RequestPromotionHandler.Find(store, command.AccountId, now, cancellationToken) is not { } found)
        {
            return Result.Failure<ExecutionTrailView>(Error.NotFound(ExecutionModeErrors.UnknownAccount, "There is no such account with a Trading role."));
        }

        if (await store.FindRequestAsync(command.RequestId, cancellationToken) is not { } request || request.AccountId != command.AccountId)
        {
            return Result.Failure<ExecutionTrailView>(Error.NotFound(ExecutionModeErrors.UnknownRequest, "There is no such promotion request for this account."));
        }

        var newest = await store.NewestRequestAsync(command.AccountId, cancellationToken);
        if (newest?.Id != request.Id || request.From != ExecutionModes.Name(found.Effective) || ExecutionModes.TryParse(request.To) is not { } to)
        {
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.Stale, "The request is no longer the newest for this account, or the account's mode has changed since."));
        }

        if (now < request.EligibleFrom)
        {
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.CoolingOff, "The cooling-off has not ended: the request can be confirmed from " + request.EligibleFrom.ToString("u", System.Globalization.CultureInfo.InvariantCulture) + "."));
        }

        if (now > request.ExpiresAt)
        {
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.Expired, "The request has expired; ask again."));
        }

        var gate = ModeGate.Evaluate(found.Effective, to, await evidence.ReadAsync(command.AccountId, cancellationToken), now);
        if (!gate.Eligible)
        {
            LogRefused(logger, request.To, gate.Hash[..12]);
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.GateFailed, "The evidence no longer holds: " + string.Join(" ", gate.Reasons)));
        }

        var effectiveFrom = PromotionTiming.EffectiveFrom(now, await calendar.GetAsync(cancellationToken));
        var refused = await store.InsertTrailAsync(new NewModeTrailRow(command.AccountId, request.From, request.To, "promotion", command.Actor, "promotion request " + request.Id.ToString("N")[..8], request.Id, ModeReading.Snapshot(found.Effective, to, gate), gate.Hash, effectiveFrom), cancellationToken);
        if (refused is not null)
        {
            LogRefused(logger, request.To, "database");
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.GateFailed, "The promotion is not recorded: " + refused));
        }

        LogPromoted(logger, request.From, request.To);
        var row = (await store.TrailAsync(command.AccountId, 1, cancellationToken)).FirstOrDefault(t => t.RequestId == request.Id);
        return row is null ? Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.Stale, "The promotion was recorded but could not be read back.")) : Result.Success(ModeReading.View(row));
    }

    [LoggerMessage(EventId = 6702, Level = LogLevel.Information, Message = "execution.mode_promoted from={From} to={To}")]
    private static partial void LogPromoted(ILogger logger, string from, string to);

    [LoggerMessage(EventId = 6703, Level = LogLevel.Information, Message = "execution.mode_confirm_refused to={To} evidence={Evidence}")]
    private static partial void LogRefused(ILogger logger, string to, string evidence);
}

internal sealed partial class DemoteModeHandler(IExecutionModeStore store, TimeProvider time, ILogger<DemoteModeHandler> logger) : ICommandHandler<DemoteModeCommand, ExecutionTrailView>
{
    public async ValueTask<Result<ExecutionTrailView>> HandleAsync(DemoteModeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModeActRules.IsOwner(command.Actor))
        {
            return Result.Failure<ExecutionTrailView>(Error.Forbidden(ExecutionModeErrors.OwnerOnly, "Only the signed-in owner can lower a mode."));
        }

        if (ExecutionModes.TryParse(command.ToMode) is not { } to || !ModeActRules.ReasonOk(command.Reason))
        {
            return Result.Failure<ExecutionTrailView>(Error.Validation(ExecutionModeErrors.ValuesInvalid, "A demotion names a mode (paper or manual_live) and a reason of 1 to 300 characters without secrets."));
        }

        var now = time.GetUtcNow();
        if (await RequestPromotionHandler.Find(store, command.AccountId, now, cancellationToken) is not { } found)
        {
            return Result.Failure<ExecutionTrailView>(Error.NotFound(ExecutionModeErrors.UnknownAccount, "There is no such account with a Trading role."));
        }

        if (!ExecutionModes.IsDemotion(found.Effective, to))
        {
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.NotLower, "A demotion goes to a lower mode than " + ExecutionModes.Name(found.Effective) + "."));
        }

        var (fromName, toName) = (ExecutionModes.Name(found.Effective), ExecutionModes.Name(to));
        var snapshot = JsonSerializer.Serialize(new Dictionary<string, object> { ["policy"] = ModeGate.PolicyVersion, ["from"] = fromName, ["to"] = toName, ["by"] = "owner" });
        var refused = await store.InsertTrailAsync(new NewModeTrailRow(command.AccountId, fromName, toName, "owner_demotion", command.Actor, command.Reason!.Trim(), null, snapshot, ModeGate.HashOf([]), now), cancellationToken);
        if (refused is not null)
        {
            return Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.NotLower, "The demotion is not recorded: " + refused));
        }

        LogDemoted(logger, fromName, toName);
        var newest = await store.TrailAsync(command.AccountId, 1, cancellationToken);
        var row = newest.Count > 0 ? newest[0] : null;
        return row is null ? Result.Failure<ExecutionTrailView>(Error.Conflict(ExecutionModeErrors.Stale, "The demotion was recorded but could not be read back.")) : Result.Success(ModeReading.View(row));
    }

    [LoggerMessage(EventId = 6704, Level = LogLevel.Information, Message = "execution.mode_demoted from={From} to={To} by=owner")]
    private static partial void LogDemoted(ILogger logger, string from, string to);
}
