using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The lesson log of spec P6-16 (ADR 0078): read the lessons and one with its history. Writing is the <c>xd learn lesson</c> verb; nothing here changes a lesson, runs a test or applies a change.</summary>
internal sealed class LessonsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/lessons").WithTags("Learning");

        group.MapGet("/", ListAsync)
            .WithName("ListLessons")
            .WithSummary("The lessons (a problem a replayed monitor found, the fix linked and the stored result), newest first, each in its current state (open, linked, closed, dismissed); optional state filter, limit 1 to 200 (default 50). Empty before the first. 422 learning.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:long}", GetAsync)
            .WithName("GetLesson")
            .WithSummary("One lesson with its history, the states it may move to and the before and after numbers of its closing. Linking or closing applies nothing. 404 lessons.not_found")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<LessonView>>, ProblemHttpResult>> ListAsync(
        string? state,
        int? limit,
        HttpContext http,
        IQueryHandler<ListLessonsQuery, Result<IReadOnlyList<LessonView>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListLessonsQuery(state, limit ?? 50), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<LessonView>, ProblemHttpResult>> GetAsync(
        long id,
        HttpContext http,
        IQueryHandler<GetLessonQuery, Result<LessonView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetLessonQuery(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
