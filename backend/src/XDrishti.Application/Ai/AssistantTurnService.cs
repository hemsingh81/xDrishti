using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Grounding;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>
/// The assistant's turn (spec P9-03, ADR 0138): the refusals in words, then the loop of model, tool calls and answer. The model reaches only the tools of the tool server, within the budget the owner set; the turn writes nothing but its own audit rows. The answer is checked figure by figure against
/// the tool results of the turn and the unmatched ones are marked, not removed. Everything outside the loop (the model, the tool server, the store, the clock) is a port.
/// </summary>
internal sealed class AssistantTurnService(
    IAssistantSettingsStore settingsStore,
    ILlmHealth llmHealth,
    IToolCatalog toolCatalog,
    INightlyRunProbe nightly,
    IConversationStore conversations,
    IChatModel model,
    TurnGate gate,
    TurnOptions options,
    TimeProvider time,
    ILogger<AssistantTurnService> logger) : IAssistantTurnRunner
{
    public const string ToolLimitSentence = "I used all the tool calls I am allowed for one question and could not finish an answer. Ask a narrower question or raise the budget in the settings.";
    private const int ResultsReserveTokens = 3000;
    private const int SlotLeaseMarginSeconds = 30;
    private const int MaxStoredResultBytes = 64 * 1024;
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private TurnOptions Options => options;

    private IConversationStore Conversations => conversations;

    private ILogger Logger => logger;

    private IChatModel Model => model;

    private TimeProvider Time => time;

    private IToolCatalog Catalog => toolCatalog;

    public async ValueTask<Result<ITurnSession>> BeginAsync(TurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await settingsStore.CurrentAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Refuse(Error.Conflict("assistant.disabled", "The assistant is off. The owner can switch it on in the assistant settings."));
        }

        if (!IsValid(request))
        {
            return Refuse(Error.Validation("assistant.invalid", $"The message must be 1 to {options.MaxMessageChars} characters, with no control character."));
        }

        if (request.ConversationId is { } existing && !await conversations.ExistsAsync(existing, cancellationToken))
        {
            return Refuse(Error.NotFound("assistant.not_found", "That conversation does not exist."));
        }

        var llm = await llmHealth.GetStatusAsync(cancellationToken);
        if (llm.State != LlmState.Ready)
        {
            return Refuse(new Error("assistant.model_unavailable", "The model is not running.", ErrorKind.Upstream, Payload: new Dictionary<string, object> { ["state"] = llm.State.ToString().ToLowerInvariant() }));
        }

        var tools = await toolCatalog.ListAsync(cancellationToken);
        if (tools.State != ToolCatalogState.Ready)
        {
            return Refuse(new Error("assistant.tools_unavailable", "The tool server is not reachable.", ErrorKind.Upstream));
        }

        // make_chart is local: it needs this turn's stored results, which the stateless tool server does not hold (spec P9-05).
        tools = tools with { Tools = [.. tools.Tools, ChartTool.Descriptor] };

        if (await nightly.IsRunningAsync(TimeSpan.FromMinutes(options.NightlyGuardMinutes), cancellationToken))
        {
            return Refuse(Error.Conflict("assistant.nightly_running", "The nightly run is in progress; ask again when it has finished."));
        }

        var slot = gate.TryEnter();
        if (slot is null)
        {
            return Refuse(Error.Conflict("assistant.busy", "Another question is being answered; wait for it to finish."));
        }

        try
        {
            var system = SystemText();
            var fixedTokens = ContextBudget.EstimateTokens(system) + ContextBudget.SchemaTokens(tools.Tools) + ContextBudget.EstimateTokens(request.Question);
            var room = options.PromptBudgetTokens - ResultsReserveTokens - fixedTokens;
            if (room < 0)
            {
                slot.Dispose();
                return Refuse(Error.Validation("assistant.too_long", "The question does not fit the prompt budget; shorten it."));
            }

            var history = request.ConversationId is { } id && options.HistoryMessages > 0
                ? ContextBudget.History(await conversations.RecentAsync(id, options.HistoryMessages, cancellationToken), Math.Min(ContextBudget.HistoryTokenLimit, room))
                : [];
            return Result.Success<ITurnSession>(new Session(this, request, settings, llm, tools, system, history, slot, time.CreateTimer(_ => slot.Dispose(), null, TimeSpan.FromSeconds(options.TurnTimeoutSeconds + SlotLeaseMarginSeconds), Timeout.InfiniteTimeSpan)));
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    private Result<ITurnSession> Refuse(Error error)
    {
        AssistantLog.Refused(logger, error.Code);
        return Result.Failure<ITurnSession>(error);
    }

    private bool IsValid(TurnRequest request) =>
        !string.IsNullOrWhiteSpace(request.Question) && request.Question.Length <= options.MaxMessageChars && !request.Question.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
        && request.Origin is TurnRequest.Chat or TurnRequest.Eval or TurnRequest.Job && !string.IsNullOrWhiteSpace(request.Actor);

    private string SystemText()
    {
        var now = time.GetUtcNow().ToOffset(India);
        return $"{PromptRegistry.Chat.Text}\n{now.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)} IST.";
    }

    private sealed class Session(
        AssistantTurnService owner,
        TurnRequest request,
        AssistantSettings settings,
        LlmStatus llm,
        ToolCatalogSnapshot tools,
        string system,
        IReadOnlyList<ModelMessage> history,
        IDisposable slot,
        ITimer lease) : ITurnSession
    {
        public ValueTask DisposeAsync()
        {
            lease.Dispose();
            slot.Dispose();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<TurnResult> RunAsync(Func<TurnEvent, ValueTask> sink, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sink);
            var o = owner.Options;
            var clock = Stopwatch.StartNew();
            var prompt = PromptRegistry.Chat;
            var turnId = Guid.NewGuid();
            var started = await owner.Conversations.StartTurnAsync(
                new StartTurnRecord(request.ConversationId, turnId, request.Actor, request.Origin, request.Question, prompt.Sha256, prompt.Name, prompt.Version, prompt.Text, settings.Id, llm.Model ?? "unknown", llm.Build), CancellationToken.None);
            AssistantLog.TurnStart(owner.Logger, started.ConversationId, turnId, history.Count, tools.Tools.Count);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(o.TurnTimeoutSeconds));
            var token = timeout.Token;
            var clientGone = false;

            async ValueTask Emit(TurnEvent e)
            {
                if (clientGone)
                {
                    return;
                }

                try
                {
                    await sink(e);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    clientGone = true;
                    timeout.Cancel();
                }
            }

            var budget = new ToolBudget(settings.MaxToolCalls);
            var results = new ToolResultBudget(o.ToolResultTextBudget, o.ToolResultCutChars);
            var messages = new List<ModelMessage> { new(ModelRole.System, system) };
            messages.AddRange(history);
            messages.Add(new ModelMessage(ModelRole.User, request.Question));
            var resultTexts = new List<string>();
            var resultsByCall = new Dictionary<int, string>();
            var iterationText = new StringBuilder();
            var finish = "stop";
            int? promptTokens = null;
            int? completionTokens = null;
            int? firstToken = null;
            string? errorCode = null;
            var answer = string.Empty;
            var calls = 0;
            var maxIterations = settings.MaxToolCalls + 2;
            var warned = false;

            await Emit(new TurnStarted(started.ConversationId, turnId, started.UserMessageId, prompt.Sha256, new TurnModel(llm.Model ?? "unknown", llm.ModelSha256), settings.Id));
            await Emit(new TurnStatus("reading", null));
            try
            {
                for (var iteration = 1; ; iteration++)
                {
                    if (iteration > maxIterations)
                    {
                        finish = "tool_limit";
                        answer = iterationText.Length > 0 ? iterationText.ToString() : ToolLimitSentence;
                        break;
                    }

                    iterationText.Clear();
                    var requested = new List<ModelToolCall>();
                    var reason = "stop";
                    var modelRequest = new ModelRequest(messages, tools.Tools, o.MaxAnswerTokens, (float)o.Temperature, o.Seed);
                    await foreach (var part in owner.Model.StreamAsync(modelRequest, token))
                    {
                        switch (part)
                        {
                            case ModelTextPart text when text.Text.Length > 0:
                                firstToken ??= (int)clock.ElapsedMilliseconds;
                                iterationText.Append(text.Text);
                                await Emit(new TurnToken(iteration, text.Text));
                                break;
                            case ModelToolCallPart call:
                                requested.Add(call.Call);
                                break;
                            case ModelFinishPart done:
                                reason = done.Reason;
                                promptTokens = Add(promptTokens, done.PromptTokens);
                                completionTokens = Add(completionTokens, done.CompletionTokens);
                                break;
                            default:
                                break;
                        }
                    }

                    if (requested.Count == 0)
                    {
                        answer = iterationText.ToString();
                        finish = reason == "length" ? "length" : "stop";
                        break;
                    }

                    if (iterationText.Length > 0)
                    {
                        await Emit(new TurnIterationDiscard(iteration));
                    }

                    if (warned)
                    {
                        // The model was told its budget was used and still asks for tools: stop (spec AC 4).
                        finish = "tool_limit";
                        answer = ToolLimitSentence;
                        iterationText.Clear();
                        break;
                    }

                    messages.Add(new ModelMessage(ModelRole.Assistant, string.Empty, requested));
                    foreach (var call in requested)
                    {
                        var executed = await RunToolAsync(call, budget, results, resultsByCall, started.ConversationId, turnId, Emit, token);
                        calls++;
                        warned |= executed.BudgetRefused;
                        if (executed.Granted && !string.Equals(call.Name, ChartTool.Name, StringComparison.Ordinal))
                        {
                            resultTexts.Add(executed.ModelText);
                        }

                        messages.Add(new ModelMessage(ModelRole.Tool, executed.ModelText, null, call.Id));
                    }

                    iterationText.Clear();
                    await Emit(new TurnStatus("answering", null));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || clientGone)
            {
                finish = "cancelled";
            }
            catch (OperationCanceledException)
            {
                finish = "error";
                errorCode = "assistant.timeout";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                finish = "error";
                errorCode = "assistant.model_error";
            }

            var total = (int)clock.ElapsedMilliseconds;
            var interrupted = finish is "cancelled" or "error";
            if (interrupted)
            {
                answer = iterationText.ToString();
            }

            var grounding = interrupted
                ? GroundingResult.NotChecked
                : GroundingCheck.Check(answer, request.Question, resultTexts);
            var wire = new TurnGrounding(grounding.Status.ToCode(), grounding.Checked, grounding.Ungrounded);
            var spans = JsonSerializer.Serialize(grounding.Ungrounded.Select(s => new { start = s.Start, end = s.End, text = s.Text }));
            var messageId = await owner.Conversations.CompleteTurnAsync(
                new AssistantMessageRecord(started.ConversationId, turnId, answer, prompt.Sha256, llm.Model ?? "unknown", promptTokens, completionTokens, firstToken, total, finish, grounding.Status.ToCode(), spans), CancellationToken.None);
            AssistantLog.TurnEnd(owner.Logger, turnId, finish, wire.Status, wire.Checked, wire.Ungrounded.Count, calls, promptTokens, completionTokens, total);
            if (finish == "cancelled")
            {
                AssistantLog.Cancelled(owner.Logger, turnId);
            }

            var usage = new TurnUsage(promptTokens, completionTokens);
            if (errorCode is not null)
            {
                await Emit(new TurnError(errorCode, errorCode == "assistant.timeout" ? "The model took too long to answer." : "The model failed while answering."));
            }
            else if (finish != "cancelled")
            {
                await Emit(new TurnDone(messageId, finish, wire, usage, firstToken, total));
            }

            return new TurnResult(messageId, answer, wire, calls, usage, finish, firstToken, total);
        }

        private async ValueTask<Executed> RunToolAsync(ModelToolCall call, ToolBudget budget, ToolResultBudget results, Dictionary<int, string> resultsByCall, Guid conversationId, Guid turnId, Func<TurnEvent, ValueTask> emit, CancellationToken cancellationToken)
        {
            var granted = budget.TryTake();
            var index = budget.Asked;
            var argumentsElement = ParseArguments(call.ArgumentsJson);
            await emit(new TurnToolCall(index, call.Name, argumentsElement));
            var startedAt = owner.Time.GetUtcNow();
            var clock = Stopwatch.StartNew();
            string text;
            var budgetRefused = false;
            if (!granted)
            {
                budgetRefused = true;
                text = Failure(call.Name, "tool.budget_exhausted", "The budget of tool calls for this question is used; answer with what you have or say what is missing.");
            }
            else if (!tools.Tools.Any(t => string.Equals(t.Name, call.Name, StringComparison.Ordinal)))
            {
                granted = false;
                text = Failure(call.Name, "tool.unknown", "That tool does not exist.");
            }
            else if (string.Equals(call.Name, ChartTool.Name, StringComparison.Ordinal))
            {
                text = ChartTool.Run(call.ArgumentsJson, resultsByCall, startedAt, out var chartReason);
                if (chartReason is not null)
                {
                    AssistantLog.ChartRejected(owner.Logger, chartReason);
                }
            }
            else
            {
                var outcome = await owner.Catalog.CallAsync(call.Name, call.ArgumentsJson, cancellationToken);
                text = outcome.Reachable ? outcome.Text : Failure(call.Name, ToolErrorCodes.Unavailable, "The tool server could not be reached.");
            }

            var ms = (int)clock.ElapsedMilliseconds;
            if (granted)
            {
                resultsByCall[index] = text;
            }

            var facts = ToolResultReader.Read(text);
            var status = StatusOf(facts, granted);
            var stored = text;
            var bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes > MaxStoredResultBytes)
            {
                stored = JsonSerializer.Serialize(new { stored = "cut", bytes });
            }

            var fitted = results.Fit(text);
            var record = new ToolCallRecord(conversationId, turnId, index, call.Name, argumentsElement.GetRawText(), status, facts.ErrorCode, stored, bytes, facts.Rows, facts.Truncated || fitted.Cut, facts.EvidenceJson, facts.Redactions, facts.InjectionFlags, startedAt, ms);
            var id = await owner.Conversations.RecordToolCallAsync(record, CancellationToken.None);
            AssistantLog.ToolCall(owner.Logger, call.Name, status, ms);
            await emit(new TurnToolResult(index, id, status, facts.Rows, facts.Truncated || fitted.Cut, ms, facts.Evidence, facts.ErrorCode));
            return new Executed(granted, budgetRefused, fitted.Text);
        }

        private static string StatusOf(ToolResultFacts facts, bool granted)
        {
            if (!granted)
            {
                return "refused";
            }

            if (facts.Ok)
            {
                return "ok";
            }

            return facts.ErrorCode switch
            {
                "tool.timeout" or "sql.timeout" => "timeout",
                "assistant.disabled" => "disabled",
                _ => "error",
            };
        }

        private static string Failure(string tool, string code, string message) =>
            JsonSerializer.Serialize(new { ok = false, tool, rows = (object?)null, data = (object?)null, rowCount = 0, truncated = false, error = new { code, message } });

        private static JsonElement ParseArguments(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                using var wrapped = JsonDocument.Parse(JsonSerializer.Serialize(json.Length > 500 ? json[..500] : json));
                return wrapped.RootElement.Clone();
            }
        }

        private static int? Add(int? total, int? more) => more is null ? total : (total ?? 0) + more;
    }

    private sealed record Executed(bool Granted, bool BudgetRefused, string ModelText);
}
