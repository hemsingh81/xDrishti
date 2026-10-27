namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One idea of the drafting set (C). <paramref name="Kind"/> is <c>draft</c>, <c>decline</c> or <c>decline_or_partial</c> (a decline, or a valid draft that lists the unsupported part in <c>not_expressed</c>).</summary>
public sealed record IdeaCase(string Id, string Idea, string Kind, IReadOnlyList<IdeaAssertion> Assertions)
{
    public const string Draft = "draft";
    public const string Decline = "decline";
    public const string DeclineOrPartial = "decline_or_partial";

    public bool IsDeclineKind => Kind != Draft;
}
