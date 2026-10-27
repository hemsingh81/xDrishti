using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The stored briefing and weekly review texts (spec P9-06 AC 12, ADR 0140). Read only: there is no write endpoint, no "regenerate" button and nothing here calls the model. A new text is made by the briefing and weekly-review services or by the owner's <c>xd assistant briefing</c> verb.
/// </summary>
internal sealed class AssistantBriefingEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/assistant/briefings").WithTags("Assistant");

        group.MapGet(string.Empty, ListAsync)
            .WithName("ListAssistantBriefings")
            .WithSummary("The newest stored briefing and weekly review texts (kind daily or weekly, limit 1 to 60, default 14), each with its source (model or deterministic), the reason a model text was not used, the guard's findings per attempt and the records behind the facts. Read only. 422 assistant.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/daily", DailyAsync)
            .WithName("GetAssistantDailyBriefing")
            .WithSummary("The stored briefing of a plan date (date=YYYY-MM-DD; default the newest stored one): the text, source, reason, word count, facts hash, model, attempts with findings and the evidence. Read only. 404 assistant.briefing_not_found; 422 assistant.invalid")
            .ProducesRejections(conflict: false)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/weekly", WeeklyAsync)
            .WithName("GetAssistantWeeklyReview")
            .WithSummary("The stored review text of an ISO week (week=YYYY-Www; default this week). Read only. 404 assistant.briefing_not_found; 422 assistant.invalid")
            .ProducesRejections(conflict: false)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/preview", PreviewAsync)
            .WithName("PreviewAssistantBriefing")
            .WithSummary("The fixed-format text of a plan date or week (kind daily or weekly, subject a date or an ISO week, default the newest plan / this week), computed from the stored facts and shown. Writes nothing and never calls the model. 404 assistant.briefing_not_found; 409 assistant.briefing_not_evaluable (a week with no digest); 422 assistant.invalid")
            .ProducesRejections(conflict: true)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<IReadOnlyList<BriefingView>>, ProblemHttpResult>> ListAsync(
        HttpContext http, IQueryHandler<ListBriefingsQuery, Result<IReadOnlyList<BriefingView>>> handler, CancellationToken cancellationToken, string? kind = null, int limit = ListBriefingsQuery.DefaultLimit)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new ListBriefingsQuery(kind, limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static Task<Results<Ok<BriefingView>, ProblemHttpResult>> DailyAsync(
        HttpContext http, IQueryHandler<GetBriefingQuery, Result<BriefingView>> handler, CancellationToken cancellationToken, string? date = null) =>
        GetAsync("daily", date, http, handler, cancellationToken);

    private static Task<Results<Ok<BriefingView>, ProblemHttpResult>> WeeklyAsync(
        HttpContext http, IQueryHandler<GetBriefingQuery, Result<BriefingView>> handler, CancellationToken cancellationToken, string? week = null) =>
        GetAsync("weekly", week, http, handler, cancellationToken);

    private static async Task<Results<Ok<BriefingView>, ProblemHttpResult>> GetAsync(
        string kind, string? subject, HttpContext http, IQueryHandler<GetBriefingQuery, Result<BriefingView>> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetBriefingQuery(kind, subject), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BriefingView>, ProblemHttpResult>> PreviewAsync(
        HttpContext http, IQueryHandler<PreviewBriefingQuery, Result<BriefingView>> handler, CancellationToken cancellationToken, string? kind = "daily", string? subject = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new PreviewBriefingQuery(kind ?? "daily", subject), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 as the contract says; the rest through the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
