using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The AI assistant's surface (specs P9-01, P9-03, ADR 0136, 0138): the state of the model server, the status of everything the assistant depends on, the streamed chat, the owner's settings change, and the stored turns. Every route needs a session and every POST/PUT the CSRF token.
/// The assistant is off by default: chat answers 409 <c>assistant.disabled</c> and calls nothing. It writes nothing but its own audit rows. The settings change is the owner's act: a fresh password for every change that turns something on or raises it, none for switching off.
/// </summary>
internal sealed class AssistantEndpoints : IEndpointModule
{
    public const string ChatRateLimitPolicy = "assistant-chat";

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/assistant").WithTags("Assistant");

        group.MapGet("/llm-status", GetLlmStatusAsync)
            .WithName("GetLlmStatus")
            .WithSummary("Whether the local model server is ready, loading, down or misconfigured, with the loaded model file (and its SHA-256 when it is a pinned one), the build and how to start it. Read only; cached a few seconds; no key, path or address in the answer")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetAssistantStatus")
            .WithSummary("The assistant's settings in force (off by default), the model server, the tool server (not asked while the assistant is off), whether a turn is running, whether the nightly run blocks it, and the evaluation (not_yet_evaluated until P9-09). Read only")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapPost("/chat", ChatAsync)
            .RequireRateLimiting(ChatRateLimitPolicy)
            .WithName("ChatWithAssistant")
            .WithSummary("Ask the assistant; the answer is a text/event-stream of turn, status, tool_call, tool_result, token, iteration_discard, ping, done and error events. Refusals come before the stream as problems: 409 assistant.disabled, assistant.busy, assistant.nightly_running; 503 assistant.model_unavailable (with state), assistant.tools_unavailable; 422 assistant.invalid, assistant.too_long; 404 assistant.not_found; 429 auth.rate_limited. Nothing is stored for a refusal")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/settings", ChangeSettingsAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeAssistantSettings")
            .WithSummary("Change the assistant's switches as the owner; appends one audited row and answers the status. Needs a session and the CSRF token. A change that only reduces exposure (assistant off, portfolio context off, a lower budget) needs no password; any change that turns something on or raises it needs the password in the body, checked on EVERY such change (an earlier step-up window does not count). 401 auth.invalid_credentials, 423 auth.locked, 403 assistant.owner_only, 400 assistant.values_invalid or assistant.reason_invalid, 409 assistant.stale, 429 auth.rate_limited")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapGet("/conversations", ListConversationsAsync)
            .WithName("ListAssistantConversations")
            .WithSummary("The newest stored conversations (limit 1 to 50, default 20). Read only; nothing edits or deletes a conversation")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapGet("/conversations/{id:guid}", GetConversationAsync)
            .WithName("GetAssistantConversation")
            .WithSummary("One conversation: its messages in order, each assistant message with its tool-call summaries, the grounding status and the figures that were not found in the tool results. 404 assistant.not_found")
            .ProducesRejections(conflict: false);

        group.MapGet("/tool-calls/{id:long}", GetToolCallAsync)
            .WithName("GetAssistantToolCall")
            .WithSummary("One stored tool call with its arguments and the result envelope (up to 64 KB), as the audit keeps it. 404 assistant.not_found")
            .ProducesRejections(conflict: false);
    }

    private static async Task<Ok<LlmStatusView>> GetLlmStatusAsync(HttpContext http, IQueryHandler<GetLlmStatusQuery, LlmStatusView> handler, CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(new GetLlmStatusQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(view);
    }

    private static async Task<Ok<AssistantStatusView>> GetStatusAsync(HttpContext http, IQueryHandler<GetAssistantStatusQuery, AssistantStatusView> handler, CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(new GetAssistantStatusQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(view);
    }

    private static async Task<Results<ServerSentEventsResult<string>, ProblemHttpResult>> ChatAsync(
        AssistantChatRequest request, HttpContext http, IAssistantTurnRunner runner, TurnOptions options, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var begun = await runner.BeginAsync(new TurnRequest(request.Message ?? string.Empty, request.ConversationId, TurnRequest.Chat, owner.Value), cancellationToken);
        if (begun.IsFailure)
        {
            return Refusal(begun.Error, http);
        }

        return TypedResults.ServerSentEvents(AssistantStream.EventsAsync(begun.Value, TimeSpan.FromSeconds(options.PingSeconds), http.RequestAborted));
    }

    private static async Task<Results<Ok<AssistantStatusView>, ProblemHttpResult>> ChangeSettingsAsync(
        AssistantSettingsRequest request,
        HttpContext http,
        IQueryHandler<AssistantSettingsNeedPasswordQuery, bool> needsPassword,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeAssistantSettingsCommand, AssistantStatusView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // Switching something off or lowering it is free (the owner can always switch it off); anything that turns something on or raises it needs the password for this change.
        var raises = await needsPassword.HandleAsync(new AssistantSettingsNeedPasswordQuery(request.Enabled, request.PortfolioContext, request.MaxToolCalls), cancellationToken);
        var owner = raises ? await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken) : OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(
            new ChangeAssistantSettingsCommand(owner.Value, request.Reason ?? string.Empty, request.Enabled, request.PortfolioContext, request.MaxToolCalls, request.ExpectedSettingsId, PasswordVerified: raises), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<ConversationSummary>>> ListConversationsAsync(
        HttpContext http, IQueryHandler<ListAssistantConversationsQuery, IReadOnlyList<ConversationSummary>> handler, CancellationToken cancellationToken, int limit = ListAssistantConversationsQuery.DefaultLimit)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new ListAssistantConversationsQuery(limit), cancellationToken));
    }

    private static async Task<Results<Ok<ConversationDetail>, ProblemHttpResult>> GetConversationAsync(
        Guid id, HttpContext http, IQueryHandler<GetAssistantConversationQuery, Result<ConversationDetail>> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetAssistantConversationQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<StoredToolCall>, ProblemHttpResult>> GetToolCallAsync(
        long id, HttpContext http, IQueryHandler<GetAssistantToolCallQuery, Result<StoredToolCall>> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new GetAssistantToolCallQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refusal before the stream: 422 for an invalid or too long question, 503 for a model or tool server that is not available (the state in the body); the rest by the shared mapping. A code and a fixed sentence, never an exception text.</summary>
    private static ProblemHttpResult Refusal(Error error, HttpContext http)
    {
        var status = error.Code switch
        {
            "assistant.invalid" or "assistant.too_long" => StatusCodes.Status422UnprocessableEntity,
            "assistant.model_unavailable" or "assistant.tools_unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => 0,
        };
        if (status == 0)
        {
            return ProblemResults.From(error, http);
        }

        var problem = ProblemResults.Create(status, error.Message, error.Code);
        foreach (var (name, value) in error.Payload ?? new Dictionary<string, object>())
        {
            problem.Extensions[name] = value;
        }

        return TypedResults.Problem(problem);
    }
}
