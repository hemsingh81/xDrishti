using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The Services screen (ADR 0018, P1-13): what runs by itself and when. The services (end-of-day data run, instrument master refresh, holdings quotes) with their schedule in words, retries, timeout, catch-up, parameters, status,
/// latest run, next run and findings, plus the read-only per-account sync; the history of runs; the trading-day timeline; the notices for the bell; and the changes: save a schedule (checked against all services,
/// optimistic revision), the dry run of a schedule, restore default, pause, resume and run now. Every change is audited. All routes need a session; the changes also need the CSRF token; none needs a step-up (a
/// schedule is not a credential or a role). Part of the platform module.
/// </summary>
internal sealed class ServicesEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/services").WithTags("Services");

        group.MapGet("/", ListAsync)
            .WithName("ListServices")
            .WithSummary("Every registered service (id, name, description, runsAs, enabled, schedule with words, execution settings, parameters with their schema, status healthy|running|failed|paused, lastRun, nextRun, issues, isDefault, revision) and the read-only entries (the per-account sync: readOnly true, manageUrl /accounts)");

        group.MapGet("/timeline", GetTimelineAsync)
            .WithName("GetServicesTimeline")
            .WithSummary("The trading-day timeline of a date (date yyyy-MM-dd, India time; default today): market hours, the review cutoff, whether it is a trading day and the starts (marks) and up-times (bands) of every enabled service, with the service a run-after entry follows")
            .ProducesValidationProblem();

        group.MapGet("/notices", GetNoticesAsync)
            .WithName("GetServiceNotices")
            .WithSummary("What the owner should know about the services for the notification bell: service_failed for the latest run of each service with notify on failure when it failed and no retry follows (id service:failed:{serviceId}:{yyyy-MM-dd}: one notice per service per India day, text and time of the latest failure); the next run of the service clears it");

        group.MapGet("/{id}", GetAsync)
            .WithName("GetService")
            .WithSummary("One service as the list shows it. 404 services.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/runs", ListRunsAsync)
            .WithName("ListServiceRuns")
            .WithSummary("The runs and attempts of a service, newest first (page from 1, pageSize 1 to 100, default 25; the latest 200 are shown). trigger scheduled|catch_up|after|manual, status running|succeeded|failed|skipped, dataJobId links the data job a run started. 404 services.not_found")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id}/schedule", SaveAsync)
            .WithName("SaveServiceSchedule")
            .WithSummary("Save enabled, schedule (type daily|weekly|after|window|interval|continuous with its fields, times HH:mm India time), retries 0-5, timeoutMinutes 1-240, catchUp, notifyOnFailure and parameters, with the revision read. Checked against all services: 400 services.invalid with issues[] (service, field, message) when anything is wrong, nothing stored; warnings come back with the service. 409 services.revision_changed on a stale revision, 404 services.not_found, 409 services.read_only. Takes effect at the next look of the scheduler, no restart")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id}/schedule/preview", PreviewAsync)
            .WithName("PreviewServiceSchedule")
            .WithSummary("Dry run of a save: the same checks (issues: errors and warnings) and the next three starts, nothing stored. The revision is not looked at. 404 services.not_found, 409 services.read_only")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id}/schedule/reset", ResetAsync)
            .WithName("ResetServiceSchedule")
            .WithSummary("Restore default: writes the definition's default choices (nothing is deleted). Audited. 400 services.invalid if they would break a rule against the other services, 404 services.not_found, 409 services.read_only")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id}/pause", PauseAsync)
            .WithName("PauseService")
            .WithSummary("Pause: no scheduled, catch-up or run-after starts; past times are not made up for when it is resumed; Run now still works. Audited. 404 services.not_found, 409 services.read_only")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id}/resume", ResumeAsync)
            .WithName("ResumeService")
            .WithSummary("Resume a paused service. Audited. 404 services.not_found, 409 services.read_only")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id}/run", RunAsync)
            .WithName("RunServiceNow")
            .WithSummary("Run now (trigger manual), also for a paused service. 202 with the run, which goes on in the background (for the end-of-day run its dataJobId links the data job whose progress /api/data/jobs/{id} shows). Audited. 409 services.already_running when it runs (a skipped run is recorded), 404 services.not_found, 409 services.read_only")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<ServiceList>> ListAsync(IQueryHandler<ListServicesQuery, ServiceList> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListServicesQuery(), cancellationToken));

    private static async Task<Results<Ok<ServicesTimeline>, ProblemHttpResult>> GetTimelineAsync(
        HttpContext http,
        IQueryHandler<GetServicesTimelineQuery, Result<ServicesTimeline>> handler,
        CancellationToken cancellationToken,
        string? date = null)
    {
        var result = await handler.HandleAsync(new GetServicesTimelineQuery(date), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<ServiceNotices>> GetNoticesAsync(IQueryHandler<GetServiceNoticesQuery, ServiceNotices> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetServiceNoticesQuery(), cancellationToken));

    private static async Task<Results<Ok<ServiceView>, ProblemHttpResult>> GetAsync(
        string id,
        HttpContext http,
        IQueryHandler<GetServiceQuery, Result<ServiceView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetServiceQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ServiceRunPage>, ProblemHttpResult>> ListRunsAsync(
        string id,
        HttpContext http,
        IQueryHandler<ListServiceRunsQuery, Result<ServiceRunPage>> handler,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null)
    {
        var result = await handler.HandleAsync(new ListServiceRunsQuery(id, page, pageSize), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ServiceView>, ProblemHttpResult>> SaveAsync(
        string id,
        [FromBody] ServiceScheduleInput input,
        HttpContext http,
        ICommandHandler<SaveServiceScheduleCommand, ServiceView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new SaveServiceScheduleCommand(id, input, http.GetActor()), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<SchedulePreview>, ProblemHttpResult>> PreviewAsync(
        string id,
        [FromBody] ServiceScheduleInput input,
        HttpContext http,
        IQueryHandler<PreviewServiceScheduleQuery, Result<SchedulePreview>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PreviewServiceScheduleQuery(id, input), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ServiceView>, ProblemHttpResult>> ResetAsync(
        string id,
        HttpContext http,
        ICommandHandler<ResetServiceScheduleCommand, ServiceView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResetServiceScheduleCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ServiceView>, ProblemHttpResult>> PauseAsync(
        string id,
        HttpContext http,
        ICommandHandler<PauseServiceCommand, ServiceView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PauseServiceCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ServiceView>, ProblemHttpResult>> ResumeAsync(
        string id,
        HttpContext http,
        ICommandHandler<ResumeServiceCommand, ServiceView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResumeServiceCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<ServiceRunView>, ProblemHttpResult>> RunAsync(
        string id,
        HttpContext http,
        ICommandHandler<RunServiceNowCommand, ServiceRunView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RunServiceNowCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted($"/api/services/{id}/runs", result.Value);
    }

    /// <summary>The problem of a failure; a refused schedule (<c>services.invalid</c>) also lists its issues: service, field, code and message of each.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http)
    {
        if (error.Code != ServiceErrors.InvalidCode || error.Fields is not { Count: > 0 } fields)
        {
            return ProblemResults.From(error, http);
        }

        var errors = fields.GroupBy(f => f.Field).ToDictionary(g => g.Key, g => g.Select(f => f.Message).ToArray());
        var problem = new HttpValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest, Title = error.Message };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["issues"] = fields.Select(f => new ProblemIssue(f.Scope, f.Field, "error", f.Code ?? string.Empty, f.Message)).ToArray();
        return TypedResults.Problem(problem);
    }
}
