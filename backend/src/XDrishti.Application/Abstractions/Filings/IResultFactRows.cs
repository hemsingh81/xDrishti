using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Reads stored parse rows and facts (P15-03). SELECT only; never a file, a path or a source reference.</summary>
public interface IResultFactRows
{
    /// <summary>The results envelopes of the instrument with a broadcast time at or before <paramref name="cutoff"/> (and those with none, to be counted), each with its facts of <paramref name="parserVersion"/>.</summary>
    ValueTask<IReadOnlyList<ResultFilingFacts>> ForInstrumentAsync(Guid instrumentId, DateTimeOffset cutoff, string parserVersion, CancellationToken cancellationToken);

    ValueTask<StoredParse?> ParseOfAsync(Guid filingId, string parserVersion, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<StoredResultFact>> FactsOfAsync(Guid filingId, string parserVersion, CancellationToken cancellationToken);
}
