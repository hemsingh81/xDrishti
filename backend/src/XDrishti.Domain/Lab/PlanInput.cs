namespace XDrishti.Domain.Lab;

/// <summary>One parameter of the registered sweep plan and the values it is tried at (spec CF-06, D2).</summary>
public sealed record PlanSweep(string Parameter, IReadOnlyList<double> Values);

/// <summary>
/// What a first full or robust run freezes (not the method: a robust run after a full run of the same plan is the same plan) (spec CF-06, D2): the strategy, the universe by label and by the hash of its members, the window, the cost sheet and its state, the registered sweep plan, the candidate exit flags, the slippage preset and the registration text.
/// </summary>
/// <param name="StrategyHash">The hash of the strategy text.</param>
/// <param name="UniverseLabel">The label of the universe (it says "members as of today", ISS-22).</param>
/// <param name="UniverseMembersHash">The hash of the sorted member ids: the label alone does not change when the members do.</param>
/// <param name="From">First day of the window.</param>
/// <param name="To">Last day of the window.</param>
/// <param name="CostSheet">The id, version or rates hash and the confirmation state of the cost sheet ("none" when gross).</param>
/// <param name="Sweeps">The sweep plan; the order of the parameters does not matter.</param>
/// <param name="ExitFlags">The candidate exit flags.</param>
/// <param name="SlippagePreset">The slippage preset (the modelled fills).</param>
/// <param name="RegistrationHash">The text hash of the registration, or "none".</param>
public sealed record PlanInput(
    string StrategyHash,
    string UniverseLabel,
    string UniverseMembersHash,
    DateOnly From,
    DateOnly To,
    string CostSheet,
    IReadOnlyList<PlanSweep> Sweeps,
    IReadOnlyList<string> ExitFlags,
    string SlippagePreset,
    string RegistrationHash);
