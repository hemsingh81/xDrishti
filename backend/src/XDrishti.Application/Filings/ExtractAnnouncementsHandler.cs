using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>xd filings extract</c> (spec P15-08 criterion 5): classifies stored announcements one request at a time and stores one insert-only row each in <c>filings.extractions</c>, keyed by the model file hash, the prompt hash and the schema version, so a run is resumable and a second run with
/// nothing new does nothing. It refuses to start while the assistant's master switch is off (<c>assistant.disabled</c>, the existing code; it never sets it), while the model is not ready, while another turn holds the model or while the nightly run is going. It stops on a model failure after
/// recording that row as <c>model_unavailable</c>. The model's output is only ever the checked answer or a closed reason; the only thing written is the extraction row. Nothing here is read by a decision, a plan, a check or an order. The model is reached only through
/// <see cref="IAnnouncementFlagger"/>, which offers no tool.
/// </summary>
internal sealed class ExtractAnnouncementsHandler(
    IAssistantSettingsStore settingsStore,
    IEvalEnvironment environment,
    INightlyRunProbe nightly,
    IAnnouncementExtractionStore store,
    IAnnouncementCandidates candidates,
    IAnnouncementFlagger flagger,
    TurnOptions turn,
    TurnGate gate,
    TimeProvider time,
    ILogger<ExtractAnnouncementsHandler> logger) : ICommandHandler<ExtractAnnouncementsCommand, ExtractionSummary>
{
    private const int Batch = 25;

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public async ValueTask<Result<ExtractionSummary>> HandleAsync(ExtractAnnouncementsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.To < command.From || command.Limit is < 1)
        {
            return Refused(Error.Validation("filings.invalid_query", "--to must not be before --from and --limit must be at least 1."));
        }

        var settings = await settingsStore.CurrentAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Refused(Error.Conflict(ToolErrorCodes.Disabled, "The assistant's master switch is off; the owner switches it on in the assistant settings before announcements can be classified. This verb never sets it."));
        }

        var model = await environment.ModelAsync(cancellationToken);
        var modelHash = model.Sha256.ToLowerInvariant();
        if (!model.Known || modelHash.Length != 64 || !modelHash.All(char.IsAsciiHexDigitLower))
        {
            return Refused(Error.Conflict("assistant.model_unavailable", "model_unavailable: the model is not running or does not report its file hash. Nothing was asked of it."));
        }

        if (await nightly.IsRunningAsync(TimeSpan.FromMinutes(turn.NightlyGuardMinutes), cancellationToken))
        {
            return Refused(Error.Conflict("assistant.nightly_running", "The nightly run is in progress; classify announcements when it has finished."));
        }

        var prompt = PromptRegistry.AnnouncementFlag;
        var key = new ExtractionKey(modelHash, prompt.Name, prompt.Version, prompt.Sha256, AnnouncementSchema.Version);
        var from = new DateTimeOffset(command.From.ToDateTime(TimeOnly.MinValue), India);
        var toExclusive = new DateTimeOffset(command.To.AddDays(1).ToDateTime(TimeOnly.MinValue), India);
        ExtractionLog.Started(logger, command.From, command.To, command.Limit ?? 0);

        var clock = Stopwatch.StartNew();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var latencies = new List<int>();
        int attempted = 0, ungrounded = 0, noText = 0, skip = 0;
        string? stopped = null;
        while (stopped is null && (command.Limit is null || attempted < command.Limit))
        {
            if (attempted > 0 || skip > 0)
            {
                // The switch is the owner's and can go off during a long run: it is read again before every batch of 25.
                if (!(await settingsStore.CurrentAsync(cancellationToken)).Enabled)
                {
                    stopped = "the assistant's master switch went off (assistant.disabled)";
                    break;
                }
            }

            var take = command.Limit is { } limit ? Math.Min(Batch, limit - attempted) : Batch;
            var ids = await store.PendingAsync(from, toExclusive, key, skip, take, cancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            var bodies = await candidates.ReadBodiesAsync(ids, cancellationToken);
            foreach (var id in ids)
            {
                if (!bodies.TryGetValue(id, out var body))
                {
                    // The stored file is missing or changed: nothing is invented for it, and the next read starts after it.
                    skip++;
                    noText++;
                    continue;
                }

                using var slot = gate.TryEnter();
                if (slot is null)
                {
                    stopped = "another turn holds the model (assistant.busy)";
                    break;
                }

                var started = time.GetTimestamp();
                AnnouncementFlag flag;
                try
                {
                    flag = await flagger.FlagAsync(new AnnouncementFlagInput(body.Subject, body.Category, body.Text), cancellationToken);
                }
                catch (ModelUnavailableException)
                {
                    flag = new AnnouncementFlag(ExtractionStates.ModelUnavailable, ExtractionStates.ServerFailed, null, false, (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds), null, null);
                    stopped = "the model server failed (model_unavailable)";
                }

                attempted++;
                counts[flag.State] = counts.GetValueOrDefault(flag.State) + 1;
                ungrounded += flag.Ungrounded ? 1 : 0;
                if (flag.State != ExtractionStates.TooLong)
                {
                    latencies.Add(flag.LatencyMs);
                }

                await store.InsertAsync(new NewExtraction(id, key, flag.State, flag.Reason, flag.Answer, flag.Ungrounded, flag.LatencyMs, flag.PromptTokens, flag.CompletionTokens, time.GetUtcNow()), CancellationToken.None);
                ExtractionLog.Stored(logger, id, flag.State, flag.Reason, flag.Ungrounded, flag.LatencyMs);
                if (stopped is not null || (command.Limit is { } cap && attempted >= cap))
                {
                    break;
                }
            }
        }

        if (stopped is not null)
        {
            ExtractionLog.Stopped(logger, stopped.Contains("busy", StringComparison.Ordinal) ? "busy" : stopped.Contains("assistant.disabled", StringComparison.Ordinal) ? "assistant.disabled" : "model_unavailable", attempted);
        }

        var summary = new ExtractionSummary(
            attempted, counts.GetValueOrDefault(ExtractionStates.Ok), counts.GetValueOrDefault(ExtractionStates.InvalidOutput), counts.GetValueOrDefault(ExtractionStates.ModelUnavailable), counts.GetValueOrDefault(ExtractionStates.TooLong),
            ungrounded, noText, latencies.Count == 0 ? null : latencies.Average(), stopped, key.ModelSha256, key.PromptHash);
        ExtractionLog.Finished(logger, attempted, summary.Ok, summary.InvalidOutput, summary.ModelUnavailable, summary.TooLong, noText, clock.ElapsedMilliseconds);
        return Result.Success(summary);
    }

    private Result<ExtractionSummary> Refused(Error error)
    {
        ExtractionLog.Refused(logger, error.Code);
        return Result.Failure<ExtractionSummary>(error);
    }
}
