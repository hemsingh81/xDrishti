namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The exchange symbols of the master list that filings may be filed under: exact match on the NSE ticker among the tracked (not retired) shares and ETFs. Never a guess, never a fuzzy match.</summary>
public interface IFilingInstruments
{
    /// <summary>Finds the instrument of a ticker such as <c>RELIANCE</c> or a canonical symbol such as <c>NSE:RELIANCE</c>.</summary>
    ValueTask<InstrumentMatch> ResolveAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>All tracked instruments, ordered by ticker (P15-04: the instruments without a calendar entry are listed, never defaulted).</summary>
    ValueTask<IReadOnlyList<TrackedInstrument>> TrackedAsync(CancellationToken cancellationToken);
}

/// <summary>The result of <see cref="IFilingInstruments.ResolveAsync"/>: one instrument, none or more than one.</summary>
public sealed record InstrumentMatch(Guid? Id, bool Ambiguous)
{
    public static readonly InstrumentMatch Unknown = new(null, false);

    public static readonly InstrumentMatch Many = new(null, true);

    public static InstrumentMatch One(Guid id) => new(id, false);
}

/// <summary>A tracked instrument: its id and ticker (<c>RELIANCE</c>).</summary>
public sealed record TrackedInstrument(Guid Id, string Symbol);
