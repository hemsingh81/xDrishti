namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>The local folder the owner drops NSE daily CSVs of NIFTY 50 and INDIA VIX into (P3-01d fallback D). Local files only; no network.</summary>
public interface IContextInbox
{
    /// <summary>Whether the loader is switched on (<c>Data:Context:InboxEnabled</c>, default true).</summary>
    bool Enabled { get; }

    /// <summary>The names of the regular .csv files waiting at the top of the folder, in name order; a missing folder is an empty list.</summary>
    ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The text of a listed file (strict UTF-8), or null when it cannot be read (gone, too big, not UTF-8).</summary>
    ValueTask<string?> ReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>Moves a handled file to <c>done/</c> or <c>failed/</c> next to the inbox, never overwriting.</summary>
    ValueTask MoveAsync(string name, bool processed, CancellationToken cancellationToken);
}
