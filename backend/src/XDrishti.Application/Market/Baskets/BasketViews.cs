using XDrishti.Application.Market.Search;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// A basket. <c>purpose</c>: trading · long_term · watch. <c>position</c> = place among the active baskets (0 first; the system baskets count on their own, in their group).
/// <c>memberCount</c> = instruments in it now. <c>archivedAt</c> is null for an active basket (archived ones are only listed on request).
/// <c>kind</c>: manual (the owner's) · portfolio (an account's portfolio bucket, ADR 0014); <c>system</c> = true for the latter: read-only, never archived, its members are the
/// bucket's current holdings. <c>accountId</c>/<c>accountName</c> are set for the default bucket of an account.
/// </summary>
public sealed record BasketView(
    Guid Id, string Name, string Colour, string Purpose, string? Description, int Position, int MemberCount, DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt,
    string Kind, bool System, Guid? AccountId, string? AccountName);

/// <summary>
/// One stay of an instrument in a basket: <c>validFrom</c> = when it was added, <c>validTo</c> = when it was removed (null while it is a member),
/// <c>position</c> = place among the members (0 first), <c>addedBy</c> = the owner's address.
/// In a system (portfolio) basket a member is the instrument of the bucket's holdings: <c>id</c> = the (first) holding, <c>validFrom</c> = when the holding was first seen,
/// <c>addedBy</c> = "portfolio", <c>quantity</c> = units held (summed over the bucket's holdings of it) and <c>accountName</c> = the account(s) holding it; both are null in a manual basket.
/// </summary>
public sealed record BasketMemberView(
    Guid Id, InstrumentSummary Instrument, int Position, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo, string? Note, string AddedBy, long? Quantity, string? AccountName);

/// <summary>
/// The members of a basket, in their order. <c>asOf</c> is null for the current members, otherwise the India date they are as of (members at the end of that
/// day; removed ones show their <c>validTo</c>). <c>total</c> counts the items.
/// </summary>
public sealed record BasketMembers(Guid BasketId, string Name, DateTimeOffset? ArchivedAt, DateOnly? AsOf, int Total, IReadOnlyList<BasketMemberView> Items);

/// <summary>An addition (<c>kind</c> = added, <c>by</c> = who added it) or removal (<c>kind</c> = removed, <c>by</c> = who removed it) of an instrument. Only additions carry a note (the one given when adding); a removal has none.</summary>
public sealed record BasketHistoryEntry(
    Guid MemberId, string Kind, DateTimeOffset At, Guid InstrumentId, string Symbol, string Name, string Type, string? Note, string? By);

/// <summary>The newest additions and removals of a basket, newest first.</summary>
public sealed record BasketHistory(Guid BasketId, string Name, IReadOnlyList<BasketHistoryEntry> Items);

/// <summary>A basket that holds an instrument now; <c>since</c> = when it was added.</summary>
public sealed record InstrumentBasketView(Guid BasketId, string Name, string Colour, string Purpose, int Position, DateTimeOffset Since);

/// <summary>Attributes of a basket (create and replace). Missing colour = the default; missing purpose = trading; blank description = none.</summary>
public sealed record BasketInput(string? Name, string? Colour, string? Purpose, string? Description);

/// <summary>Every active basket exactly once, in the wanted order.</summary>
public sealed record BasketOrderInput(IReadOnlyList<Guid>? Ids);

/// <summary>Instruments to add (1 to 500; repeats count once) with an optional note stored on each new membership.</summary>
public sealed record AddMembersInput(IReadOnlyList<Guid>? InstrumentIds, string? Note);

/// <summary>Instruments to remove from a basket (1 to 500; repeats count once).</summary>
public sealed record RemoveMembersInput(IReadOnlyList<Guid>? InstrumentIds);

/// <summary>Every member of the basket exactly once, in the wanted order.</summary>
public sealed record MembersOrderInput(IReadOnlyList<Guid>? InstrumentIds);

/// <summary>Instruments (1 to 500) to copy or move from one basket to another; <c>mode</c> = copy · move.</summary>
public sealed record TransferInput(Guid FromBasketId, Guid ToBasketId, IReadOnlyList<Guid>? InstrumentIds, string? Mode);

/// <summary>
/// What an add did (counts of distinct instruments): <c>added</c> became members, <c>alreadyMembers</c> were members already, <c>newlyTracked</c> of the added
/// ones were not tracked and are now. <c>memberCount</c> = members now.
/// </summary>
public sealed record AddMembersResult(int Requested, int Added, int AlreadyMembers, int NewlyTracked, int MemberCount);

/// <summary>What a removal did: <c>removed</c> were members, <c>notMembers</c> were not. Untracking never happens. <c>memberCount</c> = members now.</summary>
public sealed record RemoveMembersResult(int Requested, int Removed, int NotMembers, int MemberCount);

/// <summary>
/// What a transfer did. Only instruments that are members of the source are transferred: the others are <c>notInSource</c> (for a repeated move, the ones
/// moved before). <c>added</c> became members of the target, <c>alreadyInTarget</c> were there already, <c>removed</c> left the source (a move only),
/// <c>newlyTracked</c> of the added ones were not tracked and are now.
/// </summary>
public sealed record TransferResult(
    string Mode, int Requested, int Added, int AlreadyInTarget, int Removed, int NotInSource, int NewlyTracked, int SourceMemberCount, int TargetMemberCount);
