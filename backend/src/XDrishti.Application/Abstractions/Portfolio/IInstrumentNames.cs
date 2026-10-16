namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>An NSE share or ETF of the instrument master with the names a dividend file may use for it: its id, ticker (without <c>NSE:</c>), ISIN, name and display name, and whether the master has retired it.</summary>
public sealed record ShareName(Guid Id, string Symbol, string? Isin, string Name, string DisplayName, bool Retired);

/// <summary>The names of the instrument master's NSE shares and ETFs, for matching the scrip names of a dividend file (reads only).</summary>
public interface IInstrumentNames
{
    /// <summary>Every NSE share and ETF of the master, retired ones included, in one read.</summary>
    ValueTask<IReadOnlyList<ShareName>> ListSharesAsync(CancellationToken cancellationToken);
}
