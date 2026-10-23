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

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetPlan")
            .WithSummary("One plan with its tickets (primary, reserve, rejected with reasons and evidence), size per account, the states they may move to and the trail of every state change. Armed means watched, never an order. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:long}/grade", GradeAsync)
            .WithName("GetPlanGrade")
            .WithSummary("The grade of every ticket of a plan (primary, reserve, rejected), each walked through the stored bars of its session and labelled 'simulated on plan prices', with the day's top-1/3/5/all curves and decision groups. A ticket without a stored grade is pending. Read only. 404 plans.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGroup("/reports").WithTags("Plans").MapGet("/decisions", DecisionsAsync)
            .WithName("GetDecisionsReport")
            .WithSummary("Top-1/3/5/all curves by rank, primary versus reserve versus rejected, and the owner's decisions (approved, modified, rejected, lapsed, undecided) over the graded plans of an optional date range, with sample-size notes. Labelled 'simulated on plan prices'. Read only. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        api.MapGroup("/plan-runs").WithTags("Plans").MapGet("/", ListRunsAsync)
            .WithName("ListPlanRuns")
            .WithSummary("The recorded attempts of the nightly pipeline, newest planned session first: published (with the plan) or failed closed (with the gate and the reason). Optional planned-session date, limit 1 to 200 (default 50). Read only. 422 plans.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
