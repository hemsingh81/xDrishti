using System.Text.Json;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// The owner's choice for one service (ADR 0018 §1): whether it is on, its schedule, how it is run (retries 0–5, timeout 1–240 minutes, catch up a missed run, tell the owner when it fails)
/// and its parameters. A definition carries the defaults; the stored row replaces them as a whole.
/// </summary>
public sealed record ServiceSettings(
    bool Enabled,
    Schedule Schedule,
    int Retries,
    int TimeoutMinutes,
    bool CatchUp,
    bool NotifyOnFailure,
    IReadOnlyDictionary<string, JsonElement> Parameters)
{
    public const int MinRetries = 0;
    public const int MaxRetries = 5;
    public const int MinTimeoutMinutes = 1;
    public const int MaxTimeoutMinutes = 240;

    /// <summary>Whether both hold the same choices (the parameters compared by their JSON).</summary>
    public bool Same(ServiceSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && Schedule == other.Schedule
        && Retries == other.Retries
        && TimeoutMinutes == other.TimeoutMinutes
        && CatchUp == other.CatchUp
        && NotifyOnFailure == other.NotifyOnFailure
        && Parameters.Count == other.Parameters.Count
        && Parameters.All(p => other.Parameters.TryGetValue(p.Key, out var v) && p.Value.GetRawText() == v.GetRawText());
}
