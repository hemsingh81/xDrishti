namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>Whether a rule ran.</summary>
public enum RuleStatus
{
    /// <summary>The rule ran on real inputs.</summary>
    Applied,

    /// <summary>An input is missing or not known before the decision date: the rule did not run and the plan says so. Never a pass.</summary>
    Unavailable,

    /// <summary>Switched off by the owner.</summary>
    Off,
}
