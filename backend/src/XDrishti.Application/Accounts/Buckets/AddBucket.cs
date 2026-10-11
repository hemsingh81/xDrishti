using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts.Buckets;

/// <summary>Adds a custom portfolio bucket (each account's default bucket is created automatically, see <see cref="DefaultBuckets"/>).</summary>
public sealed record AddBucketCommand(BucketInput Input, string Actor) : ICommand;

internal sealed class AddBucketHandler(
    IBucketStore buckets,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AddBucketHandler> logger) : ICommandHandler<AddBucketCommand, BucketView>
{
    public async ValueTask<Result<BucketView>> HandleAsync(AddBucketCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var created = PortfolioBucket.Create(command.Input.Name, now);
        if (created.IsFailure)
        {
            return Result.Failure<BucketView>(created.Error);
        }

        var bucket = created.Value;
        if (await buckets.NameTakenAsync(bucket.Name, null, cancellationToken))
        {
            return Result.Failure<BucketView>(AccountErrors.Conflict($"A bucket named '{bucket.Name}' already exists."));
        }

        buckets.Add(bucket);
        events.Record(AccountEvent.Record(AccountEventType.BucketAdded, null, $"Portfolio bucket '{bucket.Name}' added", command.Actor, now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, "a portfolio bucket");
            return Result.Failure<BucketView>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.BucketAdded(logger, bucket.Name);
        return Result.Success(new BucketView(bucket.Id, bucket.Name, false, null, null));
    }
}
