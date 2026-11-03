using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>What the model is shown about one announcement (P15-08): the subject (empty when only the labelled eval text exists), the exchange category and the text, all third-party data.</summary>
public sealed record AnnouncementFlagInput(string Subject, string Category, string Text);

/// <summary>
/// The outcome of one request (P15-08): a <see cref="State"/> from <see cref="ExtractionStates"/>, a closed <see cref="Reason"/> for any state but <c>ok</c>, the checked <see cref="Answer"/> (its order value is null when it was not grounded in the text, counted in
/// <see cref="Ungrounded"/>), the time and the tokens. Nothing of the raw answer is carried.
/// </summary>
public sealed record AnnouncementFlag(string State, string? Reason, AnnouncementAnswer? Answer, bool Ungrounded, int LatencyMs, int? PromptTokens, int? CompletionTokens);

/// <summary>
/// The one model call of the announcement flag (P15-08): one request, no tool, temperature 0, a fixed seed, the announcement fenced as data, the answer parsed against the closed schema. A server failure surfaces as <see cref="Abstractions.Ai.ModelUnavailableException"/>;
/// an answer that is not the schema is a flag with state <c>invalid_output</c>, never a guess.
/// </summary>
public interface IAnnouncementFlagger
{
    ValueTask<AnnouncementFlag> FlagAsync(AnnouncementFlagInput input, CancellationToken cancellationToken);
}
