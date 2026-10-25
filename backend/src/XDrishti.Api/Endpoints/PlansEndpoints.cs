using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.Endpoints;

/// <summary>The plan record of spec P7-03 (ADR 0083): read the plans and one with its tickets and trail. Read only; no order exists anywhere in the system, and nothing here changes a plan.</summary>
internal sealed class PlansEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/plans").WithTags("Plans");

        group.MapGet("/", ListAsync)
            .WithName("ListPlans")
            .WithSummary("The plans, newest session first, each in its current state; optional session date and account filters, limit 1 to 200 (default 50). Empty before the first. No order is placed by any plan. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/notices", NoticesAsync)
            .WithName("GetPlanNotices")
            .WithSummary("What the owner should know about the plan for the notification bell, derived at read time from the stored plan record, runs and fills: a plan awaiting review with the minutes to its stored cutoff, the nightly run that failed closed (gate and reason), tickets that lapsed, tickets armed or invalidated at pre-open, and approved tickets whose fills are not matched yet. Each item has an in-app route and no command, carries counts and no symbol, and is gone once the stored state changes. Read only; in-app only");

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetPlan")
            .WithSummary("One plan with its tickets (primary, reserve, rejected with reasons and evidence), size per account, the states they may move to and the trail of every state change. Armed means watched, never an order. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:long}/grade", GradeAsync)
            .WithName("GetPlanGrade")
            .WithSummary("The grade of every ticket of a plan (primary, reserve, rejected), each walked through the stored bars of its session and labelled 'simulated on plan prices', with the day's top-1/3/5/all curves and decision groups. A ticket without a stored grade is pending. Read only. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:long}/journal", JournalAsync)
            .WithName("GetPlanJournal")
            .WithSummary("The auto-journal of a plan: each approved ticket's fills the portfolio sync stored (matched by account, instrument, side and day), the fill grade ('realised fills, not simulated') beside the plan-price grade ('simulated on plan prices'), and the day's discretionary fills apart. Fills are only read; nothing is an order. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGroup("/reports").WithTags("Plans").MapGet("/decisions", DecisionsAsync)
            .WithName("GetDecisionsReport")
            .WithSummary("Top-1/3/5/all curves by rank, primary versus reserve versus rejected, and the owner's decisions (approved, modified, rejected, lapsed, undecided) over the graded plans of an optional date range, with sample-size notes. Labelled 'simulated on plan prices'. Read only. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var runs = api.MapGroup("/plan-runs").WithTags("Plans");
        runs.MapGet("/", ListRunsAsync)
            .WithName("ListPlanRuns")
            .WithSummary("The recorded attempts of the nightly pipeline, newest planned session first: published (with the plan) or failed closed (with the gate and the reason). Optional planned-session date, limit 1 to 200 (default 50). Read only. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        runs.MapGet("/phase-check", PhaseCheckAsync)
            .WithName("GetPlanPhaseCheck")
            .WithSummary("The P7 phase check from stored records: the last ten trading sessions (scheduled or manual run, gates, published or failed closed, tickets, manual runs), then each acceptance criterion as MET, NOT MET or NOT EVALUABLE with its numbers, the services and owner switches that are still off, and the honest limits. The exit is never MET on fewer than ten consecutive scheduled sessions. Optional 'through' planned-session date. Read only; writes nothing. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Ok<PlanNotices>> NoticesAsync(IQueryHandler<GetPlanNoticesQuery, PlanNotices> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetPlanNoticesQuery(), cancellationToken));

    private static async Task<Results<Ok<PlanPhaseCheckView>, ProblemHttpResult>> PhaseCheckAsync(
        DateOnly? through,
        HttpContext http,
        IQueryHandler<EvaluatePlanPhaseCheckQuery, Result<PlanPhaseCheckView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EvaluatePlanPhaseCheckQuery(through), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<PlanRunView>>, ProblemHttpResult>> ListRunsAsync(
        DateOnly? date,
        int? limit,
        HttpContext http,
        IQueryHandler<ListPlanRunsQuery, Result<IReadOnlyList<PlanRunView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListPlanRunsQuery(date, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<PlanView>>, ProblemHttpResult>> ListAsync(
        DateOnly? date,
        Guid? account,
        int? limit,
        HttpContext http,
        IQueryHandler<ListPlansQuery, Result<IReadOnlyList<PlanView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListPlansQuery(date, account, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PlanView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetPlanQuery, Result<PlanView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PlanGradeView>, ProblemHttpResult>> GradeAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetPlanGradeQuery, Result<PlanGradeView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanGradeQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<PlanJournalView>, ProblemHttpResult>> JournalAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetPlanJournalQuery, Result<PlanJournalView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPlanJournalQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<GradeReportData>, ProblemHttpResult>> DecisionsAsync(
        DateOnly? from,
        DateOnly? to,
        HttpContext http,
        IQueryHandler<GetDecisionsReportQuery, Result<GradeReportData>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDecisionsReportQuery(from, to), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == PlanErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
