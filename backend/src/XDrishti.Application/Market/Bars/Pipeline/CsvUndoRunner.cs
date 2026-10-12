using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// The CSV phase of the job that undoes a load (ADR 0017 §6): removes the CSV bars the file wrote (through <see cref="ICsvUndo"/>: one transaction, the file's instrument and recorded range, Dhan bars stay), takes the
/// file off the loaded list so it is pending again, and records the owner's act in the audit trail — the text names the file and the count, never any row data. The aggregate phase that follows rebuilds the instrument.
/// Values an earlier load had overwritten are not restored. When another loaded file of the same instrument covers part of the same time (a copy under another name), its bars in that part go too (the removal is by
/// instrument and range, not by file): the job says so in a warning naming the files, which stay loaded.
/// </summary>
internal sealed class CsvUndoRunner(
    ICsvUndo undo,
    ICsvFileLog log,
    IAccountEventLog events,
    IMarketUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<CsvUndoRunner> logger)
{
    public async ValueTask<CsvPhase> RunAsync(DataJob job, JobReporter reporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var file = job.UndoCsvFile ?? throw new InvalidOperationException("The job does not undo a CSV load.");
        var phase = CsvPhase.Empty(PhaseStatus.Running) with { StartedAt = clock.GetUtcNow(), Total = 1 };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        var overlapping = await OverlappingAsync(file, cancellationToken);
        var result = await undo.UndoAsync(file, clock.GetUtcNow(), cancellationToken);
        if (result is null)
        {
            return phase with { Status = PhaseStatus.Failed, Code = DataErrors.CsvFileNotFoundCode, Message = DataErrors.CsvFileNotFound.Message, FinishedAt = clock.GetUtcNow() };
        }

        var text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"CSV load of {file} undone, {result.BarsRemoved} bars removed");
        DataLog.CsvLoadUndone(logger, file, result.BarsRemoved);
        await AuditAsync(text, job.RequestedBy, reporter, cancellationToken);
        if (overlapping.Count > 0)
        {
            await reporter.WarnAsync(OverlapWarning(overlapping), cancellationToken);
        }

        return phase with { Status = PhaseStatus.Succeeded, Done = 1, BarsRemoved = result.BarsRemoved, Message = text, FinishedAt = clock.GetUtcNow() };
    }

    private async ValueTask<IReadOnlyList<string>> OverlappingAsync(string file, CancellationToken cancellationToken)
    {
        var loaded = await log.ListAsync(cancellationToken);
        return loaded.TryGetValue(file, out var self) ? CsvOverlaps.Of(self, loaded.Values) : [];
    }

    internal static string OverlapWarning(IReadOnlyList<string> overlapping)
    {
        var shown = string.Join(", ", overlapping.Take(5)) + (overlapping.Count > 5 ? $" and {overlapping.Count - 5} more" : string.Empty);
        return $"Other loaded files of this instrument cover the same time ({shown}): the undo removed their bars in the overlap too. They stay loaded; load them again with force to bring those bars back.";
    }

    // The bars are gone and the file is pending again whatever happens here; a failed audit write is said in the job's warnings and the log.
    private async ValueTask AuditAsync(string text, string actor, JobReporter reporter, CancellationToken cancellationToken)
    {
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.CsvLoadUndone, null, text, actor, clock.GetUtcNow()));
            await unitOfWork.SaveAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the removal is committed; a failing audit write must not fail the job, it is reported as a warning
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.CsvUndoAuditFailed(logger, ex);
            await reporter.WarnAsync("The undo is done but could not be written to the activity log.", cancellationToken);
        }
    }
}
