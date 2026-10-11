using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>Replaces the trading configuration of an account (mode, risk profile, capital, styles, instruments, overrides).</summary>
public sealed record UpdateTradingCommand(Guid AccountId, TradingInput Input, string Actor) : ICommand;

internal sealed class UpdateTradingHandler(
    IAccountStore accounts,
    IRiskProfileStore riskProfiles,
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
        var configured = account.ConfigureTrading(spec, now);
        if (configured.IsFailure)
        {
            return Result.Failure<AccountDetail>(configured.Error);
        }

        events.Record(AccountEvent.Record(AccountEventType.TradingUpdated, account.Id, Describe(spec, profileName, account.Trading.Enabled), command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.TradingUpdated(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
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
