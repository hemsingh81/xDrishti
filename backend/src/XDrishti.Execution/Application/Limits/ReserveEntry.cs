using System.Globalization;
using System.Security.Cryptography;
using XDrishti.Domain.Market;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Application.Limits;

public enum ReserveStatus
{
    /// <summary>Not an account the system sends for (mode, or a switch OFF or unreadable): nothing was written, nothing is routed.</summary>
    NotAuto,

    /// <summary>Refused with codes; one refusal row was written (when it could be).</summary>
    Refused,

    /// <summary>The ledger row and its action-log row were written in one transaction; the entry may be handed to the sender.</summary>
    Reserved,
}

/// <summary>The answer of a reservation. <see cref="RouteToTicket"/>: the only refusal was the strategy's first live use, which goes to a ticket as in <c>semi_auto</c>.</summary>
public sealed record ReserveOutcome(ReserveStatus Status, IReadOnlyList<string> Codes, long? LedgerId, long? ActionLogId, bool RouteToTicket)
{
    public static readonly ReserveOutcome NotAuto = new(ReserveStatus.NotAuto, [AutoLiveCodes.NotAuto], null, null, false);
}

/// <summary>
/// The reservation of one system-sent entry (spec P12-09 criteria 1 to 12), called by the submit path before anything is sent. It reads the account's mode and the two switches first; an account that is neither <c>auto_live</c> with both
/// switches ON nor <c>semi_auto</c> with execution ON gets <see cref="ReserveOutcome.NotAuto"/> and nothing else is read or written. Otherwise it reads the sheet in force with the database clock, the ledger day and (for <c>auto_live</c>) the
/// strategy's live use, the owner's presence and the feed; judges the entry (<see cref="AutoLiveGuard"/>); and either writes one refusal row with every code, or the ledger row with its action-log row in one transaction, which the database
/// judges again. A fact that cannot be read never passes. Composed by no host: no account can be <c>semi_auto</c> or <c>auto_live</c> (ADR 0163) and no sender is given to it.
/// </summary>
public sealed partial class ReserveEntry(
    IModeSource modes, IAutoLiveSettingsSource settings, ILimitsStore limits, ILedger ledger, IActionLog actions, IOwnerPresence owner, IFeedHealth feed, ILogger<ReserveEntry> logger)
{
    public async Task<ReserveOutcome> ReserveAsync(AutoEntry entry, EntryFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(facts);
        var account = Short(entry.AccountId);
        var mode = (await modes.AnswerAsync(entry.AccountId, cancellationToken).ConfigureAwait(false))?.EffectiveMode;
        var switches = await settings.ReadAsync(cancellationToken).ConfigureAwait(false);
        var tap = mode == ExecMode.SemiAuto;
        if (switches is null || !(tap ? switches.ExecutionEnabled : mode == ExecMode.AutoLive && switches is { ExecutionEnabled: true, AutoLiveEnabled: true }))
        {
            LogNotAuto(logger, account);
            return ReserveOutcome.NotAuto;
        }

        LimitsReading reading;
        try
        {
            reading = await limits.ReadAsync(entry.AccountId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return await RefuseAsync(entry, tap, [AutoLiveCodes.LedgerUnavailable], switches.SettingsId, null, cancellationToken).ConfigureAwait(false);
        }

        var now = reading.DbNow;
        var sheet = reading.InForce?.Limits;
        var day = await ledger.DayAsync(entry.AccountId, IndiaTime.Today(now), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> codes;
        if (tap)
        {
            codes = AutoLiveGuard.Caps(sheet, switches, entry, day, now);
        }
        else
        {
            var used = await ledger.StrategyUsedAsync(entry.AccountId, entry.StrategyHash, cancellationToken).ConfigureAwait(false);
            var seen = await owner.NewestAsync(cancellationToken).ConfigureAwait(false);
            var priced = await feed.LastPriceAtAsync(entry.Symbol, cancellationToken).ConfigureAwait(false);
            codes = AutoLiveGuard.Evaluate(mode, switches, entry, new AutoFacts(sheet, day, facts, used, seen, priced), now).Codes;
        }

        if (codes.Count > 0 || reading.InForce is not { } row || switches.SettingsId is not { } settingsId || day is null)
        {
            return await RefuseAsync(entry, tap, codes.Count > 0 ? codes : [AutoLiveCodes.LimitsIncomplete], switches.SettingsId, reading.InForce?.Id, cancellationToken).ConfigureAwait(false);
        }

        var orderCap = Math.Min(row.Limits.MaxOrdersPerDay!.Value, switches.GlobalMaxOrdersPerDay);
        var text = ActionLogText.Entry(entry, Ticket(entry), tap, day.Notional + entry.Notional, row.Limits.MaxNotionalPerDay!.Value, day.Orders + 1, orderCap);
        var action = new ActionLogEntry(entry.AccountId, tap ? ActionKinds.TapEntry : ActionKinds.AutoEntry, tap ? ActionKinds.ByTap : ActionKinds.ByAuto, entry.IntentPublicId, null, text, [], settingsId, row.Id);
        var reservation = new LedgerReservation(
            entry.AccountId, IndiaTime.Today(now), entry.IntentPublicId, entry.Symbol, entry.StrategyHash, entry.Product, entry.Side == ExecSide.Buy ? "buy" : "sell", entry.Quantity, entry.Notional, entry.RiskAtStop,
            tap ? "semi_auto" : "auto_live", row.Id, settingsId);
        var result = await ledger.ReserveAsync(reservation, action, cancellationToken).ConfigureAwait(false);
        if (!result.Reserved)
        {
            var code = result.Refused ?? AutoLiveCodes.LedgerUnavailable;
            LogDatabaseRefused(logger, account, code);
            return await RefuseAsync(entry, tap, [code], settingsId, row.Id, cancellationToken).ConfigureAwait(false);
        }

        LogReserved(logger, account, tap ? "semi_auto" : "auto_live", day.Orders + 1, orderCap);
        return new ReserveOutcome(ReserveStatus.Reserved, [], result.LedgerId, result.ActionLogId, false);
    }

    internal static string Ticket(AutoEntry entry) => entry.PlanItemId.ToString(CultureInfo.InvariantCulture);

    internal static string Short(Guid id) => Convert.ToHexStringLower(SHA256.HashData(id.ToByteArray()))[..8];

    private async Task<ReserveOutcome> RefuseAsync(AutoEntry entry, bool tap, IReadOnlyList<string> codes, long? settingsId, long? limitsId, CancellationToken cancellationToken)
    {
        var row = new ActionLogEntry(
            entry.AccountId, tap ? ActionKinds.TapRefused : ActionKinds.AutoRefused, tap ? ActionKinds.ByTap : ActionKinds.ByAuto, entry.IntentPublicId, null, ActionLogText.Refused(entry, Ticket(entry), codes), codes, settingsId, limitsId);
        var id = await actions.AppendAsync(row, cancellationToken).ConfigureAwait(false);
        var (who, what) = (Short(entry.AccountId), string.Join(',', codes));
        LogRefused(logger, who, what, id is not null);
        return new ReserveOutcome(ReserveStatus.Refused, codes, null, id, codes.Count == 1 && codes[0] == AutoLiveCodes.StrategyFirstUse);
    }

    [LoggerMessage(EventId = 6720, Level = LogLevel.Information, Message = "execution.entry_reserved account={Account} mode={Mode} order={Order} of={Cap}")]
    private static partial void LogReserved(ILogger logger, string account, string mode, int order, int cap);

    [LoggerMessage(EventId = 6721, Level = LogLevel.Information, Message = "execution.entry_refused account={Account} codes={Codes} logged={Logged}")]
    private static partial void LogRefused(ILogger logger, string account, string codes, bool logged);

    [LoggerMessage(EventId = 6722, Level = LogLevel.Warning, Message = "execution.entry_refused_by_database account={Account} code={Code}")]
    private static partial void LogDatabaseRefused(ILogger logger, string account, string code);

    [LoggerMessage(EventId = 6723, Level = LogLevel.Debug, Message = "execution.entry_not_auto account={Account}")]
    private static partial void LogNotAuto(ILogger logger, string account);
}
