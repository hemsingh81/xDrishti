using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Insert-only audit trail of the accounts module. New entries are saved by <see cref="IAccountUnitOfWork"/> together with the change they describe.</summary>
public interface IAccountEventLog
{
    void Record(AccountEvent accountEvent);

    /// <summary>Newest first.</summary>
    ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken);
}
