using XDrishti.Domain.Common;

namespace XDrishti.Domain.LiveTrading;

/// <summary>Problem codes of the ramp (spec P11-04). <c>live.invalid</c> is <see cref="ComplianceRecord.InvalidCode"/>.</summary>
public static class RampErrors
{
    public const string GatesNotMetCode = "live.gates_not_met";
    public const string StepNotAdjacentCode = "live.step_not_adjacent";
    public const string HoldActiveCode = "live.hold_active";
    public const string HoldConditionActiveCode = "live.hold_condition_active";
    public const string ExitNotConfirmedCode = "live.exit_not_confirmed";
    public const string ExitNotCheckedCode = "live.exit_not_checked";
    public const string NoHoldCode = "live.no_hold";
    public const string NoStepCode = "live.no_step";

    public static Error Invalid(string field, string message) =>
        Error.Validation(ComplianceRecord.InvalidCode, message) with { Fields = [new FieldError(field, message)] };
}
