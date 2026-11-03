using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A stored expression profile with the time it was stored.</summary>
/// <param name="Profile">The profile, rebuilt from the stored parameters and checked against its stored hash.</param>
/// <param name="CreatedAt">When the row was written.</param>
public sealed record StoredExpressionProfile(ExpressionProfile Profile, DateTimeOffset CreatedAt);
