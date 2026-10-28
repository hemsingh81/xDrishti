using XDrishti.Domain.Common;

namespace XDrishti.Domain.LiveTrading;

/// <summary>Problem codes of the ramp (spec P11-04). <c>live.invalid</c> is <see cref="ComplianceRecord.InvalidCode"/>.</summary>
public static class RampErrors
{
    public const string GatesNotMetCode = "live.gates_not_met";
    public const string StepNotAdjacentCode = "live.step_not_adjacent";

    public static Error Invalid(string field, string message) =>
        Error.Validation(ComplianceRecord.InvalidCode, message) with { Fields = [new FieldError(field, message)] };
}
