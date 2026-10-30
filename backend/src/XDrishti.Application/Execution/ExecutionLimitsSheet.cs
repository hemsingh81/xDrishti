using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Limits;

namespace XDrishti.Application.Execution;

/// <summary>One stored limits snapshot as the owner sees it (spec P12-09 Contract). A null cap is unset: it allows nothing. Times are India time <c>HH:mm</c>.</summary>
public sealed record LimitsRowView(
    long Id,
    Guid AccountId,
    int? MaxOrdersPerDay,
    decimal? MaxNotionalPerOrder,
    decimal? MaxNotionalPerDay,
    decimal? MaxLossPerDay,
    int? MaxOpenPositions,
    IReadOnlyList<string> AllowList,
    string? WindowStart,
    string? WindowEnd,
    IReadOnlyList<string> Products,
    bool ExitAllOnLossHalt,
    string Actor,
    string Reason,
    DateTimeOffset ChangedAt,
    DateTimeOffset EffectiveFrom,
    string SnapshotHash);

/// <summary>The compiled ceilings the owner cannot exceed (code constants, not settings). Money caps have none; the order rate is P12-01's <c>exec.max_orders_per_second</c>.</summary>
public sealed record LimitsCeilingsView(int MaxOrdersPerDay, int MaxOpenPositions, int MaxAllowList, string WindowEarliest, string WindowLatest, IReadOnlyList<string> Products);

/// <summary>
/// The limits of one account: the row in force (null: no sheet, nothing is allowed), the raise waiting for its <c>effectiveFrom</c>, whether the sheet in force is complete and which fields are missing (an incomplete sheet allows nothing),
/// the latest 20 rows and the ceilings.
/// </summary>
public sealed record LimitsView(LimitsRowView? InForce, LimitsRowView? Pending, bool Complete, IReadOnlyList<string> Missing, IReadOnlyList<LimitsRowView> History, LimitsCeilingsView Ceilings, DateTimeOffset ServerNow);

/// <summary>A stored row with its sheet.</summary>
public sealed record StoredLimits(long Id, Guid AccountId, ExecutionLimits Limits, string Actor, string Reason, DateTimeOffset ChangedAt, DateTimeOffset EffectiveFrom, string SnapshotHash);

/// <summary>A row to append. <see cref="EffectiveFrom"/> is the database clock for a lowering (the trigger stamps it) and the next-session time for a raise (the trigger refuses an earlier one).</summary>
public sealed record NewLimits(Guid AccountId, ExecutionLimits Limits, string Actor, string Reason, DateTimeOffset EffectiveFrom, string SnapshotHash);

/// <summary>
/// The limits record as the runtime role (spec P12-09 Grants): SELECT on <c>execution.limits</c> and INSERT, which the trigger <c>execution.check_limits</c> judges again (owner only, the raise rule, the ceilings). An insert answers the new
/// id, or the database's refusal.
/// </summary>
public interface IExecutionLimitsAccess
{
    Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken);

    /// <summary>Null when there is no such account with a Trading role; else whether the role is on.</summary>
    Task<bool?> TradingOnAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The newest rows of an account, newest first.</summary>
    Task<IReadOnlyList<StoredLimits>> RowsAsync(Guid accountId, int take, CancellationToken cancellationToken);

    Task<(long? Id, string? Refused)> InsertAsync(NewLimits row, CancellationToken cancellationToken);
}

public static class LimitsErrors
{
    public const string ValuesInvalid = "execution.values_invalid";
    public const string PasswordRequired = "execution.password_required";
    public const string OwnerOnly = "execution.owner_only";
    public const string Stale = "execution.stale";
    public const string Disabled = "execution.disabled";
    public const string UnknownAccount = "execution.account_unknown";
}

internal static partial class LimitsReading
{
    public const int HistorySize = 20;

    public static readonly LimitsCeilingsView Ceilings = new(
        LimitsCeilings.MaxOrdersPerDay, LimitsCeilings.MaxOpenPositions, LimitsCeilings.MaxAllowList, Hm(LimitsCeilings.WindowEarliest)!, Hm(LimitsCeilings.WindowLatest)!, LimitsCeilings.Products);

    /// <summary>The row in force (the newest whose effective time has passed) and the raise waiting after it, the same rule as the database.</summary>
    public static (StoredLimits? InForce, StoredLimits? Pending) Read(IReadOnlyList<StoredLimits> newestFirst, DateTimeOffset now)
    {
        var inForce = newestFirst.FirstOrDefault(r => r.EffectiveFrom <= now);
        var pending = newestFirst.TakeWhile(r => r != inForce).FirstOrDefault(r => r.EffectiveFrom > now);
        return (inForce, pending);
    }

