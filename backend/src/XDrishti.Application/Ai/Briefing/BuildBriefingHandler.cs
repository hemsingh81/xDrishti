using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>
/// Builds and stores one briefing or weekly review (spec P9-06 AC 8 to 10, ADR 0140). The facts and the fixed-format text are deterministic; the model, when it may be used at all, only phrases, and the guard rejects any text it cannot verify against the facts, after which the
/// fixed-format text is stored instead. No model call is made unless <c>ai.settings.enabled</c> AND <c>Assistant:Briefing:UseModel</c>. A run that cannot start (the nightly run is going, a chat turn is going, the clock is inside the nightly window) waits a while and then stores nothing.
/// The only write is the insert-only briefing and its attempts; there is no order, approval or plan change here.
/// </summary>
internal sealed class BuildBriefingHandler(
    IBriefingFactSource source,
    IBriefingStore store,
    IBriefingWriter writer,
    IAssistantSettingsStore settingsStore,
    ILlmHealth health,
    INightlyRunProbe nightly,
    IModelCallAudit audit,
    TurnGate gate,
    TurnOptions turn,
    BriefingOptions options,
    TimeProvider time,
    ILogger<BuildBriefingHandler> logger) : ICommandHandler<BuildBriefingCommand, BriefingView>
{
    public const string ReasonModelOff = "model_off";
    public const string ReasonModelUnavailable = "model_unavailable";
    public const string ReasonGuardRejected = "guard_rejected";
    public const string ReasonFactsThin = "facts_thin";

    private const int MaxOutputDaily = 700;
    private const int MaxOutputWeekly = 900;
    private static readonly TimeOnly WindowStart = new(17, 30);
    private static readonly TimeSpan GatePoll = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadyPoll = TimeSpan.FromSeconds(5);

    public async ValueTask<Result<BriefingView>> HandleAsync(BuildBriefingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var resolved = await BriefingSubjects.ResolveAsync(command.Kind, command.Subject, source, now, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<BriefingView>(resolved.Error);
        }

        var kind = command.Kind;
        var subject = resolved.Value;
        if (BriefingSubjects.StoreRefusal(kind, subject, now) is { } refusal)
        {
            return Result.Failure<BriefingView>(BriefingErrors.Invalid("subject", refusal));
        }

        var settings = await settingsStore.CurrentAsync(cancellationToken);
        var built = await source.BuildAsync(kind, subject, settings.Enabled && settings.PortfolioContext, cancellationToken);
        if (built.IsFailure)
        {
            return Result.Failure<BriefingView>(built.Error);
        }

        var facts = built.Value.Facts;
        BriefingLog.Facts(logger, kind, subject, facts.Lines.Count, facts.Tickets.Count, built.Value.InjectionFlags);
        var existing = await store.FindAsync(kind, subject, cancellationToken);
        if (existing is not null && !command.NewVersion)
        {
            BriefingLog.Repeat(logger, kind, subject, existing.Id);
            return Result.Success(BriefingView.Of(existing, true));
        }

        var maxWords = kind == BriefingFacts.Daily ? options.DailyMaxWords : options.WeeklyMaxWords;
        var deterministic = kind == BriefingFacts.Daily ? DeterministicBriefing.Build(facts, maxWords) : DeterministicWeeklyReview.Build(facts, maxWords);
        var decision = await DecideAsync(command, facts, settings.Enabled, maxWords, now, cancellationToken);
        if (decision.Deferred)
        {
            BriefingLog.Deferred(logger, kind, subject, decision.Why ?? "gate");
            return Result.Failure<BriefingView>(Error.Conflict(BriefingErrors.DeferredCode, $"The model text of {kind} {subject} was not started ({decision.Why}); nothing was stored. Run it again later."));
        }

        var text = decision.ModelText is null ? deterministic : decision.ModelText + "\n\n" + (kind == BriefingFacts.Daily ? BriefingWording.DailyFooter : BriefingWording.WeeklyFooter);
        var factsJson = BriefingFactsCodec.Json(facts);
        var parameters = JsonSerializer.Serialize(new
        {
            temperature = 0,
            seed = options.Seed,
            maxOutputTokens = kind == BriefingFacts.Daily ? MaxOutputDaily : MaxOutputWeekly,
            thinking = "the model server's setting",
            cachePrompt = "the server default; the client does not set it",
            maxAttempts = options.MaxAttempts,
        });
        var record = new NewBriefing(
            kind, subject, decision.ModelText is null ? "deterministic" : "model", decision.ModelText is null ? decision.Reason : null, text, deterministic, factsJson, BriefingFactsCodec.Hash(factsJson),
            decision.ModelText is null ? null : decision.ModelId, decision.ModelText is null ? null : decision.ModelSha256, "briefing." + PromptRegistry.Briefing.Version, parameters, TextGuard.WordCount(text), command.Actor, decision.Attempts, command.NewVersion);
        var (stored, created) = await store.InsertAsync(record, cancellationToken);
        if (created)
        {
            BriefingLog.Stored(logger, kind, subject, stored.Version, stored.Source, stored.Reason, stored.WordCount, stored.Attempts.Count);
        }
        else
        {
            BriefingLog.Repeat(logger, kind, subject, stored.Id);
        }

        return Result.Success(BriefingView.Of(stored, !created));
    }

    private async ValueTask<Decision> DecideAsync(BuildBriefingCommand command, BriefingFacts facts, bool settingsEnabled, int maxWords, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The safety gate: both switches, else no model call at all.
        if (command.ForceDeterministic || !options.UseModel || !settingsEnabled)
        {
            return Decision.Text(ReasonModelOff);
        }

        if (facts.Kind == BriefingFacts.Daily && facts.Tickets.Count == 0)
        {
            return Decision.Text(ReasonFactsThin);
        }

        var slot = await WaitForGatesAsync(facts.Kind, cancellationToken);
        if (slot.Why is not null)
        {
            return Decision.Defer(slot.Why);
        }

        using (slot.Slot)
        {
            var status = await WaitForModelAsync(cancellationToken);
            if (status.State != LlmState.Ready)
            {
                return Decision.Text(ReasonModelUnavailable);
            }

            return await AttemptsAsync(command, facts, status, maxWords, cancellationToken);
        }
    }

    private async ValueTask<(IDisposable? Slot, string? Why)> WaitForGatesAsync(string kind, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow().AddMinutes(options.GateWaitMinutes);
        while (true)
        {
            string? why = null;
            var clock = TimeOnly.FromDateTime(IndiaTime.ToIst(time.GetUtcNow()).DateTime);
            if (kind == BriefingFacts.Daily && clock >= WindowStart && clock < options.EarliestTime)
            {
                why = "before_earliest_time";
            }
            else if (await nightly.IsRunningAsync(TimeSpan.FromMinutes(turn.NightlyGuardMinutes), cancellationToken))
            {
                why = "nightly_running";
            }
            else if (gate.TryEnter() is { } slot)
            {
                return (slot, null);
            }
            else
            {
                why = "chat_turn_running";
            }

            if (time.GetUtcNow() >= deadline)
            {
                return (null, why);
            }

            await Task.Delay(GatePoll, time, cancellationToken);
        }
    }

    private async ValueTask<LlmStatus> WaitForModelAsync(CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow().AddSeconds(options.ReadyWaitSeconds);
        while (true)
        {
            var status = await health.GetStatusAsync(cancellationToken);
            if (status.State == LlmState.Ready || time.GetUtcNow() >= deadline)
            {
                return status;
            }

            await Task.Delay(ReadyPoll, time, cancellationToken);
        }
    }

    private async ValueTask<Decision> AttemptsAsync(BuildBriefingCommand command, BriefingFacts facts, LlmStatus status, int maxWords, CancellationToken cancellationToken)
    {
        var modelId = status.ModelFile ?? status.Model ?? "unknown";
        var attempts = new List<BriefingAttemptRecord>();
        IReadOnlyList<GuardFinding>? refused = null;
        for (var n = 1; n <= options.MaxAttempts; n++)
        {
            var request = new BriefingRequest(PromptRegistry.Briefing.Text, BriefingPrompt.User(facts, maxWords, refused), facts.Kind == BriefingFacts.Daily ? MaxOutputDaily : MaxOutputWeekly, options.Seed);
            BriefingWrite written;
            try
            {
                written = await writer.WriteAsync(request, cancellationToken);
            }
            catch (ModelUnavailableException)
            {
                BriefingLog.ModelFailed(logger, facts.Kind, facts.Subject, n);
                await audit.RecordAsync(new ModelCallRecord("briefing", command.Actor, PromptRegistry.Briefing.Sha256, modelId, status.ModelSha256, null, null, 0, "error", "error", "model_unavailable"), cancellationToken);
                return new Decision(false, null, ReasonModelUnavailable, null, null, null, attempts);
            }

            var text = written.Text.Trim();
            var guard = TextGuard.Check(text, facts, maxWords);
            var findings = guard.Findings.ToList();
            if (written.FinishReason == "length")
            {
                findings.Add(new GuardFinding(GuardCodes.FormTooLong, "cut", text.Length));
            }

            var passed = findings.Count == 0;
            attempts.Add(new BriefingAttemptRecord(n, text, passed, [.. findings.Select(f => new StoredFinding(f.Code, f.Token, f.Offset))], written.LatencyMs, written.PromptTokens, written.CompletionTokens));
            var codes = Codes(findings);
            var count = findings.Count;
            BriefingLog.Attempt(logger, facts.Kind, facts.Subject, n, passed, count, codes, written.LatencyMs);
            await audit.RecordAsync(new ModelCallRecord("briefing", command.Actor, PromptRegistry.Briefing.Sha256, modelId, status.ModelSha256, written.PromptTokens, written.CompletionTokens, written.LatencyMs, written.FinishReason, "ok", null), cancellationToken);
            if (passed)
            {
                return new Decision(false, null, null, text, modelId, status.ModelSha256, attempts);
            }

            refused = findings;
        }

        return new Decision(false, null, ReasonGuardRejected, null, null, null, attempts);
    }

    private static string Codes(List<GuardFinding> findings) => string.Join(',', findings.Select(f => f.Code).Distinct(StringComparer.Ordinal));

    private sealed record Decision(bool Deferred, string? Why, string? Reason, string? ModelText, string? ModelId, string? ModelSha256, IReadOnlyList<BriefingAttemptRecord> Attempts)
    {
        public static Decision Text(string reason) => new(false, null, reason, null, null, null, []);

        public static Decision Defer(string why) => new(true, why, null, null, null, null, []);
    }
}
