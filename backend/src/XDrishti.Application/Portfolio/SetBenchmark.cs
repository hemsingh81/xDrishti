using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Chooses the benchmark index of the portfolio: any index of the instrument master that is still listed (ADR 0020 §2). Anything else — no id, an unknown id, a share, ETF or future, a retired index — is <c>portfolio.invalid</c>. Choosing the index already
/// in force changes nothing and records nothing. A change is audited with the symbols (no figures) and drops the cached histories.
/// </summary>
public sealed record SetBenchmarkCommand(BenchmarkInput Input, string Actor) : ICommand;

internal sealed class SetBenchmarkHandler(
    IPortfolioBenchmark benchmarks,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SetBenchmarkHandler> logger) : ICommandHandler<SetBenchmarkCommand, BenchmarkView>
{
    public async ValueTask<Result<BenchmarkView>> HandleAsync(SetBenchmarkCommand command, CancellationToken cancellationToken)
    {
        if (command.Input?.InstrumentId is not { } id || id == Guid.Empty)
        {
            return Result.Failure<BenchmarkView>(PortfolioErrors.Invalid("instrumentId", "Required: the id of an index of the instrument master."));
        }

        var instrument = await benchmarks.FindAsync(id, cancellationToken);
        if (instrument is null)
        {
            return Result.Failure<BenchmarkView>(PortfolioErrors.Invalid("instrumentId", "That instrument is not in the instrument master."));
        }

        if (instrument.Type != InstrumentTypeCodes.Index)
        {
            return Result.Failure<BenchmarkView>(PortfolioErrors.Invalid("instrumentId", "Pick an index; shares, ETFs and futures cannot be the benchmark."));
        }

        if (instrument.Retired)
        {
            return Result.Failure<BenchmarkView>(PortfolioErrors.Invalid("instrumentId", "That index is no longer listed."));
        }

        var current = await benchmarks.GetAsync(cancellationToken);
        if (current is { IsDefault: false } && current.Instrument.Id == id)
        {
            return Result.Success(BenchmarkViews.Of(current));
        }

        var now = clock.GetUtcNow();
        await benchmarks.SetAsync(id, command.Actor, now, cancellationToken);
        events.Record(AccountEvent.Record(
            AccountEventType.PortfolioBenchmarkChanged,
            null,
            current is null ? $"Portfolio benchmark set to {instrument.Symbol}" : $"Portfolio benchmark changed from {current.Instrument.Symbol} to {instrument.Symbol}",
            command.Actor,
            now));
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<BenchmarkView>(PortfolioErrors.BenchmarkChanged);
        }

        PortfolioHistoryLog.BenchmarkChanged(logger);
        return Result.Success(BenchmarkViews.Of(await benchmarks.GetAsync(cancellationToken)));
    }
}
