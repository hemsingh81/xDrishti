using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Audit;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The audit screen of spec P8-22 (ADR 0117): a read over the six insert-only stores. GET only, never cached, nothing written, no export.</summary>
internal sealed class AuditEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var audit = api.MapGroup("/audit").WithTags("Audit");
        audit.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        audit.MapGet("/entries", ListAsync)
            .WithName("ListAuditEntries")
            .WithSummary("The audit, newest first, merged from the sign-in, account, approval, plan, autonomy, learning and charge-sheet confirmation records (limit 1 to 200, default 50; from/to India dates, default the last 30 days; source, category, kind, outcome, actor take comma lists; account; q 3 to 64 characters, matched against the redacted summary only). Cursor paging. A record that cannot be read is named in sources. 422 audit.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        audit.MapGet("/entries/{id}", GetAsync)
            .WithName("GetAuditEntry")
            .WithSummary("One audit entry with its detail fields (id as in the list, such as auth:<uuid> or approval:12). A sign-in entry adds the IP address and user agent. 404 audit.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        audit.MapGet("/summary", SummaryAsync)
            .WithName("GetAuditSummary")
            .WithSummary("Counts of the window per record, category and outcome, with the same per-record read states. 422 audit.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        audit.MapGet("/kinds", KindsAsync)
            .WithName("ListAuditKinds")
            .WithSummary("The kinds each record can hold, with category and outcome, for the filter lists.");
    }

    private static async Task<Results<Ok<AuditPageView>, ProblemHttpResult>> ListAsync(
        string? from, string? to, string? source, string? category, string? kind, string? outcome, string? actor, string? account, string? q, int? limit, string? cursor,
        HttpContext http,
        IQueryHandler<ListAuditQuery, Result<AuditPageView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListAuditQuery(new AuditFilters(from, to, source, category, kind, outcome, actor, account, q), limit, cursor, OwnerEmail(http)), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<AuditEntryDetailView>, ProblemHttpResult>> GetAsync(
        string id,
        HttpContext http,
        IQueryHandler<GetAuditEntryQuery, Result<AuditEntryDetailView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAuditEntryQuery(id, OwnerEmail(http)), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<AuditSummaryView>, ProblemHttpResult>> SummaryAsync(
        string? from, string? to,
        HttpContext http,
        IQueryHandler<GetAuditSummaryQuery, Result<AuditSummaryView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAuditSummaryQuery(from, to), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<AuditKindsView>> KindsAsync(IQueryHandler<ListAuditKindsQuery, AuditKindsView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListAuditKindsQuery(), cancellationToken));

    private static string? OwnerEmail(HttpContext http) => http.GetVerifiedSession()?.Info.User.Email;

    /// <summary>A refused input is 422; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == AuditErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
