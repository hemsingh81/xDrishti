namespace XDrishti.Application.Learning;

/// <summary>The smart-rule evidence of P8-13: counts read from stored plan rows, and for every rule an outcome that is a named non-value until its input exists.</summary>
/// <param name="State"><c>empty</c> when no plan is stored, else <c>observed</c>.</param>
/// <param name="PlansRead">Planned sessions counted.</param>
/// <param name="From">Earliest planned session; null without a plan.</param>
/// <param name="To">Latest planned session; null without a plan.</param>
/// <param name="Source">Where the counts come from.</param>
/// <param name="Rules">The five rules.</param>
/// <param name="FlaggedPlans">Plans flagged for review (a rule removed half or more of its candidates).</param>
/// <param name="Limits">What the counts do not say.</param>
public sealed record SmartRulesView(string State, int PlansRead, DateOnly? From, DateOnly? To, string Source, IReadOnlyList<SmartRuleView> Rules, IReadOnlyList<FlaggedPlanView> FlaggedPlans, IReadOnlyList<string> Limits);

/// <summary>One rule. A count that the plan rows cannot give is null with the reason in <paramref name="NotRecorded"/>, never zero.</summary>
/// <param name="Id">gate, throttle, meta, windows or correlation.</param>
/// <param name="Name">The rule's name.</param>
/// <param name="State"><c>empty</c> (no plan), <c>observed</c> (counts from plans) or <c>not_recorded</c> (the plan rows carry no code for this rule).</param>
/// <param name="RanOn">Plans on which the rule ran: always null, a plan row does not prove a run (ADR 0111).</param>
/// <param name="UnavailableOn">Plans on which the rule was unavailable; null when not recorded.</param>
/// <param name="PartialOn">Plans on which some correlation pairs were not checked; null for the other rules.</param>
/// <param name="Moved">Candidates moved to reserve; null when not recorded.</param>
/// <param name="Reasons">Reserve reasons with counts.</param>
/// <param name="NotRecorded">Why a count is null; null when every count is known.</param>
/// <param name="Outcome">The effect of the rule: never a number today.</param>
public sealed record SmartRuleView(string Id, string Name, string State, int? RanOn, int? UnavailableOn, int? PartialOn, int? Moved, IReadOnlyList<ReasonCountView> Reasons, string? NotRecorded, RuleOutcomeView Outcome);

/// <summary>A reserve reason and its count.</summary>
/// <param name="Code">The stable code.</param>
/// <param name="Count">Items.</param>
public sealed record ReasonCountView(string Code, int Count);

/// <summary>The outcome metric of a rule.</summary>
/// <param name="State"><c>not_evaluable</c> until the input exists.</param>
/// <param name="Value">Always null while not evaluable.</param>
/// <param name="Reason">Why it cannot be evaluated.</param>
/// <param name="Command">The verb or slice that would produce it.</param>
public sealed record RuleOutcomeView(string State, double? Value, string Reason, string Command);

/// <summary>A plan flagged for review.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Code">The flag code.</param>
public sealed record FlaggedPlanView(long PlanId, DateOnly ForDate, string Code);
