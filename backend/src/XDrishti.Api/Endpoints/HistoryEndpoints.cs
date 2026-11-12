using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Market.History;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The history card of the Data screen (CF-04e): how deep the stored history of every tracked instrument is, where the 1-minute backfill stands, and the two owner acts that start it (the whole backfill as the service run, the seam probe in the request). Each act needs a session, the CSRF token
/// and a fresh password for THIS act; the switch <c>Data:History:Backfill:Enabled</c> is asked FIRST, so an act that is off neither counts toward the sign-in lockout nor says anything about the password. No route turns the switch on (it is a deployment setting).
/// </summary>
internal sealed class HistoryEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/data/history").WithTags("Data");

        group.MapGet("/", GetAsync)
            .WithName("GetDataHistory")
            .WithSummary("The history card: the switch Data:History:Backfill:Enabled (read-only: no route turns it on) with the reason it is off, the run window and whether it is open now, whether the history-backfill service runs, the span of the daily files, totals (instruments by backfill status, chunks, depth buckets by years of stored bars) and per instrument (symbol order) the first stored bar, the first CSV bar (the seam), the backfill status, chunks, the seam probe and the seam jump. Reads only: no request to Dhan. No password");

        group.MapPost("/backfill", StartAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("StartHistoryBackfill")
            .WithSummary("Owner act: start the 1-minute backfill of the whole tracked set as a run of the service history-backfill. Needs a session, the CSRF token and the password in the body, checked on EVERY call. Order: session (401), switch (422 history.disabled), password (403 auth.step_up_required, 401 auth.invalid_credentials, 423 auth.locked, 429), run active (409 history.run_active), run window (422 history.outside_window), dry run (422 history.calendar_missing, history.disk_low), Dhan (409 data.dhan_not_available, data.dhan_not_connected). 202 with the run when work is pending, 200 started=false when nothing is. Audited with counts only")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked);

        group.MapPost("/probe", ProbeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ProbeHistorySeam")
            .WithSummary("Owner act: probe the seam of the first tracked instruments (every instrument with an applied action first, then others up to Data:History:Probe:Instruments) in this request: one Dhan request each, compared with the CSV bars in memory; nothing is stored but the seam rows. Same checks and order as the backfill less the dry run (422 history.disabled, 409 history.run_active also while the service runs or another probe does, 422 history.outside_window, 409 data.dhan_not_available, data.dhan_not_connected). Needs a session, the CSRF token and the password in the body. Audited with counts only")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked);
    }

    private static async Task<Ok<HistoryOverview>> GetAsync(HttpContext http, IQueryHandler<GetHistoryOverviewQuery, HistoryOverview> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetHistoryOverviewQuery(), cancellationToken));
    }

    private static async Task<Results<Accepted<HistoryBackfillStarted>, Ok<HistoryBackfillStarted>, ProblemHttpResult>> StartAsync(
        HistoryActRequest request,
        HttpContext http,
        IBackfillSettings settings,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<StartHistoryBackfillCommand, HistoryBackfillStarted> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAsync(http, settings, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new StartHistoryBackfillCommand(owner.Value), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        return result.Value.Started
            ? TypedResults.Accepted($"/api/services/{HistoryBackfillService.Id}/runs", result.Value)
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ProbeSeamSummary>, ProblemHttpResult>> ProbeAsync(
        HistoryActRequest request,
        HttpContext http,
        IBackfillSettings settings,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ProbeHistorySeamCommand, ProbeSeamSummary> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAsync(http, settings, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ProbeHistorySeamCommand(owner.Value), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    // Session first (401), then the switch (422: an act that is off neither counts toward the lockout nor says anything about the password), then the fresh password.
    private static async Task<XDrishti.Domain.Common.Result<string>> OwnerAsync(HttpContext http, IBackfillSettings settings, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, string? password, CancellationToken cancellationToken)
    {
        var session = OwnerAct.SessionOwner(http);
        if (session.IsFailure)
        {
            return session;
        }

        return settings.Enabled
            ? await OwnerAct.FreshAsync(http, passwordCheck, password, cancellationToken)
            : XDrishti.Domain.Common.Result.Failure<string>(BackfillErrors.Disabled());
    }
}
