using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Shared small rules of the hypothesis handlers (P6-10).</summary>
internal static class HypothesisRows
{
    public const string OwnerActor = "cli:xd";

    public const int MaxText = 500;

    /// <summary>The single owner acts through the verb (<c>cli:xd</c>) or as <c>owner:...</c>; anything else is the system.</summary>
    public static bool IsOwner(string actor) => actor == OwnerActor || actor.StartsWith("owner:", StringComparison.Ordinal);

    public static bool ValidActor(string? actor) => !string.IsNullOrWhiteSpace(actor) && actor.Length <= 100 && !actor.Any(char.IsControl);

    public static string Clip(string text) => text.Length <= MaxText ? text : text[..MaxText];

    /// <summary>The row that follows <paramref name="latest"/>: the same facts, a new state, a reason and who.</summary>
    public static NewHypothesisRow Next(HypothesisRow latest, HypothesisState to, string reason, string actor, DateTimeOffset now, string? candidateRunKey = null, long? experimentId = null)
    {
        var r = latest.Row;
        return r with
        {
            HypothesisId = latest.HypothesisId,
            State = to.Code(),
            FromState = r.State,
            CandidateRunKey = candidateRunKey ?? r.CandidateRunKey,
            ExperimentId = experimentId ?? r.ExperimentId,
            Reason = Clip(reason),
            Actor = actor,
            CreatedAt = now,
        };
    }
}
