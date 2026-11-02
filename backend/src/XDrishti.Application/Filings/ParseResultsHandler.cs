using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// The results parse step (spec P15-03 criterion 5; ADR 0184): every results envelope with no parse row for the current parser version is opened from the store folder, parsed by the hardened reader and stored in one transaction (the only writer of
/// <c>result_facts</c> and <c>result_parses</c>). A file that is not an XBRL file (any extension but <c>.xml</c> and <c>.xbrl</c>) is recorded <c>not_xbrl</c> and never opened. A file that cannot be opened (missing, or its hash changed) or a store failure is counted, logged by
/// code and left for the next run; it stops no other filing. Idempotent: a second run parses nothing. No network.
/// </summary>
internal sealed class ParseResultsHandler(IResultFactsStore store, IFilingFiles files, IResultsXbrlReader reader, TimeProvider clock, ILogger<ParseResultsHandler> logger) : ICommandHandler<ParseResultsCommand, ParseResultsSummary>
{
    public const int MaxPerRun = 5_000;

    public async ValueTask<Result<ParseResultsSummary>> HandleAsync(ParseResultsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var pending = await store.PendingAsync(ResultsParser.Version, MaxPerRun, cancellationToken);
        int parsed = 0, rejected = 0, notXbrl = 0, noFacts = 0, skipped = 0, failures = 0;
        foreach (var filing in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await ParseOneAsync(filing, cancellationToken);
                if (result is null)
                {
                    failures++;
                    continue;
                }

                if (!await store.SaveAsync(filing.Id, ResultsParser.Version, result, clock.GetUtcNow(), cancellationToken))
                {
                    skipped++;
                    continue;
                }

                ResultsParseLog.Parsed(logger, filing.Id, ResultsParser.Version, result.Outcome, result.Facts.Count);
                switch (result.Outcome)
                {
                    case ResultCodes.Parsed:
                        parsed++;
                        break;
                    case ResultCodes.Rejected:
                        rejected++;
                        ResultsParseLog.Rejected(logger, filing.Id, ResultsParser.Version, result.Reason ?? "unknown");
                        break;
                    case ResultCodes.NotXbrl:
                        notXbrl++;
                        break;
                    default:
                        noFacts++;
                        break;
                }

                if (result.Flags.Count > 0)
                {
                    var flags = string.Join(",", result.Flags.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}:{f.Value}"));
                    ResultsParseLog.Flags(logger, filing.Id, flags);
                }
            }
#pragma warning disable CA1031 // one filing that cannot be parsed or stored must not block the others: it is logged by type, counted and left for the next run
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                failures++;
                ResultsParseLog.Failed(logger, filing.Id, ex.GetType().Name);
            }
        }

        return Result.Success(new ParseResultsSummary(pending.Count, parsed, rejected, notXbrl, noFacts, skipped, failures, ResultsParser.Version));
    }

    // Null when the file could not be opened (a failure, retried next run).
    private async ValueTask<ParsedResults?> ParseOneAsync(PendingResultFiling filing, CancellationToken cancellationToken)
    {
        if (!IsXbrlName(filing.FileName))
        {
            return ParsedResults.NotXbrl();
        }

        if (filing.PeriodEnd is not { } periodEnd)
        {
            return ParsedResults.NoFactsBecause(ResultCodes.NoPeriod, new Dictionary<string, int>());
        }

        var opened = await files.OpenAsync(filing.ContentSha256, cancellationToken);
        if (opened.IsFailure)
        {
            ResultsParseLog.Failed(logger, filing.Id, opened.Error.Code);
            return null;
        }

        await using var stream = opened.Value;
        return await reader.ParseAsync(stream, periodEnd, cancellationToken);
    }

    private static bool IsXbrlName(string fileName) => fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".xbrl", StringComparison.OrdinalIgnoreCase);
}
