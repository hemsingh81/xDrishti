namespace XDrishti.Application.Abstractions.Options;

/// <summary>The turnover ranking the default option list is built from, and the India date of the newest daily bar it used.</summary>
public sealed record RankedShares(IReadOnlyList<string> Symbols, DateOnly? NewestBar);
