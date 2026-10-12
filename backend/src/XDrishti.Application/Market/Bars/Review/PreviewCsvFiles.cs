using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Review;

/// <summary>
/// The review step before loading (ADR 0017 §6): a dry run of the real CSV reader, row rules and session check over up to <see cref="PreviewCsvFilesHandler.MaxFiles"/> files, nothing written. Names are checked
/// against the folder listing; a name that is not in it (a path, a traversal, a file that is gone) is that file's <c>error</c>, never a failure of the request. A request reads at most twice the
/// per-file limits of <c>Data:Csv</c> in rows and bytes and works for at most 60 seconds (the files that come after either limit, and the one being read when time runs out, are <c>error</c>), and looks up at most one million stored bars
/// (more is sampled and said so per file).
/// <c>data.invalid</c> (400) for no files or more than 20.
/// </summary>
public sealed record PreviewCsvFilesQuery(PreviewCsvFilesRequest Request) : IQuery<Result<CsvPreviewResult>>;

internal sealed class PreviewCsvFilesHandler(CsvFileCatalog catalog, CsvFilePreviewer previewer, IDataSettings settings, TimeProvider clock) : IQueryHandler<PreviewCsvFilesQuery, Result<CsvPreviewResult>>
{
    public const int MaxFiles = 20;

    /// <summary>The stored bars one request may look up before the rest is sampled.</summary>
    public const long MaxComparedRows = 1_000_000;

    /// <summary>A request may read this many times the per-file row and byte limits of one file, over all its files.</summary>
    public const int LimitMultiple = 2;

    /// <summary>How long one request may work; files not finished by then are reported as errors.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    public async ValueTask<Result<CsvPreviewResult>> HandleAsync(PreviewCsvFilesQuery query, CancellationToken cancellationToken)
    {
        var requested = query.Request.Files;
        if (requested is null || requested.Count == 0)
        {
            return Result.Failure<CsvPreviewResult>(DataErrors.Invalid("files", "Choose at least one file to preview."));
        }

        var names = requested.Where(n => n is not null).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return Result.Failure<CsvPreviewResult>(DataErrors.Invalid("files", "Choose at least one file to preview."));
        }

        if (names.Count > MaxFiles)
        {
            return Result.Failure<CsvPreviewResult>(DataErrors.Invalid("files", $"Preview at most {MaxFiles} files at a time."));
        }

        var listing = await catalog.ListAsync(cancellationToken);
        var byName = listing.Files.ToDictionary(f => f.File.Name, StringComparer.Ordinal);
        var budget = new PreviewBudget(settings.CsvMaxRowsPerFile * (long)LimitMultiple, settings.CsvMaxBytesPerFile * LimitMultiple, MaxComparedRows, names.Count);
        var previews = new List<CsvFilePreview>(names.Count);
        using var deadline = new CancellationTokenSource(Deadline, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byName.TryGetValue(name, out var entry))
            {
                previews.Add(CsvFilePreviewer.NotInFolder(name, listing.Exists));
                continue;
            }

            if (deadline.IsCancellationRequested)
            {
                previews.Add(CsvFilePreviewer.OutOfTime(entry));
                continue;
            }

            try
            {
                previews.Add(await previewer.PreviewAsync(entry, budget, linked.Token));
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                previews.Add(CsvFilePreviewer.OutOfTime(entry));
            }
        }

        return Result.Success(new CsvPreviewResult(previews, budget.RowsRead, budget.ComparedRows));
    }
}
