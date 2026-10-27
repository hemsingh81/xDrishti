namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>
/// The codes of the proposal checks and refusals (spec P9-08 AC 3 to 8). They are stored with a proposal, shown on its card and logged, so they never change. A check row has a positive id (<c>baseline_run</c>); when it fails, <see cref="Refusal"/> gives the refusal code of the spec.
/// </summary>
public static class ProposalCodes
{
    public const string AnswerShape = "assistant.answer_shape";
    public const string EvidenceUnresolved = "evidence_unresolved";
    public const string EvidenceInsufficient = "evidence_insufficient";
    public const string EvidenceUnavailable = "evidence_unavailable";
    public const string KnobKindNotSupported = "knob_kind_not_supported";
    public const string ReasonClaimWord = "reason_claim_word";
    public const string ReasonNumberNotInEvidence = "reason_number_not_in_evidence";
    public const string ReasonInvalid = "reason_invalid";
    public const string ReasonInstructionLike = "reason_instruction_like";
    public const string RepeatBlocked = "repeat_blocked";
    public const string DuplicatePending = "duplicate_pending";
    public const string PendingLimit = "pending_limit";
    public const string BasketSystem = "basket_system";
    public const string KindUnknown = "kind_unknown";
    public const string PayloadInvalid = "payload_invalid";

    // The ids of the check rows.
    public const string CheckEvidence = "evidence_resolved";
    public const string CheckKnobKind = "knob_kind";
    public const string CheckBaselineRun = "baseline_run";
    public const string CheckBaselineValue = "baseline_value";
    public const string CheckKnobBounds = "knob_bounds";
    public const string CheckCellEvidence = "cell_evidence";
    public const string CheckOpenLimit = "open_limit";
    public const string CheckPendingLimit = "pending_limit";
    public const string CheckNoDuplicate = "no_duplicate";
    public const string CheckNotBlocked = "not_repeat_blocked";
    public const string CheckBasketFound = "basket_found";
    public const string CheckBasketManual = "basket_manual";
    public const string CheckInstrumentTracked = "instrument_tracked";
    public const string CheckMembership = "membership";
    public const string CheckTradeEvidence = "trade_evidence";

    /// <summary>The refusal code (spec P9-08) of a failed check row.</summary>
    public static string Refusal(string checkId) => checkId switch
    {
        CheckEvidence => EvidenceUnresolved,
        CheckKnobKind => KnobKindNotSupported,
        CheckCellEvidence or CheckTradeEvidence => EvidenceInsufficient,
        CheckBasketManual => BasketSystem,
        CheckNoDuplicate => DuplicatePending,
        CheckNotBlocked => RepeatBlocked,
        CheckPendingLimit or CheckOpenLimit => PendingLimit,
        _ => checkId,
    };
}
