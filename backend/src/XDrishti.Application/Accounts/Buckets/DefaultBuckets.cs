using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.Buckets;

/// <summary>
/// Keeps the default bucket of every account that has the Portfolio role: created with the account (or when the role is switched on),
/// renamed with the account, archived with the account. Everything is collected in the caller's unit of work. The unique link between
/// bucket and account is what makes creation safe against concurrent requests: the loser's save is refused and it starts over, finding the bucket.
/// </summary>
internal sealed class DefaultBuckets(IBucketStore buckets, IAccountEventLog events)
{
    /// <summary>
    /// Makes sure an account with the Portfolio role has its default bucket (nothing happens for an account without the role or one that has the bucket
    /// already). The result says whether a bucket was added to the unit of work.
    /// </summary>
    public async ValueTask<Result<bool>> EnsureAsync(BrokerAccount account, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!account.Portfolio.Enabled || await buckets.FindDefaultForAccountAsync(account.Id, cancellationToken) is not null)
        {
            return Result.Success(false);
        }

        var name = await FreeNameAsync(account.Name, null, cancellationToken);
        if (name is null)
        {
            return Result.Failure<bool>(AccountErrors.Conflict("Too many buckets have a similar name. Rename the account."));
        }

        var created = PortfolioBucket.CreateDefault(account.Id, name, now);
        if (created.IsFailure)
        {
            return Result.Failure<bool>(created.Error);
        }

        buckets.Add(created.Value);
        events.Record(AccountEvent.Record(AccountEventType.BucketCreated, account.Id, $"Default portfolio bucket '{name}' created", actor, now));
        return Result.Success(true);
    }

    /// <summary>Gives the default bucket of the account (if it has one) the name that fits the account's current name.</summary>
    public async ValueTask<Result> FollowRenameAsync(BrokerAccount account, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bucket = await buckets.FindDefaultForAccountAsync(account.Id, cancellationToken);
        if (bucket is null)
        {
            return Result.Success();
        }

        var wanted = PortfolioBucket.DefaultNameFor(account.Name);
        if (bucket.Name == wanted)
        {
            return Result.Success();
        }

        var name = await FreeNameAsync(account.Name, bucket.Id, cancellationToken);
        if (name is null)
        {
            return Result.Failure(AccountErrors.Conflict("Too many buckets have a similar name. Choose another account name."));
        }

        if (name == bucket.Name)
        {
            return Result.Success();
        }

        var old = bucket.Name;
        var renamed = bucket.Rename(name);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        events.Record(AccountEvent.Record(AccountEventType.BucketRenamed, account.Id, $"Default portfolio bucket '{old}' renamed to '{name}'", actor, now));
        return Result.Success();
    }

    /// <summary>
    /// Archives the default bucket of an account that is being archived (its holdings and history stay) and renames it
    /// ("… · Portfolio (archived)"), so that its name is free for a new account.
    /// </summary>
    public async ValueTask ArchiveAsync(BrokerAccount account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bucket = await buckets.FindDefaultForAccountAsync(account.Id, cancellationToken);
        if (bucket is null || bucket.ArchivedAt is not null)
        {
            return;
        }

        var taken = new HashSet<string>(await buckets.NamesStartingWithAsync(bucket.ArchivedBaseName(), bucket.Id, cancellationToken), StringComparer.OrdinalIgnoreCase);
        bucket.Archive(now, taken.Contains);
    }

    /// <summary>
    /// Checks the default bucket chosen for imports: blank or the account's own default bucket means "its own" (null is returned); otherwise it must be a
    /// custom bucket, which is returned. Anything else adds a field error under <paramref name="field"/> (null is returned then, too).
    /// </summary>
    public async ValueTask<PortfolioBucket?> ResolveChoiceAsync(
        BrokerAccount account, string? selected, FieldErrors errors, string field, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selected))
        {
            return null;
        }

        var name = selected.Trim();
        var own = await buckets.FindDefaultForAccountAsync(account.Id, cancellationToken);
        if (string.Equals(name, own?.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, PortfolioBucket.DefaultNameFor(account.Name), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var found = await buckets.FindByNameAsync(name, cancellationToken);
        if (found is null || found.ArchivedAt is not null)
        {
            errors.Add(field, "Unknown bucket.");
            return null;
        }

        if (found.IsAccountDefault)
        {
            errors.Add(field, "Choose this account's own bucket or a custom bucket.");
            return null;
        }

        return found;
    }

    /// <summary>The name of a custom bucket by id; null when there is none.</summary>
    public async ValueTask<string?> NameOfAsync(Guid? bucketId, CancellationToken cancellationToken) =>
        bucketId is { } id ? (await buckets.NamesAsync([id], cancellationToken)).GetValueOrDefault(id) : null;

    private async ValueTask<string?> FreeNameAsync(string accountName, Guid? exceptBucketId, CancellationToken cancellationToken) =>
        PortfolioBucket.FreeDefaultName(accountName, await buckets.NamesStartingWithAsync(PortfolioBucket.DefaultNameFor(accountName), exceptBucketId, cancellationToken));
}
