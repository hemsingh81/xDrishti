using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// "Track the benchmark" (ADR 0020 §2): tracks the benchmark index through the bulk tracking command (idempotent). The index is fetched like any tracked instrument, by the next <i>Update data</i> (no job is started here, and the answer says so).
/// <c>portfolio.not_found</c> when the master list has no benchmark index; a retired index is <c>portfolio.invalid</c>.
/// </summary>
public sealed record TrackBenchmarkCommand(string Actor) : ICommand;

internal sealed class TrackBenchmarkHandler(
    IPortfolioBenchmark benchmarks,
    ICommandHandler<TrackInstrumentsCommand, TrackResult> track,
    ILogger<TrackBenchmarkHandler> logger) : ICommandHandler<TrackBenchmarkCommand, PortfolioTrackResult>
{
    public const string AlreadyTrackedHint = "The benchmark is tracked already. Run Update data if its price history is missing.";

    public async ValueTask<Result<PortfolioTrackResult>> HandleAsync(TrackBenchmarkCommand command, CancellationToken cancellationToken)
    {
        if (await benchmarks.GetAsync(cancellationToken) is not { } configured)
        {
            return Result.Failure<PortfolioTrackResult>(PortfolioErrors.BenchmarkNotFound);
        }

        if (configured.Instrument.Retired)
        {
            return Result.Failure<PortfolioTrackResult>(PortfolioErrors.Invalid("instrumentId", "The benchmark index is no longer listed. Choose another index."));
        }

        var result = await track.HandleAsync(new TrackInstrumentsCommand(new TrackingInput([configured.Instrument.Id]), command.Actor), cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<PortfolioTrackResult>(result.Error);
        }

        var added = result.Value.Added + result.Value.Reactivated;
        PortfolioHistoryLog.BenchmarkTracked(logger, added);
        return Result.Success(new PortfolioTrackResult(1, added, result.Value.AlreadyTracked, 0, 0, result.Value.TotalTracked, added > 0 ? TrackPortfolioHoldingsHandler.UpdateDataHint : AlreadyTrackedHint));
    }
}
