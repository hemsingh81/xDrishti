using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>The stored paper entries of one epoch for the backtest comparison (spec P10-04).</summary>
/// <param name="Rows">Every entry of the epoch with its exit, oldest entry first.</param>
/// <param name="OtherEpochs">Entries of other epochs.</param>
/// <param name="NextEpochStart">The first session of the next epoch, or null when this is the newest.</param>
public sealed record PaperCompareData(IReadOnlyList<PaperCompareRow> Rows, int OtherEpochs, DateOnly? NextEpochStart);

/// <summary>The raw read behind the backtest-versus-paper report (spec P10-04, ADR 0122). Select only; no write member.</summary>
public interface IBacktestPaperData
{
    /// <summary>The entries of the epoch with their exits, and the counts the comparison needs.</summary>
    ValueTask<PaperCompareData> ReadAsync(long epochId, CancellationToken cancellationToken);
}
