using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Corporate actions and price adjustments (ADR 0017 §5, P1-10a): splits and bonus issues of instruments. Bars stay raw; an applied action makes adjusted reads
/// (<c>GET /api/data/instruments/{id}/bars?adjusted=true</c>) multiply the bars before its ex-date by its factor. Suggestions come from a detection over the stored day bars (after every data job and on
/// request) and change nothing until the owner confirms, edits or ignores them. All routes need a session; changes also need the CSRF token; none needs a step-up. Part of the market module.
/// </summary>
internal sealed class CorporateActionEndpoints : IEndpointModule
{
    /// <summary>The most bytes of a CSV text (the text itself is limited to 1,000,000 characters).</summary>
    private const long MaxBodyBytes = 4_000_000;

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/data").WithTags("Corporate actions");

        group.MapGet("/corporate-actions", ListAsync)
            .WithName("ListCorporateActions")
            .WithSummary("The splits and bonus issues, newest ex-date first: status (suggested|applied|ignored), instrumentId, page, pageSize (max 200); pending is the number of suggestions waiting, whatever the filter")
            .ProducesValidationProblem();

        group.MapPost("/corporate-actions", AddAsync)
            .WithName("AddCorporateAction")
            .WithSummary("Add a split (numerator:denominator = old shares : new shares, 1:5 gives a factor of 0.2) or a bonus (bonus shares : shares held, 1:1 gives 0.5) as applied. 201; 400 data.invalid with the field; 404 data.instrument_not_found; 409 data.corporate_action_exists, 409 data.corporate_action_conflicts_same_day (an applied action of the other kind is on that ex-date), 409 data.csv_already_adjusted (the ex-date is on or before csvAdjustedThrough: the loaded CSV files are adjusted at source; send acknowledgeCsvAdjusted=true, in the body or the query, to add it anyway)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/corporate-actions/{id:guid}", UpdateAsync)
            .WithName("UpdateCorporateAction")
            .WithSummary("Confirm a suggestion (an empty body confirms it as it is), change the ex-date, kind, ratio or note, or bring back an ignored action: the result is applied. Fields left out keep their value. 200; 400 data.invalid; 404 data.corporate_action_not_found; 409 data.corporate_action_exists, 409 data.corporate_action_conflicts_same_day, 409 data.csv_already_adjusted (as when adding; acknowledgeCsvAdjusted=true in the body or the query)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/corporate-actions/{id:guid}/ignore", IgnoreAsync)
            .WithName("IgnoreCorporateAction")
            .WithSummary("Decline an action: it stays in the list as ignored (nothing is deleted) and is not suggested again. Idempotent. 200; 404 data.corporate_action_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/corporate-actions/detect", DetectAsync)
            .WithName("DetectCorporateActions")
            .WithSummary("Look for splits and bonus issues in the stored day bars of every share and ETF now; new candidates become suggested actions, nothing is applied and repeating it finds nothing new. 200 with the counts");

        group.MapPost("/corporate-actions/import", ImportAsync)
            .WithName("ImportCorporateActions")
            .WithSummary("Import actions from CSV (header symbol,exDate,kind,numerator,denominator; RELIANCE or NSE:RELIANCE; at most 5,000 lines) sent as the text/csv or text/plain body, or as the first file of a multipart/form-data upload; they are applied. preview=true writes nothing and returns the counts. Unknown symbols are listed, never guessed; duplicates (any status) are skipped; bad lines (also a kind that clashes with an applied one on the same day) are reported and skipped. Lines with an ex-date on or before the date the loaded CSV files reach (adjusted at source) are counted in adjustsCsvTwice and listed in warnings; importing them needs acknowledgeCsvAdjusted=true. 200; 400 data.invalid; 409 data.csv_already_adjusted (with adjustsCsvTwice and instruments)")
            .Accepts<string>("text/csv", "text/plain", "multipart/form-data")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<CorporateActionPage>, ProblemHttpResult>> ListAsync(
        HttpContext http,
        IQueryHandler<ListCorporateActionsQuery, Result<CorporateActionPage>> handler,
        CancellationToken cancellationToken,
        string? status = null,
        Guid? instrumentId = null,
        int? page = null,
        int? pageSize = null)
    {
        var result = await handler.HandleAsync(new ListCorporateActionsQuery(new CorporateActionRequest(status, instrumentId, page, pageSize)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Created<CorporateActionView>, ProblemHttpResult>> AddAsync(
        HttpContext http,
        ICommandHandler<AddCorporateActionCommand, CorporateActionView> handler,
        CancellationToken cancellationToken,
        [FromBody] AddCorporateActionRequest? request = null,
        bool? acknowledgeCsvAdjusted = null)
    {
        var body = request ?? new AddCorporateActionRequest(null, null, null, null, null, null);
        var result = await handler.HandleAsync(new AddCorporateActionCommand(body with { AcknowledgeCsvAdjusted = acknowledgeCsvAdjusted ?? body.AcknowledgeCsvAdjusted }, http.GetActor()), cancellationToken);
        return result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.Created($"/api/data/corporate-actions?instrumentId={result.Value.InstrumentId}", result.Value);
    }

    private static async Task<Results<Ok<CorporateActionView>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<UpdateCorporateActionCommand, CorporateActionView> handler,
        CancellationToken cancellationToken,
        [FromBody] UpdateCorporateActionRequest? request = null,
        bool? acknowledgeCsvAdjusted = null)
    {
        var body = request ?? new UpdateCorporateActionRequest(null, null, null, null, null);
        var result = await handler.HandleAsync(new UpdateCorporateActionCommand(id, body with { AcknowledgeCsvAdjusted = acknowledgeCsvAdjusted ?? body.AcknowledgeCsvAdjusted }, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<CorporateActionView>, ProblemHttpResult>> IgnoreAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<IgnoreCorporateActionCommand, CorporateActionView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new IgnoreCorporateActionCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<DetectionResult>, ProblemHttpResult>> DetectAsync(
        HttpContext http,
        ICommandHandler<DetectCorporateActionsCommand, DetectionResult> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DetectCorporateActionsCommand(), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ImportCorporateActionsResult>, ProblemHttpResult>> ImportAsync(
        HttpContext http,
        ICommandHandler<ImportCorporateActionsCommand, ImportCorporateActionsResult> handler,
        CancellationToken cancellationToken,
        bool? preview = null,
        bool? acknowledgeCsvAdjusted = null)
    {
        var text = await ReadTextAsync(http.Request, cancellationToken);
        var result = await handler.HandleAsync(new ImportCorporateActionsCommand(text, preview ?? false, http.GetActor(), acknowledgeCsvAdjusted ?? false), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    // The CSV is the body (text/csv, text/plain, anything that is not a form) or the first file of a multipart upload. Reading stops a little past the limit, which the handler then refuses.
    private static async Task<string?> ReadTextAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        if (request.HasFormContentType)
        {
            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                return null; // a multipart body that is not one: the use case answers data.invalid
            }

            if (form.Files.Count == 0)
            {
                return null;
            }

            await using var file = form.Files[0].OpenReadStream();
            return await ReadAsync(file, cancellationToken);
        }

        return await ReadAsync(request.Body, cancellationToken);
    }

    private static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = new StringBuilder();
        var buffer = new char[16 * 1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > 1_000_001)
            {
                break;
            }
        }

        return text.ToString();
    }
}
