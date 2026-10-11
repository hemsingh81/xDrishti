using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Replaces the account's API key and/or secret in the vault (write-only) and the day the key was generated. The audit trail names what was replaced, never a value.
/// </summary>
public sealed record UpdateSecretsCommand(Guid AccountId, UpdateSecretsInput Input, string Actor) : ICommand;

internal sealed class UpdateSecretsHandler(
    IAccountStore accounts,
    IBrokerSecretVault vault,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdateSecretsHandler> logger) : ICommandHandler<UpdateSecretsCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdateSecretsCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.NotFound);
        }

        var input = command.Input;
        if (input.ApiKey is null && input.ApiSecret is null && input.ApiKeyIssuedOn is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.Invalid("apiKey", "Give a new API key, API secret or key date."));
        }

        var errors = new FieldErrors();
        var secrets = AccountInputs.ParseSecrets(input.ApiKey, input.ApiSecret, required: false, errors);
        var now = clock.GetUtcNow();
        var today = AccountTime.TodayInIndia(now);
        var issuedBefore = account.ApiKeyIssuedOn;

        // A new key was generated today unless the owner says otherwise.
        var newKey = secrets.Exists(s => s.Key == BrokerSecretName.ApiKey);
        var issuedOn = input.ApiKeyIssuedOn ?? (newKey ? today : null);
        if (issuedOn is { } day && AccountInputs.Collect(account.ChangeApiKeyIssuedOn(day, today, now), errors) is { } failure)
        {
            return Result.Failure<AccountDetail>(failure);
        }

        if (errors.HasErrors)
        {
            return Result.Failure<AccountDetail>(AccountErrors.Invalid(errors));
        }

        foreach (var (name, value) in secrets)
        {
            await vault.SetAsync(account.Id, name, value, now, cancellationToken);
        }

        if (secrets.Count > 0)
        {
            account.SecretsReplaced(now);
        }

        events.Record(AccountEvent.Record(AccountEventType.SecretsReplaced, account.Id, Describe(secrets.ConvertAll(s => s.Key), issuedBefore, account.ApiKeyIssuedOn), command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.SecretsReplaced(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }

    /// <summary>For example <c>API key replaced; API key issued on 2026-10-11</c> — names and dates only, never a value.</summary>
    private static string Describe(List<BrokerSecretName> replaced, DateOnly issuedBefore, DateOnly issuedAfter)
    {
        var parts = new List<string>(2);
        if (replaced.Count > 0)
        {
            parts.Add(AccountSummaries.SecretsReplaced(replaced));
        }

        if (issuedAfter != issuedBefore)
        {
            parts.Add("API key issued on " + issuedAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? "Secrets saved without changes" : string.Join("; ", parts);
    }
}
