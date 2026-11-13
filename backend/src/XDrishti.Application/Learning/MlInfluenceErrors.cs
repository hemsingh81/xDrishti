using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the model influence use cases (CF-10 part 10c-2).</summary>
public static class MlInfluenceErrors
{
    /// <summary>The model's family took its one look at the hold-out already (spec CF-10, 409).</summary>
    public const string HoldOutReusedCode = "learning.holdout_reused";

    /// <summary>The model was trained without a hold-out, or no locked block is fixed, so there is no look to take.</summary>
    public const string HoldOutUnavailableCode = "learning.holdout_unavailable";

    /// <summary>The pass failed after the look was claimed, for a reason that goes away on its own (409).</summary>
    public const string HoldOutInterruptedCode = "learning.holdout_interrupted";

    /// <summary>The look ended without figures for a reason that does not go away (409): terminal, stored.</summary>
    public const string HoldOutFailedCode = "learning.holdout_failed";

    /// <summary>The look ended and stays without figures: the gate reads it as unavailable (never a pass) and the release may go on.</summary>
    public static Error HoldOutFailed(string code) => Error.Conflict(HoldOutFailedCode, $"The model's look at the hold-out block ended without figures ({code}). It is recorded as such: the model has no hold-out figures for this block and the block can be released.");

    /// <summary>The claimed look has no figures yet; asking again finishes it (it is the same look, not a second one).</summary>
    public static Error HoldOutInterrupted() => Error.Conflict(HoldOutInterruptedCode, "The pass over the hold-out block was interrupted (a database or network failure). The model's look is claimed and has no figures yet; asking again finishes that same look and is not a second one.");

    /// <summary>The look was taken but the block gave no candidate the model could score (422).</summary>
    public const string HoldOutNoRowsCode = "learning.holdout_no_rows";

    /// <summary>The look is burned (insert-only) and nothing could be scored.</summary>
    public static Error HoldOutNoRows() => Error.Validation(HoldOutNoRowsCode, "The look was taken, but the block gave no candidate the model could score (no trade, or no features or cell statistics before it). The look stays burned and counts for nothing.");

    /// <summary>The family of the model already took its one look at this block of the hold-out.</summary>
    public static Error HoldOutReused(string family) =>
        Error.Conflict(HoldOutReusedCode, $"The model family `{family}` has already looked at this block of the hold-out; a second look is a new trial and cannot make the model pass. It needs a fresh hold-out (a block the owner advances) or stays advisory.");

    /// <summary>The look cannot be taken as asked.</summary>
    public static Error InvalidLook(string message) => Error.Validation(LearningErrors.InvalidCode, message);

    /// <summary>There is no hold-out to look at.</summary>
    public static Error HoldOutUnavailable(string reason) => Error.Conflict(HoldOutUnavailableCode, reason);
}
