using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Accounts;

/// <summary>Replaces the trading configuration of an account (mode, risk profile, capital, styles, instruments, overrides).</summary>
public sealed record UpdateTradingCommand(Guid AccountId, TradingInput Input, string Actor) : ICommand;

internal sealed class UpdateTradingHandler(
    IAccountStore accounts,
    IRiskProfileStore riskProfiles,
    IBrokerConnectionStore connections,
    ILiveReadinessSource readiness,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdateTradingHandler> logger) : ICommandHandler<UpdateTradingCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdateTradingCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.NotFound);
        }

        var errors = new FieldErrors();
        var spec = AccountInputs.ParseTrading(command.Input, errors);
        if (spec is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.Invalid(errors));
        }

        var profileName = spec.RiskProfileId is { } profileId ? await riskProfiles.FindNameAsync(profileId, cancellationToken) : null;
        if (spec.RiskProfileId is not null && profileName is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.Invalid("riskProfileId", "Unknown risk profile."));
        }

        var now = clock.GetUtcNow();
        string? liveNote = null;
        if (spec.Mode == TradingMode.ManualLive && account.Trading.Mode != TradingMode.ManualLive)
        {
            var gate = await CheckLiveReadyAsync(account, spec, command.Input.LiveConfirmReason, now, cancellationToken);
            if (gate.Error is { } refused)
            {
                return Result.Failure<AccountDetail>(refused);
            }

            liveNote = gate.Note;
        }

        var configured = account.ConfigureTrading(spec, now);
        if (configured.IsFailure)
        {
            return Result.Failure<AccountDetail>(configured.Error);
        }

        events.Record(AccountEvent.Record(AccountEventType.TradingUpdated, account.Id, Describe(spec, profileName, account.Trading.Enabled) + liveNote, command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.TradingUpdated(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }

    // The live-ready gate of the switch to manual_live (spec P11-04): a failing hard item refuses; a failing soft item needs the typed reason, which goes into the audit text.
    private async ValueTask<(Error? Error, string? Note)> CheckLiveReadyAsync(BrokerAccount account, TradingSpec spec, string? confirmReason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var source = await readiness.ReadAsync(account.Id, cancellationToken);
        var connection = await connections.FindAsync(account.Id, cancellationToken);
        var result = LiveReadiness.Evaluate(new LiveReadinessFacts(
            account.Trading.Enabled, TradingSettings.IsComplete(spec), spec.CapitalSource == CapitalSource.Fixed && spec.CapitalAmount is not null,
            connection?.StatusAt(now) ?? ConnectionStatus.NotConnected, source.StepSet, source.Capture, source.Settle, source.PaperPhaseMet, source.ChargeSheetConfirmed, false));
        var reason = confirmReason?.Trim();
        var failing = new List<ReadinessItem>(result.FailingHard);
        var softOpen = result.FailingSoft;
        var reasonOk = !string.IsNullOrEmpty(reason) && reason.Length <= 300;
        if (!reasonOk)
        {
            failing.AddRange(softOpen);
        }

        if (failing.Count == 0)
        {
            return (null, softOpen.Count == 0 ? "; live-ready gate passed" : $"; live-ready gate passed with a typed reason for: {string.Join(", ", softOpen.Select(i => i.Code))}: {reason}");
        }

        var fields = new FieldErrors();
        foreach (var item in failing)
        {
            fields.Add("trading.mode", $"{item.Code}: {item.Detail}{(item.Hard ? string.Empty : " (needs liveConfirmReason, 1 to 300 characters)")}");
        }

        LiveReadyLog.NotReady(logger, string.Join(",", failing.Select(i => i.Code)));
        return (fields.ToError("accounts.live_not_ready", "The account is not ready for manual_live."), null);
    }

    private static string Describe(TradingSpec spec, string? profileName, bool roleOn)
    {
        var styles = string.Join('+', new[] { spec.Intraday ? AccountCodes.Intraday : null, spec.Swing ? AccountCodes.Swing : null }.Where(s => s is not null));
        var capital = spec.CapitalSource == CapitalSource.Fixed
            ? $"fixed {spec.CapitalAmount?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-"}"
            : "broker funds";
        var overrides = spec.Overrides == TradingOverrides.None ? "no overrides" : "with overrides";
        return $"Trading ({(roleOn ? "role on" : "role off")}): mode {AccountCodes.Name(spec.Mode)}, profile {profileName ?? "none"}, capital {capital}, styles {(styles.Length == 0 ? "none" : styles)}, {overrides}";
    }
}
