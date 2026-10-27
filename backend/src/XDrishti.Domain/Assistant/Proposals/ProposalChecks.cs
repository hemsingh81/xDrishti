namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>What the engine found out about a hypothesis proposal (spec P9-08 AC 4). Every field is read from stored data by the engine; none comes from the model.</summary>
public sealed record HypothesisProposalFacts(
    bool EvidenceResolved,
    string KnobKind,
    bool BaselineRunFound,
    IReadOnlyList<string> BaselineProblems,
    string BaselineValue,
    string CandidateValue,
    int MaxMovePct,
    int? CellN,
    int MinTrades,
    int OpenHypotheses,
    int MaxOpen,
    int PendingHypotheses,
    int MaxPendingHypotheses,
    bool DuplicateOpenOrPending,
    bool RepeatBlocked);

/// <summary>What the engine found out about a basket proposal (spec P9-08 AC 5).</summary>
public sealed record BasketProposalFacts(
    bool EvidenceResolved,
    bool BasketFound,
    bool BasketIsManual,
    bool BasketArchived,
    bool InstrumentKnown,
    bool InstrumentTracked,
    bool IsMember,
    string TradeEvidenceState,
    int? TradeN,
    int MinTrades,
    bool DuplicatePending,
    bool RepeatBlocked);

/// <summary>
/// The rule checks of a proposal (spec P9-08 AC 4, 5, 7): pure functions of the facts, one row per check in a fixed order, each with a stable id and a detail in words. A proposal is offered only when every row is ok; an input that is unavailable is not ok (it never counts as a pass).
/// </summary>
public static class ProposalChecks
{
    public const int MinHypothesisTrades = 30;
    public const int MinInstrumentTrades = 15;

    public static IReadOnlyList<ProposalCheck> Hypothesis(HypothesisProposalFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var bounds = f.BaselineRunFound && f.BaselineProblems.Count == 0 ? KnobBounds.Problems(f.BaselineValue, f.CandidateValue, f.MaxMovePct) : [];
        return
        [
            new(ProposalCodes.CheckEvidence, f.EvidenceResolved, f.EvidenceResolved ? "Every reference was read again from stored data." : "A reference does not resolve to stored data, or the cell and the baseline run are not cited."),
            new(ProposalCodes.CheckKnobKind, f.KnobKind == "Parameter", f.KnobKind == "Parameter" ? "The knob is a strategy parameter." : "Only a strategy parameter can be proposed here; exits and slippage are labels that cannot be checked."),
            new(ProposalCodes.CheckBaselineRun, f.BaselineRunFound && f.BaselineProblems.Count == 0, !f.BaselineRunFound ? "The baseline run is not stored." : f.BaselineProblems.Count == 0 ? "The baseline run is stored and carries the knob at the stated value." : string.Join("; ", f.BaselineProblems)),
            new(ProposalCodes.CheckKnobBounds, f.BaselineRunFound && f.BaselineProblems.Count == 0 && bounds.Count == 0, !f.BaselineRunFound || f.BaselineProblems.Count > 0 ? "Not checked: the baseline is not usable." : bounds.Count == 0 ? $"The candidate keeps the sign, differs, moves at most {f.MaxMovePct} percent and has at most {KnobBounds.MaxSignificantDigits} significant digits." : string.Join(", ", bounds)),
            new(ProposalCodes.CheckCellEvidence, f.CellN is { } n && n >= f.MinTrades, f.CellN is { } m ? (m >= f.MinTrades ? $"The cell has {m} trades in the stored snapshot (at least {f.MinTrades})." : $"The cell has {m} trades; at least {f.MinTrades} are needed.") : "The cell's statistics are unavailable."),
            new(ProposalCodes.CheckOpenLimit, f.OpenHypotheses < f.MaxOpen, f.OpenHypotheses < f.MaxOpen ? $"{f.OpenHypotheses} of {f.MaxOpen} hypotheses are open." : $"{f.OpenHypotheses} hypotheses are open; the limit is {f.MaxOpen}."),
            new(ProposalCodes.CheckPendingLimit, f.PendingHypotheses < f.MaxPendingHypotheses, f.PendingHypotheses < f.MaxPendingHypotheses ? $"{f.PendingHypotheses} hypothesis proposals are pending (limit {f.MaxPendingHypotheses})." : $"{f.PendingHypotheses} hypothesis proposals are pending; the limit is {f.MaxPendingHypotheses}."),
            new(ProposalCodes.CheckNoDuplicate, !f.DuplicateOpenOrPending, f.DuplicateOpenOrPending ? "Another pending proposal or open hypothesis has the same cell and knob." : "No other pending proposal or open hypothesis has this cell and knob."),
            new(ProposalCodes.CheckNotBlocked, !f.RepeatBlocked, f.RepeatBlocked ? $"The owner rejected this idea within {ProposalLifecycle.RepeatBlockDays} days." : "Not rejected by the owner recently."),
        ];
    }

