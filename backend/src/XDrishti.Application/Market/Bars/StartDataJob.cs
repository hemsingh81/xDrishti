using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// Registers a data job (CSV files → Dhan fetch → aggregates → data quality checks, or any of them) and queues it for the background worker. One job runs at a time (all instances):
/// otherwise <c>data.job_running</c>. A job with the CSV phase needs the instrument list (<c>data.master_missing</c>). <c>steps</c> unknown or empty is <c>data.invalid</c>.
/// <see cref="Trigger"/> says who started it: the owner through the endpoint, or the scheduled end-of-day run (<see cref="DataJobTrigger.Daily"/>). Without steps the job runs
/// <see cref="DataSteps.Default"/> (the quality checks only when the request lists them). <see cref="InstrumentId"/> limits the Dhan, aggregate and quality steps to one instrument (the fix actions of the Data quality screen).
/// <c>csvFiles</c> limits the CSV step to the named files (each must be a <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c> file of the folder, at most <see cref="StartDataJobHandler.MaxCsvFiles"/>, not empty: <c>data.invalid</c> naming the
/// first unknown ones); without the CSV step it is ignored. <see cref="UndoCsvFile"/> (with <see cref="InstrumentId"/>) makes the job the undo of that file's load (ADR 0017 §6): CSV removal, then the aggregate step.
/// </summary>
public sealed record StartDataJobCommand(
    StartDataJobRequest Request,
    string RequestedBy,
    DataJobTrigger Trigger = DataJobTrigger.Manual,
    Guid? InstrumentId = null,
    string? UndoCsvFile = null) : ICommand;

internal sealed class StartDataJobHandler(
    IDataJobStore jobs,
    IDataJobChannel queue,
    IInstrumentCatalog catalog,
    ICsvFolder folder,
    TimeProvider clock,
    ILogger<StartDataJobHandler> logger) : ICommandHandler<StartDataJobCommand, DataJobView>
{
    /// <summary>The most files one job may be asked to load by name.</summary>
    public const int MaxCsvFiles = 214;

    /// <summary>How many unknown names the error lists.</summary>
    private const int MaxNamedErrors = 5;

    public async ValueTask<Result<DataJobView>> HandleAsync(StartDataJobCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var job = command.UndoCsvFile is { } undoFile && command.InstrumentId is { } undoInstrument
            ? DataJob.StartUndo(undoFile, undoInstrument, command.RequestedBy, now)
            : null;
        if (job is null)
        {
            var started = await PrepareAsync(command, now, cancellationToken);
            if (started.IsFailure)
            {
                return Result.Failure<DataJobView>(started.Error);
            }

            job = started.Value;
        }

        if (!await jobs.TryStartAsync(job, DataLimits.StaleAfter, now, cancellationToken))
        {
            return Result.Failure<DataJobView>(DataErrors.JobRunning);
        }

        var stepList = string.Join(',', job.Steps);
        var trigger = job.Trigger.ToCode();
        DataLog.JobStarted(logger, job.Id, stepList, job.ForceCsv, trigger);
        queue.Enqueue(new DataJobTicket(job.Id));
        return Result.Success(DataJobView.From(job));
    }

    private async ValueTask<Result<DataJob>> PrepareAsync(StartDataJobCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var steps = request.Steps ?? DataSteps.Default;
        if (steps.Count == 0)
        {
            return Result.Failure<DataJob>(DataErrors.Invalid("steps", "Choose at least one of csv, dhan, aggregate and quality."));
        }

        var unknown = steps.Where(s => !DataSteps.IsKnown(s)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure<DataJob>(DataErrors.Invalid("steps", "Use csv, dhan, aggregate and quality."));
        }

        var withCsv = steps.Contains(DataSteps.Csv);
        IReadOnlyList<string>? csvFiles = null;
        if (withCsv && request.CsvFiles is not null)
        {
            var checkedFiles = await CheckCsvFilesAsync(request.CsvFiles, cancellationToken);
            if (checkedFiles.IsFailure)
            {
                return Result.Failure<DataJob>(checkedFiles.Error);
            }

            csvFiles = checkedFiles.Value;
        }

        if (withCsv && await catalog.CountActiveAsync(cancellationToken) == 0)
        {
            return Result.Failure<DataJob>(DataErrors.MasterMissing);
        }

        return Result.Success(DataJob.Start(steps, request.ForceCsv ?? false, command.RequestedBy, now, command.Trigger, request.RefetchGaps ?? false, command.InstrumentId, csvFiles));
    }

    /// <summary>The names must be files of the folder listing with a loadable name (no path, no traversal can match); the list is returned without duplicates.</summary>
    private async ValueTask<Result<IReadOnlyList<string>>> CheckCsvFilesAsync(IReadOnlyList<string> requested, CancellationToken cancellationToken)
    {
        var names = requested.Where(n => n is not null).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return Result.Failure<IReadOnlyList<string>>(DataErrors.Invalid("csvFiles", "Choose at least one file, or leave csvFiles out to load every file of the folder."));
        }

        if (names.Count > MaxCsvFiles)
        {
            return Result.Failure<IReadOnlyList<string>>(DataErrors.Invalid("csvFiles", $"Name at most {MaxCsvFiles} files."));
        }

        var inFolder = new HashSet<string>(StringComparer.Ordinal);
        if (folder.Exists())
        {
            try
            {
                foreach (var file in await folder.ListAsync(cancellationToken))
                {
                    if (CsvFileName.TryParse(file.Name, out _))
                    {
                        inFolder.Add(file.Name);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // an unreadable folder has no files to name: every name below is unknown
            }
        }

        var errors = new FieldErrors();
        foreach (var name in names.Where(n => !inFolder.Contains(n)).Take(MaxNamedErrors))
        {
            errors.Add("csvFiles", $"'{CsvNames.Echo(name)}' is not a <SYMBOL>_1m.csv[.gz] file of the CSV folder.");
        }

        return errors.HasErrors
            ? Result.Failure<IReadOnlyList<string>>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success<IReadOnlyList<string>>(names);
    }
}
