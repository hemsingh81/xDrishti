namespace XDrishti.Domain.Research;

/// <summary>One instrument of the universe of a run: its id and symbol.</summary>
public sealed record UniverseMember(Guid Id, string Symbol);
