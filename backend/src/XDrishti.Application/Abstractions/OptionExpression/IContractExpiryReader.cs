using XDrishti.Domain.Options.Remark;

namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>The expiry dates <c>options.contracts</c> holds for one underlying in a date range, with their kind (weekly, monthly); contracts of an unknown kind are left out. Bounded by the underlying and the range; read only. The archive starts in 2026-11, so older expiries come from the derived rule.</summary>
public interface IContractExpiryReader
{
    ValueTask<IReadOnlyList<ContractExpiry>> ExpiriesAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
