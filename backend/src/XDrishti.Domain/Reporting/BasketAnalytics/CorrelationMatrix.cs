namespace XDrishti.Domain.Reporting;

/// <summary>The pairwise correlations of some members of a basket (BK-07), in the order of <paramref name="Symbols"/>; the diagonal is 1 and a pair without enough common sessions or without variance is null.</summary>
/// <param name="Symbols">The members, in the order the caller chose.</param>
/// <param name="Rho">Row by column, symmetric.</param>
public sealed record CorrelationMatrix(IReadOnlyList<string> Symbols, IReadOnlyList<IReadOnlyList<double?>> Rho);
