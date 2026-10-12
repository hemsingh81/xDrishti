using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The review step of the CSV load (ADR 0017 §6, P1-07a): the folder's files with their state, a dry run of the real reader over chosen files, and the undo of a load. Loading chosen files is
/// <c>POST /api/data/refresh</c> with <c>csvFiles</c>. All routes need a session; the two POSTs also need the CSRF token; none needs a step-up (the folder is mounted read-only, the undo only removes bars the
/// CSV source wrote and is audited). Part of the market module.
/// </summary>
internal sealed class DataCsvEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/data/csv").WithTags("Data");

        group.MapGet("/files", ListFilesAsync)
            .WithName("ListCsvFiles")
            .WithSummary("The files of the CSV folder: state (pending|loaded|changed - loaded, but size or modified time differ now), size, modified time, the symbol parsed from the name, the instrument it maps to or why not (unknown|ambiguous|invalidName), and rows, firstBar, lastBar, loadedAt and overlapsWith (other loaded files of the instrument covering part of the same time) for loaded files. No file is opened, so hundreds of files list at once. Changed and pending first, then loaded, by name: q (name or symbol), state, page, pageSize (max 200)")
            .ProducesValidationProblem();

        group.MapPost("/preview", PreviewAsync)
            .WithName("PreviewCsvFiles")
            .WithSummary("Dry run of the real reader, row rules and session check over up to 20 files of the folder (names from the listing; anything else is that file's error), nothing written: rows, first and last bar, trading days, repaired and rejected with up to 10 examples, the verdict (pass or the refusal message), the symbol mapping, the first 5 accepted rows and what loading would do to the stored bars (would insert, update, keep existing Dhan bars, unchanged; a spread sample past 1 million lookups per request, flagged sampled). A request reads at most 2 times the per-file limits of Data:Csv and works for at most 60 seconds")
            .ProducesValidationProblem();

        group.MapPost("/files/{name}/undo", UndoAsync)
            .WithName("UndoCsvLoad")
            .WithSummary("Undo the load of one CSV file as a data job: removes the file's csv-source bars of its instrument within the file's recorded first-to-last bar (Dhan bars stay), makes the file pending again, then rebuilds the instrument's aggregates; audited. Values an earlier load overwrote are NOT restored (no history is kept). The removal is by instrument and range: bars of another loaded file in the overlap go too, and the job warns. 202 with the job; 409 data.job_running while any data job runs; 404 data.csv_file_not_found when the file was never loaded")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<CsvFilePage>, ProblemHttpResult>> ListFilesAsync(
        HttpContext http,
        IQueryHandler<ListCsvFilesQuery, Result<CsvFilePage>> handler,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? q = null,
        string? state = null,
        int? page = null,
        int? pageSize = null)
    {
        var result = await handler.HandleAsync(new ListCsvFilesQuery(new CsvFileRequest(q, state, page, pageSize)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<CsvPreviewResult>, ProblemHttpResult>> PreviewAsync(
        HttpContext http,
        IQueryHandler<PreviewCsvFilesQuery, Result<CsvPreviewResult>> handler,
        CancellationToken cancellationToken,
        [FromBody] PreviewCsvFilesRequest? request = null)
    {
        var result = await handler.HandleAsync(new PreviewCsvFilesQuery(request ?? new PreviewCsvFilesRequest(null)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<DataJobView>, ProblemHttpResult>> UndoAsync(
        string name,
        HttpContext http,
        ICommandHandler<UndoCsvLoadCommand, DataJobView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UndoCsvLoadCommand(name, http.GetActor()), cancellationToken);
        return result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.Accepted($"/api/data/jobs/{result.Value.Id}", result.Value);
    }
}