    public static IReadOnlyList<ProposalCheck> Basket(string kind, BasketProposalFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var add = kind == ProposalKind.BasketAdd;
        var evidenceOk = f.TradeEvidenceState == "value" && f.TradeN is { } n && n >= f.MinTrades;
        return
        [
            new(ProposalCodes.CheckEvidence, f.EvidenceResolved, f.EvidenceResolved ? "Every reference was read again from stored data." : "A reference does not resolve to stored data."),
            new(ProposalCodes.CheckBasketFound, f.BasketFound && !f.BasketArchived, !f.BasketFound ? "The basket does not exist." : f.BasketArchived ? "The basket is archived." : "The basket exists and is not archived."),
            new(ProposalCodes.CheckBasketManual, f.BasketFound && f.BasketIsManual, f.BasketIsManual ? "The basket is one of the owner's manual baskets." : "A system (portfolio) basket is never changed from here."),
            new(ProposalCodes.CheckInstrumentTracked, f.InstrumentKnown && (!add || f.InstrumentTracked), !f.InstrumentKnown ? "The instrument is not in the master list." : add ? (f.InstrumentTracked ? "The instrument is already tracked (nothing will be tracked or backfilled)." : "The instrument is not tracked; this slice never tracks one.") : "The instrument is in the master list."),
            new(ProposalCodes.CheckMembership, add ? !f.IsMember : f.IsMember, add ? (f.IsMember ? "The instrument is a member already." : "The instrument is not a member yet.") : (f.IsMember ? "The instrument is a member." : "The instrument is not a member.")),
            new(ProposalCodes.CheckTradeEvidence, evidenceOk, f.TradeEvidenceState != "value" ? $"The instrument's trades are {f.TradeEvidenceState}." : f.TradeN is { } t ? (t >= f.MinTrades ? $"{t} trades of the instrument in stored runs (at least {f.MinTrades})." : $"{t} trades of the instrument; at least {f.MinTrades} are needed.") : "The instrument's trades are unavailable."),
            new(ProposalCodes.CheckNoDuplicate, !f.DuplicatePending, f.DuplicatePending ? "The same change is pending already." : "No identical change is pending."),
            new(ProposalCodes.CheckNotBlocked, !f.RepeatBlocked, f.RepeatBlocked ? $"The owner rejected this change within {ProposalLifecycle.RepeatBlockDays} days." : "Not rejected by the owner recently."),
        ];
    }

    /// <summary>Whether every check passed.</summary>
    public static bool AllOk(IReadOnlyList<ProposalCheck> checks) => checks.Count > 0 && checks.All(c => c.Ok);

    /// <summary>The refusal code of the first failed check, or null.</summary>
    public static string? FirstRefusal(IReadOnlyList<ProposalCheck> checks) => checks.FirstOrDefault(c => !c.Ok) is { } failed ? ProposalCodes.Refusal(failed.Code) : null;
}
