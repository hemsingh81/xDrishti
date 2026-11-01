namespace XDrishti.Application.Abstractions.Options;

/// <summary>
/// What <see cref="IOptionContractStore.ApplyAsync"/> did: contracts inserted, contracts whose <c>last_seen_on</c> moved, contracts not inserted because another
/// security id already holds the same underlying, expiry, strike and side (<see cref="Conflicts"/>), security ids that now name a different contract than the
/// stored one (<see cref="ReusedIds"/>: a new expiry is stored as its own row, any other difference is neither stored nor advanced), seed rows and lot-change rows written.
/// </summary>
public sealed record OptionContractsApplied(int Inserted, int Advanced, int Conflicts, int ReusedIds, int SeedsWritten, int LotChanges);
