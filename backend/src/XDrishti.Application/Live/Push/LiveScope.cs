namespace XDrishti.Application.Live.Push;

/// <summary>
/// What a Portfolio screen asks the hub to follow (argument of `Subscribe`, ADR 0024 §7): the prices of the instruments of these holdings are pushed, nothing else.
/// `accountId` and `bucketId` are the screen's scope, with the meaning they have for `GET /api/holdings/summary`: without `accountId` every open holding of the accounts that count in totals, with it every open holding of that account
/// (any account that is not archived); `bucketId` narrows either to one bucket. This is the set the tiles and the group rows are made of.
/// `holdingIds` are the holdings the screen shows (rows of the grid, the open drawer); they are added to the scope's holdings even when the scope would not include them (an account that does not count in totals, a filter applied on the screen only).
/// At most 1000 are read; holdings that are closed or have no instrument are ignored. Leave `accountId`, `bucketId` and `holdingIds` null or empty for all accounts that count in totals.
/// The server turns holdings into instruments; the messages carry instrument ids (as `instrumentId` of the holdings list). A second `Subscribe` replaces the first.
/// </summary>
public sealed record LiveScope(Guid? AccountId, Guid? BucketId, IReadOnlyList<Guid>? HoldingIds);
