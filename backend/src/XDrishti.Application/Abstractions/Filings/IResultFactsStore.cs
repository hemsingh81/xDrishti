using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The only writer of <c>filings.result_facts</c> and <c>filings.result_parses</c> (P15-03, insert-only). Named only by the parse use case.</summary>
public interface IResultFactsStore
{
    /// <summary>The results envelopes (state ok or unavailable) with no parse row for <paramref name="parserVersion"/>, oldest first.</summary>
    ValueTask<IReadOnlyList<PendingResultFiling>> PendingAsync(string parserVersion, int limit, CancellationToken cancellationToken);

    /// <summary>The parse row and its facts in one transaction (<c>ON CONFLICT DO NOTHING</c>). False when a row was already there.</summary>
    ValueTask<bool> SaveAsync(Guid filingId, string parserVersion, ParsedResults parsed, DateTimeOffset at, CancellationToken cancellationToken);
}
