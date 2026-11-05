using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the model influence use cases (CF-10 part 10c-2).</summary>
public static class MlInfluenceErrors
{
    /// <summary>The model's family took its one look at the hold-out already (spec CF-10, 409).</summary>
    public const string HoldOutReusedCode = "learning.holdout_reused";

    /// <summary>The model was trained without a hold-out, or no locked block is fixed, so there is no look to take.</summary>
    public const string HoldOutUnavailableCode = "learning.holdout_unavailable";

    /// <summary>The family of the model already took its one look at this block of the hold-out.</summary>
    public static Error HoldOutReused(string family) =>
        Error.Conflict(HoldOutReusedCode, $"The model family `{family}` has already looked at this block of the hold-out; a second look is a new trial and cannot make the model pass. It needs a fresh hold-out (a block the owner advances) or stays advisory.");

    /// <summary>The look cannot be taken as asked.</summary>
    public static Error InvalidLook(string message) => Error.Validation(LearningErrors.InvalidCode, message);

    /// <summary>There is no hold-out to look at.</summary>
    public static Error HoldOutUnavailable(string reason) => Error.Conflict(HoldOutUnavailableCode, reason);
}
