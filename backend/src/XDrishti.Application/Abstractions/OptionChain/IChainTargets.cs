namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>An underlying the recorder asks about: its instrument, its canonical symbol, the Dhan underlying id and segment, and whether it is an index.</summary>
public sealed record ChainTarget(Guid InstrumentId, string Symbol, long Scrip, string Segment, bool IsIndex);

/// <summary>The underlyings of a day: the indices of the expiry rules and the other listed underlyings (shares).</summary>
public sealed record ChainDay(DateOnly Date, IReadOnlyList<ChainTarget> Indices, IReadOnlyList<ChainTarget> Stocks)
{
    public bool IsEmpty => Indices.Count == 0 && Stocks.Count == 0;
}

/// <summary>The reference data of the recorder: the app setting <c>options.underlyings</c> resolved to Dhan ids from the master list. Reads no chain row.</summary>
public interface IChainTargets
{
    ValueTask<IReadOnlyList<ChainTarget>> LoadAsync(CancellationToken cancellationToken);
}
