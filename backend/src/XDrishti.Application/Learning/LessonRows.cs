using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Shared small rules of the lesson handlers (P6-16).</summary>
internal static class LessonRows
{
    public const int MaxProblem = 500;

    public const int MaxSubject = 400;

    public const int MaxEvidenceKey = 200;

    /// <summary>The problems of a reason and an actor, empty when both are usable.</summary>
    public static List<FieldError> CheckReasonAndActor(string? reason, string? actor)
    {
        var problems = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > HypothesisRows.MaxText)
        {
            problems.Add(new FieldError("reason", "The reason is required, at most 500 characters."));
        }

        if (!HypothesisRows.ValidActor(actor))
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        return problems;
    }

    /// <summary>The row that follows <paramref name="latest"/>: the same facts, a new state, a reason and who, with the changes stated.</summary>
    public static NewLessonRow Next(LessonRow latest, LessonState to, string reason, string actor, DateTimeOffset now) =>
        latest.Row with { LessonId = latest.LessonId, State = to.Code(), FromState = latest.Row.State, Reason = HypothesisRows.Clip(reason), Actor = actor, CreatedAt = now };
}
