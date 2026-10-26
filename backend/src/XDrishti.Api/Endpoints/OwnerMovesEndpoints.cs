using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Learning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The learning moves of the owner-acts forms (spec P8-19, ADR 0130): link, close and dismiss a lesson, approve, reject, expire or accept a hypothesis, revert the latest transition of a cell. Each is the existing handler behind the same state machine, reached after the capability
/// <c>learning_moves_ui</c> (off by default), a fresh password for THIS act and the owner from the session. A move writes one new insert-only row and nothing else: nothing is applied to a strategy, a plan, a cell's stats, a model or a setting, and the body names neither the actor
/// nor a result. Proposing, starting and judging a hypothesis, opening lessons and every other learning job stay verbs of the command line. Only this module (and the autonomy endpoint for its own setting) calls these handlers from the web.
/// </summary>
internal sealed class OwnerMovesEndpoints : IEndpointModule
{
    private const string Cap = Domain.Capabilities.Capabilities.LearningMovesUi;

    public void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/lessons/{id:long}/link", LinkAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithTags("Learning")
            .WithName("LinkLesson")
            .WithSummary("Link an OPEN lesson to a hypothesis or an experiment that is meant to fix it (fixKind hypothesis|experiment, fixId); applies nothing. Needs a session, the CSRF token, the capability learning_moves_ui and the password in the body, checked on EVERY request. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid, 404 lessons.not_found or lessons.fix_not_found, 409 lessons.move_refused or lessons.stale, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);

        api.MapPost("/lessons/{id:long}/close", CloseAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithTags("Learning")
            .WithName("CloseLesson")
            .WithSummary("Close a LINKED lesson. The body holds a reason and the password only: the result is read from the fix's stored experiment (improved only when it was Better), never typed. Same gate as the link. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid, 404 lessons.not_found, 409 lessons.move_refused (including a fix with no recorded experiment) or lessons.stale, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);

        api.MapPost("/lessons/{id:long}/dismiss", DismissAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithTags("Learning")
            .WithName("DismissLesson")
            .WithSummary("Set an open or linked lesson aside; the lesson is kept. Same gate as the link. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid, 404 lessons.not_found, 409 lessons.move_refused or lessons.stale, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);

        api.MapPost("/hypotheses/{id:long}/move", MoveHypothesisAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithTags("Learning")
            .WithName("MoveHypothesis")
            .WithSummary("Move a hypothesis to approved, rejected, expired or accepted by the table of the lifecycle (proposed, running and judged have their own commands and are refused here). Accepting records a verdict and applies nothing. Same gate as the lesson moves. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid, 404 hypotheses.not_found, 409 hypotheses.move_refused or hypotheses.stale, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);

        api.MapPost("/changelog/{id:long}/revert", RevertAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithTags("Learning")
            .WithName("RevertChangelogEntry")
            .WithSummary("Revert the LATEST transition of a cell with a NEW changelog row that points at it (rows are never changed). It only restores a less trusted state; it never makes a cell active, never un-retires, and changes no published plan. Same gate as the lesson moves. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid, 404 changelog.not_found, 409 changelog.not_revertible (every reason listed) or changelog.stale, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);
    }

    private static Task<Results<Ok<LessonMoveOutcome>, ProblemHttpResult>> LinkAsync(long id, LinkLessonRequest request, HttpContext http, ICapabilityGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IOwnerActRunner runner, ICommandHandler<LinkLessonCommand, LessonMoveOutcome> handler, CancellationToken cancellationToken) =>
        OwnerActFlow.RunAsync(http, gate, passwordCheck, runner, handler, Cap, request.Password, actor => Result.Success(new LinkLessonCommand(id, request.FixKind ?? string.Empty, request.FixId ?? 0, request.Reason ?? string.Empty, actor)), AccountEventType.LessonMoved, o => LessonDetail(o, "link"), cancellationToken);

    private static Task<Results<Ok<LessonMoveOutcome>, ProblemHttpResult>> CloseAsync(long id, OwnerMoveRequest request, HttpContext http, ICapabilityGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IOwnerActRunner runner, ICommandHandler<CloseLessonCommand, LessonMoveOutcome> handler, CancellationToken cancellationToken) =>
        OwnerActFlow.RunAsync(http, gate, passwordCheck, runner, handler, Cap, request.Password, actor => Result.Success(new CloseLessonCommand(id, request.Reason ?? string.Empty, actor)), AccountEventType.LessonMoved, o => LessonDetail(o, "close"), cancellationToken);

    private static Task<Results<Ok<LessonMoveOutcome>, ProblemHttpResult>> DismissAsync(long id, OwnerMoveRequest request, HttpContext http, ICapabilityGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IOwnerActRunner runner, ICommandHandler<DismissLessonCommand, LessonMoveOutcome> handler, CancellationToken cancellationToken) =>
        OwnerActFlow.RunAsync(http, gate, passwordCheck, runner, handler, Cap, request.Password, actor => Result.Success(new DismissLessonCommand(id, request.Reason ?? string.Empty, actor)), AccountEventType.LessonMoved, o => LessonDetail(o, "dismiss"), cancellationToken);

    // The autonomy level of a web move is a constant: supervised (a person decides). A body cannot raise it.
    private static Task<Results<Ok<HypothesisMoveOutcome>, ProblemHttpResult>> MoveHypothesisAsync(long id, MoveHypothesisRequest request, HttpContext http, ICapabilityGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IOwnerActRunner runner, ICommandHandler<MoveHypothesisCommand, HypothesisMoveOutcome> handler, CancellationToken cancellationToken) =>
        OwnerActFlow.RunAsync(http, gate, passwordCheck, runner, handler, Cap, request.Password, actor => HypothesisMove(id, request, actor), AccountEventType.HypothesisMoved, o => string.Create(CultureInfo.InvariantCulture, $"hypothesis={o.Id} to={o.State}"), cancellationToken);

    private static Task<Results<Ok<ChangelogOutcome>, ProblemHttpResult>> RevertAsync(long id, OwnerMoveRequest request, HttpContext http, ICapabilityGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IOwnerActRunner runner, ICommandHandler<RevertChangelogCommand, ChangelogOutcome> handler, CancellationToken cancellationToken) =>
        OwnerActFlow.RunAsync(http, gate, passwordCheck, runner, handler, Cap, request.Password, actor => Result.Success(new RevertChangelogCommand(id, request.Reason ?? string.Empty, actor)), AccountEventType.ChangelogReverted, o => string.Create(CultureInfo.InvariantCulture, $"changelog={o.Id} reverts={id} {o.From}->{o.To}"), cancellationToken);

    private static Result<MoveHypothesisCommand> HypothesisMove(long id, MoveHypothesisRequest request, string actor) =>
        HypothesisStateText.TryParse(request.To) is { } to
            ? Result.Success(new MoveHypothesisCommand(id, to, request.Reason ?? string.Empty, actor, AutonomyLevel.Supervised))
            : Result.Failure<MoveHypothesisCommand>(LearningErrors.Invalid([new FieldError("to", "Use approved, rejected, expired or accepted here; running and judged have their own commands.")]));

    private static string LessonDetail(LessonMoveOutcome outcome, string verb) => string.Create(CultureInfo.InvariantCulture, $"lesson={outcome.Id} {verb} to={outcome.State}");
}
