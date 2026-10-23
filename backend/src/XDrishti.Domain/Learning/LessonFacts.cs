namespace XDrishti.Domain.Learning;

/// <summary>What the caller states about a lesson move (spec P6-16).</summary>
/// <param name="ByOwner">The actor is the owner (not the system).</param>
/// <param name="HasFix">A fix is linked (for closing).</param>
/// <param name="FixJudged">The linked fix has an experiment whose numbers can be read (for closing).</param>
public sealed record LessonFacts(bool ByOwner, bool HasFix = false, bool FixJudged = false);
