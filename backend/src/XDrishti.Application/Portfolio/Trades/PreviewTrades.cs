using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Reads a tradebook (CSV) against an account and says what importing it would do — and stores and changes nothing (ADR 0023): the file's rows, what was skipped (not NSE equity) or bad (line and reason), the trades already stored,
/// and for each security whether the account holds it and what rebuilding the holding's lots from all its trades (stored and new) would do: lots before and after, hand-dated and sync-added lots replaced, realised profit on the real
/// sales, the corporate actions the trades were adjusted for, the broker's quantity against the trades, and the status (<c>ok</c>, <c>unchanged</c>, <c>quantity_mismatch</c>, <c>no_holding</c>, <c>ambiguous</c>).
/// <c>Digest</c> fingerprints all of it; an import with rebuild must send it back. The account must have the Portfolio role on (Dhan need not be connected). A file that cannot be read is <c>portfolio.invalid</c> on field <c>file</c>.
/// </summary>
public sealed record PreviewTradesQuery(Guid AccountId, string FileName, TradeFileContent Content) : IQuery<Result<TradePreviewReport>>;

internal sealed class PreviewTradesHandler(TradeWorkspace workspace, TimeProvider clock, ILogger<PreviewTradesHandler> logger)
    : IQueryHandler<PreviewTradesQuery, Result<TradePreviewReport>>
{
    public async ValueTask<Result<TradePreviewReport>> HandleAsync(PreviewTradesQuery query, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var account = await workspace.AccountAsync(query.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<TradePreviewReport>(account.Error);
        }

        if (query.Content.Length > TradeCsvLimits.MaxBytes)
        {
            return Result.Failure<TradePreviewReport>(PortfolioErrors.Invalid("file", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB."));
        }

        var now = clock.GetUtcNow();
        Result<TradeFile> read;
        await using (var stream = query.Content.Open())
        {
            read = await TradeCsvReader.ReadAsync(stream, XDrishti.Domain.Market.IndiaTime.Today(now), cancellationToken);
        }

        if (read.IsFailure)
        {
            return Result.Failure<TradePreviewReport>(read.Error);
        }

        var identified = TradeIdentity.Identify(account.Value.Id, read.Value.Trades);
        var plan = await workspace.PlanAsync(account.Value, identified, null, "preview", now, cancellationToken);
        var report = TradeViews.Report(query.FileName, read.Value, plan.Plan, plan.AlreadyImported, plan.Items.Count - plan.AlreadyImported);
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        TradesLog.Previewed(logger, account.Value.Slug, read.Value.Rows, read.Value.Parsed, read.Value.Skipped, read.Value.BadTotal, report.Securities.Count, elapsedMs);
        return Result.Success(report);
    }
}
