using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Registers a Dhan account (the add-account wizard): client id, the Dhan API key and secret (stored encrypted, never
/// returned), identity, roles and their settings — all or nothing.
/// </summary>
public sealed record CreateAccountCommand(CreateAccountInput Input, string Actor) : ICommand;

internal sealed class CreateAccountHandler(
    IAccountStore accounts,
    IRiskProfileStore riskProfiles,
    IBucketStore buckets,
    IBrokerSecretVault vault,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<CreateAccountHandler> logger) : ICommandHandler<CreateAccountCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var now = clock.GetUtcNow();
        var errors = new FieldErrors();

        var secretErrors = new FieldErrors();
        var secrets = AccountInputs.ParseSecrets(input.Secrets?.ApiKey, input.Secrets?.ApiSecret, required: true, secretErrors);
        errors.AddRange(secretErrors, "secrets");

        TradingSpec? tradingSpec = null;
        if (input.Trading is not null)
        {
            var local = new FieldErrors();
            tradingSpec = AccountInputs.ParseTrading(input.Trading, local);
            errors.AddRange(local, "trading");
        }

        var roles = AccountInputs.ParseRoles(input.Roles, errors);

        // Everything that can be checked without the database is checked now, so the owner sees all problems at once.
        var created = BrokerAccount.Create(input.Name, input.ClientId, input.ApiKeyIssuedOn, input.Color, input.Notes, AccountTime.TodayInIndia(now), now);
        if (created.IsFailure)
        {
            var failure = AccountInputs.Collect(created, errors);
            return Result.Failure<AccountDetail>(failure ?? AccountErrors.Invalid(errors));
        }

        var account = created.Value;
        var settingsFailure = await ApplySettingsAsync(account, input, tradingSpec, errors, now, cancellationToken);
        if (settingsFailure is not null)
        {
            return Result.Failure<AccountDetail>(settingsFailure);
        }

        if (errors.HasErrors)
        {
            return Result.Failure<AccountDetail>(AccountErrors.Invalid(errors));
        }

        if (await accounts.SlugExistsAsync(account.Slug, cancellationToken))
        {
            return Result.Failure<AccountDetail>(AccountErrors.Conflict("An account with a similar name already exists."));
        }

        var primary = await accounts.FindPrimaryDataAsync(cancellationToken);
        var standby = await accounts.FindStandbyDataAsync(cancellationToken);
        var rolesFailure = AccountInputs.Collect(account.ChangeRoles(roles!, primary, standby, now), errors);
        if (rolesFailure is not null)
        {
            return Result.Failure<AccountDetail>(rolesFailure);
        }

        if (errors.HasErrors)
        {
            return Result.Failure<AccountDetail>(AccountErrors.Invalid(errors));
        }

        accounts.Add(account);
        foreach (var (name, value) in secrets)
        {
            await vault.SetAsync(account.Id, name, value, now, cancellationToken);
        }

        events.Record(AccountEvent.Record(
            AccountEventType.AccountCreated,
            account.Id,
            $"Account '{account.Name}' created ({AccountCodes.Name(account.Broker)}); {AccountSummaries.SecretsSet(secrets.Select(s => s.Key))}; {AccountSummaries.Roles(account)}",
            command.Actor,
            now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.AccountCreated(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }

    /// <summary>Applies the optional trading, portfolio and data settings; field errors are collected, other failures are returned.</summary>
    private async ValueTask<Error?> ApplySettingsAsync(
        BrokerAccount account, CreateAccountInput input, TradingSpec? tradingSpec, FieldErrors errors, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (tradingSpec is not null)
        {
            if (tradingSpec.RiskProfileId is { } profileId && await riskProfiles.FindNameAsync(profileId, cancellationToken) is null)
            {
                errors.Add("trading.riskProfileId", "Unknown risk profile.");
            }
            else if (AccountInputs.Collect(account.ConfigureTrading(tradingSpec, now), errors, "trading") is { } trading)
            {
                return trading;
            }
        }

        if (input.Portfolio is not null)
        {
            var spec = AccountInputs.ParsePortfolio(input.Portfolio);
            var bucket = string.IsNullOrWhiteSpace(spec.DefaultBucket) ? null : await buckets.FindNameAsync(spec.DefaultBucket.Trim(), cancellationToken);
            if (!string.IsNullOrWhiteSpace(spec.DefaultBucket) && bucket is null)
            {
                errors.Add("portfolio.defaultBucket", "Unknown bucket.");
            }
            else if (AccountInputs.Collect(account.ConfigurePortfolio(spec with { DefaultBucket = bucket }, AccountTime.TodayInIndia(now), now), errors, "portfolio") is { } portfolio)
            {
                return portfolio;
            }
        }

        if (input.Data is not null && AccountInputs.Collect(account.ConfigureData(input.Data.LiveFeedBudget, now), errors, "data") is { } data)
        {
            return data;
        }

        return null;
    }
}
