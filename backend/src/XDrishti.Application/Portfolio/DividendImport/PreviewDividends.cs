using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Reads a dividend file (CSV) against an account and says what importing it would do — and stores and changes nothing (ADR 0028): the rows, the bad ones (line and reason), the payments repeated identically in the file (counted once), what each distinct
/// row would become (new, stored already, a possible duplicate of a dividend typed in, needing a match, ambiguous) with the holding or instrument its scrip name was matched to, the paid amounts that differ from per share × quantity, the dividends dated before
/// the account's first lot, the totals to add by year, and a digest that fingerprints it all. The account must have the Portfolio role on. A file that cannot be read is <c>portfolio.invalid</c> on field <c>file</c>; a tradebook is
/// <c>portfolio.wrong_file_kind</c> (<c>detected</c> <c>trades</c> or <c>tradebook_without_sides</c>).
/// </summary>
public sealed record PreviewDividendsQuery(Guid AccountId, string FileName, TradeFileContent Content) : IQuery<Result<DividendPreviewReport>>;

internal sealed class PreviewDividendsHandler(DividendWorkspace workspace, TimeProvider clock, ILogger<PreviewDividendsHandler> logger)
    : IQueryHandler<PreviewDividendsQuery, Result<DividendPreviewReport>>
{
    public async ValueTask<Result<DividendPreviewReport>> HandleAsync(PreviewDividendsQuery query, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var account = await workspace.AccountAsync(query.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<DividendPreviewReport>(account.Error);
        }

        if (query.Content.Length > DividendCsvLimits.MaxBytes)
        {
            return Result.Failure<DividendPreviewReport>(PortfolioErrors.Invalid("file", $"The file is larger than {DividendCsvLimits.MaxBytes / (1024 * 1024)} MB."));
        }

        Result<DividendFile> read;
        await using (var stream = query.Content.Open())
        {
            read = await DividendCsvReader.ReadAsync(stream, IndiaTime.Today(clock.GetUtcNow()), cancellationToken);
        }

        if (read.IsFailure)
        {
            return Result.Failure<DividendPreviewReport>(read.Error);
        }

        var plan = await workspace.PlanAsync(account.Value, read.Value, cancellationToken);
        var report = DividendImportViews.Report(query.FileName, plan);
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        DividendImportLog.Previewed(logger, account.Value.Slug, read.Value.Rows, read.Value.Parsed, read.Value.BadTotal, report.Totals.Count, elapsedMs);
        return Result.Success(report);
    }
}
