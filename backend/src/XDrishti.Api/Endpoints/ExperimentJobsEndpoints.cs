using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Learning;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The replay job of the Learning area (spec P8-18 Part B, ADR 0129): a preview that says what a replay would mean, the start of ONE job, the jobs and one job, and a cancel. A start needs a session, the CSRF token, the capability <c>experiments_ui</c> (off by default, asked BEFORE
/// the password), the password typed again for THIS start, and answers 202 with the job. A result is a proposal read in the Experiments screen: nothing is applied to a strategy, a plan, a cell, a model or a setting, and there is no endpoint that does. Cancelling is the safe
/// direction and needs the session only. Only this module starts a job.
/// </summary>
internal sealed class ExperimentJobsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/experiments").WithTags("Learning");

        group.MapGet("/preview", PreviewAsync)
            .WithName("PreviewExperiment")
            .WithSummary("What starting a replay on a stored run would mean, before the owner clicks: the baseline, the knobs offered (exit parameters and slippage upward), the next trial number and the improvement it must show, the folds the data has and the best possible outcome (Inconclusive when fewer than 6), the measured replay time (not_evaluable until a job measured one), and the starts left today. Read only. 422 learning.invalid, 404 learning.run_not_found")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", StartAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("StartExperimentJob")
            .WithSummary("Start ONE replay job of a stored run with one entry-neutral knob changed (an exit parameter, or slippage upward); 202 with the job, or 200 with the existing job for the identical input. The result is judged by the experiment rules of ADR 0072 and is a proposal: nothing is applied. Needs a session, the CSRF token, the capability experiments_ui and the password in the body, checked on EVERY request. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid or experiments.knob_not_allowed or experiments.baseline_not_rebuildable, 404 learning.run_not_found, 409 experiments.job_active, 429 experiments.daily_limit or auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);

        group.MapGet("/jobs", ListAsync)
            .WithName("ListExperimentJobs")
            .WithSummary("The replay jobs, newest first (limit 1 to 200, default 20), with their state, stage, progress and the measured replay time. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/jobs/{id:guid}", GetAsync)
            .WithName("GetExperimentJob")
            .WithSummary("One replay job: state, stage, progress, the candidate run and the experiment it produced, the measured replay time with the instrument count and whether other heavy jobs ran, and the limits. 404 experiments.job_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/jobs/{id:guid}/cancel", CancelAsync)
            .WithName("CancelExperimentJob")
            .WithSummary("Stop a job: a queued one at once, a running one at its next safe point between instruments (nothing is stored, no ledger row, not a trial). Needs the session and the CSRF token; no password, because stopping is the safe direction. 202, 409 experiments.job_not_cancellable, 404 experiments.job_not_found")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<ExperimentPreviewView>, ProblemHttpResult>> PreviewAsync(string? baselineRunKey, HttpContext http, IQueryHandler<GetExperimentPreviewQuery, Result<ExperimentPreviewView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetExperimentPreviewQuery(baselineRunKey ?? string.Empty), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<ExperimentJobView>, Ok<ExperimentJobView>, ProblemHttpResult>> StartAsync(
        StartExperimentRequest request,
        HttpContext http,
        ICapabilityGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<StartExperimentJobCommand, StartedExperimentJob> handler,
        CancellationToken cancellationToken)
    {
        // The capability first: a switched-off start neither counts toward the lockout nor says anything about the password.
        if (await OwnerAct.RefusedWhileOffAsync(http, gate, Domain.Capabilities.Capabilities.ExperimentsUi, cancellationToken) is { } off)
        {
            return off;
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new StartExperimentJobCommand(request.BaselineRunKey ?? string.Empty, request.Knob?.Kind, request.Knob?.Name, request.Knob?.CandidateValue, request.HoldOutMonths, request.Reason, owner.Value), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        if (result.IsFailure)
        {
            return Problem(result.Error, http);
        }

        var started = result.Value;
        return started.Created
            ? TypedResults.Accepted("/api/experiments/jobs/" + started.Job.Id.ToString("D"), started.Job)
            : TypedResults.Ok(started.Job);
    }

    private static async Task<Results<Ok<ExperimentJobList>, ProblemHttpResult>> ListAsync(int? limit, HttpContext http, IQueryHandler<ListExperimentJobsQuery, Result<ExperimentJobList>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListExperimentJobsQuery(limit ?? 20), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ExperimentJobView>, ProblemHttpResult>> GetAsync(Guid id, HttpContext http, IQueryHandler<GetExperimentJobQuery, Result<ExperimentJobView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetExperimentJobQuery(id), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<ExperimentJobView>, ProblemHttpResult>> CancelAsync(Guid id, HttpContext http, ICommandHandler<CancelExperimentJobCommand, ExperimentJobView> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new CancelExperimentJobCommand(id, owner.Value), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/experiments/jobs/" + id.ToString("D"), result.Value);
    }

    /// <summary>A refused input, a refused knob and a baseline that cannot be replayed are 422; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code is LearningErrors.InvalidCode or ReplayKnobCatalogue.NotAllowedCode or ExperimentJobErrors.NotRebuildableCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
