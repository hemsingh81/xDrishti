using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Checklist;

/// <summary>One check's answer for one ticket and one account, with the numbers.</summary>
/// <param name="Code">The stable code, see <see cref="ChecklistCodes"/>.</param>
/// <param name="Outcome">Pass, fail or unavailable. Unavailable is never a pass.</param>
/// <param name="Policy">What a failure means for approval.</param>
/// <param name="Value">The figure judged; null when there was none.</param>
/// <param name="Limit">The limit; null when there was none.</param>
/// <param name="Detail">One line in words.</param>
public sealed record ChecklistCheck(string Code, FilterOutcome Outcome, CheckPolicy Policy, double? Value, double? Limit, string Detail)
{
    /// <summary>Whether approval needs the owner's confirmation for this check: any unavailable, or a fail whose policy blocks.</summary>
    public bool NeedsConfirmation => Outcome == FilterOutcome.Unavailable || (Outcome == FilterOutcome.Fail && Policy == CheckPolicy.BlockWithConfirm);
}