    public static LimitsView View(IReadOnlyList<StoredLimits> rows, DateTimeOffset now)
    {
        var (inForce, pending) = Read(rows, now);
        var missing = (inForce?.Limits ?? ExecutionLimits.Empty).Missing();
        return new LimitsView(inForce is null ? null : Row(inForce), pending is null ? null : Row(pending), missing.Count == 0, missing, [.. rows.Select(Row)], Ceilings, now);
    }

    public static LimitsRowView Row(StoredLimits r) => new(
        r.Id, r.AccountId, r.Limits.MaxOrdersPerDay, r.Limits.MaxNotionalPerOrder, r.Limits.MaxNotionalPerDay, r.Limits.MaxLossPerDay, r.Limits.MaxOpenPositions, r.Limits.AllowList, Hm(r.Limits.WindowStart), Hm(r.Limits.WindowEnd),
        r.Limits.Products, r.Limits.ExitAllOnLossHalt, r.Actor, r.Reason, r.ChangedAt, r.EffectiveFrom, r.SnapshotHash);

    public static string? Hm(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    [LoggerMessage(EventId = 6728, Level = LogLevel.Information, Message = "execution.limits_changed account={Account} kind={Kind} sheet={Sheet}")]
    public static partial void Changed(ILogger logger, string account, string kind, string sheet);

    [LoggerMessage(EventId = 6729, Level = LogLevel.Information, Message = "execution.limits_change_refused code={Code}")]
    public static partial void Refused(ILogger logger, string code);
}

/// <summary>The limits of one account. Read only.</summary>
public sealed record GetExecutionLimitsQuery(Guid? AccountId) : IQuery<Result<LimitsView>>;

internal sealed class GetExecutionLimitsHandler(IExecutionLimitsAccess access) : IQueryHandler<GetExecutionLimitsQuery, Result<LimitsView>>
{
    public async ValueTask<Result<LimitsView>> HandleAsync(GetExecutionLimitsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.AccountId is not { } account)
        {
            return Result.Failure<LimitsView>(Error.Validation(LimitsErrors.ValuesInvalid, "accountId is required."));
        }

        if (await access.TradingOnAsync(account, cancellationToken) is null)
        {
            return Result.Failure<LimitsView>(Error.NotFound(LimitsErrors.UnknownAccount, "There is no such account with a Trading role."));
        }

        return Result.Success(LimitsReading.View(await access.RowsAsync(account, LimitsReading.HistorySize, cancellationToken), await access.NowAsync(cancellationToken)));
    }
}

/// <summary>
/// The owner's new limits sheet for one account (spec P12-09 criterion 9). The endpoint checked the session and the CSRF token and built the actor from the session; the password, needed only for a raise, is checked here through the step-up
/// handler without opening the elevation window (it counts toward the lockout). Times are India time <c>HH:mm</c>.
/// </summary>
public sealed record ChangeExecutionLimitsCommand(
    string Actor,
    Guid SessionId,
    ClientInfo Client,
    Guid AccountId,
    int? MaxOrdersPerDay,
    decimal? MaxNotionalPerOrder,
    decimal? MaxNotionalPerDay,
    decimal? MaxLossPerDay,
    int? MaxOpenPositions,
    IReadOnlyList<string>? AllowList,
    string? WindowStart,
    string? WindowEnd,
    IReadOnlyList<string>? Products,
    bool? ExitAllOnLossHalt,
    string? Reason,
    string? Password) : ICommand;

