using System.Text;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Ai;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// The announcement flag (spec P15-08): ONE request over <see cref="IChatModel"/> (the P9-01 chat client behind the allow-listed local model server; no other client, no network of its own), with an EMPTY tool list, temperature 0 and seed 1337. The announcement is third-party data: it
/// is placed between fence lines that carry a random token, under a system prompt that declares it data. The answer is parsed against the closed schema (<see cref="AnnouncementAnswerParser"/>); anything else, a tool call included, is <c>invalid_output</c> with a closed reason and
/// nothing of it is kept. The order value is kept only if the text contains it (<see cref="GroundedAmount"/>). One attempt, no repair. An over-long input never reaches the model. A server failure surfaces as <see cref="ModelUnavailableException"/>. Nothing is logged or stored here.
/// </summary>
internal sealed class AnnouncementFlagger(IChatModel model, TimeProvider time, IFenceNonce nonce) : IAnnouncementFlagger
{
    /// <summary>The seed of every request.</summary>
    public const long Seed = 1337;

    /// <summary>The longest answer asked for, in tokens (the object is about 120).</summary>
    public const int MaxOutputTokens = 400;

    private const int MaxAnswerChars = 4000;

    /// <summary>The request for one announcement; public to the assembly so that a test can pin it: no tools, temperature 0, seed 1337.</summary>
    internal static ModelRequest BuildRequest(string userMessage) =>
        new([new ModelMessage(ModelRole.System, PromptRegistry.AnnouncementFlag.Text), new ModelMessage(ModelRole.User, userMessage)], [], MaxOutputTokens, 0f, Seed);

    public async ValueTask<AnnouncementFlag> FlagAsync(AnnouncementFlagInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Text.Length > AnnouncementSchema.MaxTextLength || input.Subject.Length > AnnouncementCsv.MaxSubject || input.Category.Length > AnnouncementCsv.MaxCategory)
        {
            return new AnnouncementFlag(ExtractionStates.TooLong, ExtractionStates.TextTooLong, null, false, 0, null, null);
        }

        var fence = AnnouncementFence.Wrap(input.Subject, input.Category, input.Text, nonce.Next);
        if (fence is null)
        {
            return Invalid(ExtractionStates.BadValue, 0, null, null);
        }

        var started = time.GetTimestamp();
        var answer = new StringBuilder();
        string? failure = null;
        int? promptTokens = null;
        int? completionTokens = null;
        await foreach (var part in model.StreamAsync(BuildRequest(fence.Value.Message), cancellationToken))
        {
            if (part is ModelToolCallPart)
            {
                // The model was offered no tool; a call is not an answer. Stop reading: nothing is run and nothing is kept.
                failure = ExtractionStates.ToolCall;
                break;
            }

            if (part is ModelTextPart text)
            {
                answer.Append(text.Text);
                if (answer.Length > MaxAnswerChars)
                {
                    failure = ExtractionStates.CutShort;
                    break;
                }
            }
            else if (part is ModelFinishPart finish)
            {
                promptTokens = finish.PromptTokens;
                completionTokens = finish.CompletionTokens;
                failure ??= finish.Reason switch { "length" => ExtractionStates.CutShort, "tool_calls" => ExtractionStates.ToolCall, _ => null };
            }
        }

        var ms = (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);
        if (failure is not null)
        {
            return Invalid(failure, ms, promptTokens, completionTokens);
        }

        var parsed = AnnouncementAnswerParser.Parse(answer.ToString());
        if (parsed.Answer is not { } checkedAnswer)
        {
            return Invalid(parsed.Reason ?? ExtractionStates.BadValue, ms, promptTokens, completionTokens);
        }

        var ungrounded = checkedAnswer.OrderValueInr is { } value && !GroundedAmount.IsGrounded(value, input.Subject + "\n" + input.Text);
        return new AnnouncementFlag(ExtractionStates.Ok, null, ungrounded ? checkedAnswer with { OrderValueInr = null } : checkedAnswer, ungrounded, ms, promptTokens, completionTokens);
    }

    private static AnnouncementFlag Invalid(string reason, int ms, int? promptTokens, int? completionTokens) =>
        new(ExtractionStates.InvalidOutput, reason, null, false, ms, promptTokens, completionTokens);
}
