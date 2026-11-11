using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The performance profile of one basket (BK-07). All fields are the raw text of the request and are checked by the handler.</summary>
/// <param name="Id">The basket id.</param>
/// <param name="From">First India date, yyyy-MM-dd; default the last sessions ending at the newest stored daily bar of the index.</param>
/// <param name="To">Last India date, yyyy-MM-dd; default the newest stored daily bar of the index.</param>
/// <param name="Index">The index instrument id; default the owner's benchmark.</param>
/// <param name="MembersAsOf">India date of the member list, yyyy-MM-dd; default the window end.</param>
public sealed record GetBasketProfileQuery(string? Id, string? From, string? To, string? Index, string? MembersAsOf) : IQuery<Result<BasketProfileView>>;