/// <summary>
/// Appends one complete snapshot. A change that loosens anything against the row in force (a cap up, a wider window, a symbol or a product added, the exit-all switch OFF; a mixed change counts as a raise) needs the password and takes effect
/// at 09:00 India time of the next trading day after one full closed session; a pure lowering needs only the session and takes effect at once. The clock is the database's. The answer shows the row in force and the pending row.
/// </summary>
internal sealed class ChangeExecutionLimitsHandler(
    IExecutionLimitsAccess access, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ITradingCalendar calendar, ILogger<ChangeExecutionLimitsHandler> logger) : ICommandHandler<ChangeExecutionLimitsCommand, LimitsView>
{
    public async ValueTask<Result<LimitsView>> HandleAsync(ChangeExecutionLimitsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModeActRules.IsOwner(command.Actor))
        {
            return Refuse(Error.Forbidden(LimitsErrors.OwnerOnly, "Only the signed-in owner can change the limits."));
        }

        if (Sheet(command) is not { } sheet)
        {
            return Refuse(Error.Validation(LimitsErrors.ValuesInvalid, "window: windowStart and windowEnd are India times HH:mm, both set or both empty."));
        }

        if (sheet.Invalid() is { } field)
        {
            return Refuse(Error.Validation(LimitsErrors.ValuesInvalid, field + ": " + Rule(field)));
        }

        if (!ModeActRules.ReasonOk(command.Reason))
        {
            return Refuse(Error.Validation(LimitsErrors.ValuesInvalid, "reason: 1 to 300 characters without secrets."));
        }

        if (await access.TradingOnAsync(command.AccountId, cancellationToken) is not { } tradingOn)
        {
            return Refuse(Error.NotFound(LimitsErrors.UnknownAccount, "There is no such account with a Trading role."));
        }

        var now = await access.NowAsync(cancellationToken);
        var rows = await access.RowsAsync(command.AccountId, LimitsReading.HistorySize, cancellationToken);
        var verdict = LimitChange.Classify(LimitsReading.Read(rows, now).InForce?.Limits, sheet);
        var effectiveFrom = now;
        if (verdict.IsRaise)
        {
            if (!tradingOn)
            {
                return Refuse(Error.Conflict(LimitsErrors.Disabled, "The account's Trading role is off: limits can only be lowered."));
            }

            if (string.IsNullOrEmpty(command.Password))
            {
                return Refuse(Error.Validation(LimitsErrors.PasswordRequired, "This change loosens " + string.Join(", ", verdict.Loosened) + ": your password is required."));
            }

            var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
            if (checkedPassword.IsFailure)
            {
                return Refuse(checkedPassword.Error);
            }

            effectiveFrom = LimitChange.EffectiveFrom(now, await calendar.GetAsync(cancellationToken));
        }

        var hash = sheet.Hash();
        var (id, refused) = await access.InsertAsync(new NewLimits(command.AccountId, sheet, command.Actor, command.Reason!.Trim(), effectiveFrom, hash), cancellationToken);
        if (id is null)
        {
            return Refuse(Error.Conflict(LimitsErrors.Stale, "The sheet was not stored: " + (refused ?? "the database refused it") + ". Reload and try again."));
        }

        LimitsReading.Changed(logger, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(command.AccountId.ToByteArray()))[..8], verdict.IsRaise ? "raise" : "lowering", hash[..12]);
        var after = await access.RowsAsync(command.AccountId, LimitsReading.HistorySize, cancellationToken);
        return Result.Success(LimitsReading.View(after, await access.NowAsync(cancellationToken)));
    }

    private static ExecutionLimits? Sheet(ChangeExecutionLimitsCommand c)
    {
        TimeOnly? start = null, end = null;
        if (!string.IsNullOrEmpty(c.WindowStart) || !string.IsNullOrEmpty(c.WindowEnd))
        {
            if (!TimeOnly.TryParseExact(c.WindowStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) || !TimeOnly.TryParseExact(c.WindowEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
            {
                return null;
            }

            (start, end) = (s, e);
        }

        return new ExecutionLimits(
            c.MaxOrdersPerDay, c.MaxNotionalPerOrder, c.MaxNotionalPerDay, c.MaxLossPerDay, c.MaxOpenPositions, [.. c.AllowList ?? []], start, end, [.. c.Products ?? []], c.ExitAllOnLossHalt ?? false);
    }

    private static string Rule(string field) => field switch
    {
        ExecutionLimits.FieldOrders => "1 to " + LimitsCeilings.MaxOrdersPerDay.ToString(CultureInfo.InvariantCulture) + ".",
        ExecutionLimits.FieldNotionalOrder or ExecutionLimits.FieldLoss => "a positive amount in rupees with at most two decimals.",
        ExecutionLimits.FieldNotionalDay => "a positive amount in rupees with at most two decimals, not below maxNotionalPerOrder.",
        ExecutionLimits.FieldPositions => "1 to " + LimitsCeilings.MaxOpenPositions.ToString(CultureInfo.InvariantCulture) + ".",
        ExecutionLimits.FieldAllowList => "at most " + LimitsCeilings.MaxAllowList.ToString(CultureInfo.InvariantCulture) + " distinct NSE trading symbols, upper case, as the instrument master spells them (TATAPOWER, M&M, BAJAJ-AUTO).",
        ExecutionLimits.FieldWindow => "India times HH:mm, start before end, not before 09:16 and not after 15:00.",
        _ => "only EQ_MIS.",
    };

    private Result<LimitsView> Refuse(Error error)
    {
        LimitsReading.Refused(logger, error.Code);
        return Result.Failure<LimitsView>(error);
    }
}
