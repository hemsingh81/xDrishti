using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning.SimTraining;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The simulate-everything job (spec AU-12, ADR 0243, ADR 0248): start ONE job, read its progress, pause, resume, cancel. Every route needs the owner's session; every change needs the CSRF token and is recorded in the audit log. Starting asks no password: the job writes
/// <c>training</c> runs only (never evidence), reads stored bars only, and places no order. A second start while one is active answers 409; a start before the backfill is complete, or without a determinable hold-out, answers 422 and creates nothing.
/// </summary>
internal sealed class SimAllEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/simulate-all").WithTags("Simulate");

        group.MapPost("/", StartAsync)
            .WithName("StartSimulateAll")
            .WithSummary("Start the simulate-everything job: the latest version of every stored strategy over every tracked instrument, as training runs in the window that ends before the hold-out and its embargo. 202 with the job. 422 simulate_all.backfill_incomplete (the history backfill is not complete), simulate_all.nothing_to_run; 409 simulate_all.active, or lab.holdout_unavailable (no hold-out block can be determined)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true, notFound: false);

        group.MapGet("/current", GetAsync)
            .WithName("GetSimulateAll")
            .WithSummary("The active job, else the newest one: state, why it waits, the window, the range covered, chunks done, failed, running and remaining, and the measured estimate of time and disk once 1 % of the chunks is done. Read while it runs. 404 simulate_all.no_active_job when there never was a job")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/training", GetTrainingAsync)
            .WithName("GetSimulateAllTraining")
            .WithSummary("The training from the simulation of a job (AU-13): folds, rows, the out-of-fold score beside the baseline, that the hold-out was not read, and the challenger it registered (never a champion). state not_evaluable names the missing quantity. 404 sim_training.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/pause", (HttpContext http, ICommandHandler<PauseSimAllCommand, SimAllView> handler, CancellationToken ct) => ControlAsync(http, a => handler.HandleAsync(new PauseSimAllCommand(a), ct)))
            .WithName("PauseSimulateAll")
            .WithSummary("Pause the job; a chunk in flight finishes. Only the owner resumes it. 404 simulate_all.no_active_job, 409 simulate_all.not_pausable")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/resume", (HttpContext http, ICommandHandler<ResumeSimAllCommand, SimAllView> handler, CancellationToken ct) => ControlAsync(http, a => handler.HandleAsync(new ResumeSimAllCommand(a), ct)))
            .WithName("ResumeSimulateAll")
            .WithSummary("Resume a paused job, whatever the reason; the backfill is checked again (422 simulate_all.backfill_incomplete). Hours and disk rules still apply at the next look. 404 simulate_all.no_active_job, 409 simulate_all.not_paused")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/cancel", (HttpContext http, ICommandHandler<CancelSimAllCommand, SimAllView> handler, CancellationToken ct) => ControlAsync(http, a => handler.HandleAsync(new CancelSimAllCommand(a), ct)))
            .WithName("CancelSimulateAll")
            .WithSummary("Cancel the job; chunks not done are never run, stored runs stay. 404 simulate_all.no_active_job")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Accepted<SimAllView>, ProblemHttpResult>> StartAsync(HttpContext http, ICommandHandler<StartSimAllCommand, SimAllView> handler, CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new StartSimAllCommand(owner.Value), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/simulate-all/current", result.Value);
    }

    private static async Task<Results<Ok<SimAllView>, ProblemHttpResult>> GetAsync(HttpContext http, IQueryHandler<GetSimAllQuery, Result<SimAllView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetSimAllQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<SimTrainingView>, ProblemHttpResult>> GetTrainingAsync(Guid id, HttpContext http, IQueryHandler<GetSimTrainingQuery, Result<SimTrainingView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetSimTrainingQuery(id), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<SimAllView>, ProblemHttpResult>> ControlAsync(HttpContext http, Func<string, ValueTask<Result<SimAllView>>> act)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await act(owner.Value);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A precondition that is not met (backfill, nothing to run, hold-out) is 422; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
