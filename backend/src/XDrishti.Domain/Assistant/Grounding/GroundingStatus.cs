namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>What the grounding check says about an answer (spec P9-03 AC 6). Stored as the lower-case code.</summary>
public enum GroundingStatus
{
    /// <summary>Figures were checked and none is unmatched.</summary>
    Grounded,

    /// <summary>At least one figure is not in the tool results of the turn, the question or the arguments (or figures with no tool call that are not in the question).</summary>
    Ungrounded,

    /// <summary>The answer holds no figure.</summary>
    NoNumbers,

    /// <summary>The turn was cancelled or failed; nothing was checked.</summary>
    NotChecked,
}
