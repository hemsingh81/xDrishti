namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Reads one stored results file with the hardened XML reader and the pure parser (P15-03). Never opens a network connection; a file that cannot be read safely is a <c>rejected</c> result with a closed reason, not an exception.</summary>
public interface IResultsXbrlReader
{
    ValueTask<Domain.Filings.ParsedResults> ParseAsync(Stream file, DateOnly periodEnd, CancellationToken cancellationToken);
}
