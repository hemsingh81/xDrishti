namespace XDrishti.Domain.Execution.Confirm;

/// <summary>Whether a ticket needs the password with the tap, and why (stored on the ticket at issue). The reasons are short codes.</summary>
public sealed record PasswordRule(bool Required, IReadOnlyList<string> Reasons)
{
    public const string NotionalOverStepUp = "notional_at_or_over_step_up";
    public const string SoftChecks = "soft_checks";
    public const string CapitalUnavailable = "capital_unavailable";
}
