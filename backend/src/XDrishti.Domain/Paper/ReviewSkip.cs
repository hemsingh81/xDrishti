namespace XDrishti.Domain.Paper;

/// <summary>A stored skip: an armed allocation that produced no entry, with its code from <see cref="SkipCodes"/> (spec P10-03).</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="AccountId">The paper account.</param>
/// <param name="Code">The skip code.</param>
public sealed record ReviewSkip(long ItemId, Guid AccountId, string Code);
