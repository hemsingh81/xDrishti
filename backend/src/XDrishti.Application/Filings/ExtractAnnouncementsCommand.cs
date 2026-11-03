using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary><c>xd filings extract --from --to [--limit N]</c> (P15-08): the owner's act. Classifies the stored announcements broadcast between the two India dates (inclusive) that have no extraction for the current model, prompt and schema; at most <paramref name="Limit"/> model calls when given.</summary>
public sealed record ExtractAnnouncementsCommand(DateOnly From, DateOnly To, int? Limit) : ICommand;

/// <summary>What a run did: model calls by state, the grounding count, announcements whose stored text could not be read (nothing stored for them), the mean latency of the calls that reached the model, and why the run stopped early (null when it ran to the end or the limit).</summary>
public sealed record ExtractionSummary(int Attempted, int Ok, int InvalidOutput, int ModelUnavailable, int TooLong, int Ungrounded, int NoText, double? MeanLatencyMs, string? StoppedBecause, string ModelSha256, string PromptHash)
{
    /// <summary>The lines the verb prints: counts and words only.</summary>
    public IReadOnlyList<string> Lines() =>
    [
        string.Create(CultureInfo.InvariantCulture, $"attempted {Attempted}: ok {Ok}, invalid_output {InvalidOutput}, model_unavailable {ModelUnavailable}, too_long {TooLong}; ungrounded order values {Ungrounded}; stored text unreadable {NoText}"),
        MeanLatencyMs is { } mean ? string.Create(CultureInfo.InvariantCulture, $"mean latency {mean:0} ms") : "mean latency n/a",
        $"model {Short(ModelSha256)}, prompt announcement-flag {Short(PromptHash)}",
        StoppedBecause is null ? "ran to the end of the range or the limit" : "stopped early: " + StoppedBecause,
    ];

    private static string Short(string hash) => hash.Length > 12 ? hash[..12] : hash;
}
