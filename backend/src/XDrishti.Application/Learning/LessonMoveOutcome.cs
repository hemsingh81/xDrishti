namespace XDrishti.Application.Learning;

/// <summary>What a lesson move did.</summary>
/// <param name="Id">The lesson id.</param>
/// <param name="State">The state after the move.</param>
/// <param name="Text">A sentence for the caller.</param>
public sealed record LessonMoveOutcome(long Id, string State, string Text);
