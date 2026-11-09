namespace XDrishti.Application.Abstractions.Confidence;

/// <summary>A kill proposal to store (spec CF-12 D2, part 12b, ADR 0219).</summary>
/// <param name="Strategy">The strategy.</param>
/// <param name="EpochId">The paper epoch of the series, or null.</param>
/// <param name="Rule"><c>cost_stress</c>, <c>drawdown</c>, <c>decay_twice</c> or <c>failure_boundary</c>.</param>
/// <param name="ProposalOnly">True for the CF-09 failure boundary: the automatic setting never executes it.</param>
/// <param name="Value">The figure the rule judged.</param>
/// <param name="Limit">What it was judged against.</param>
/// <param name="Trades">Trades the figure is based on.</param>
/// <param name="Text">The words, with the numbers.</param>
/// <param name="Cells">The cells (Paper or Active) the proposal would pause.</param>
/// <param name="SettingsHash">The SHA-256 of the decay and kill settings that produced it.</param>
/// <param name="SeriesTrades">Trades in the edge series when it fired (the base of the "20 new trades" wait).</param>
public sealed record NewKillProposal(string Strategy, long? EpochId, string Rule, bool ProposalOnly, double? Value, double? Limit, int Trades, string Text, IReadOnlyList<string> Cells, string SettingsHash, int SeriesTrades);

/// <summary>A stored kill proposal.</summary>
/// <param name="Id">The row.</param>
/// <param name="Proposal">What was proposed.</param>
/// <param name="ProposedOn">The India date it was proposed (one proposal per strategy, rule and day).</param>
/// <param name="ProposedAt">When (the database clock).</param>
/// <param name="State"><c>proposed</c>, <c>applied</c> or <c>dismissed</c>.</param>
/// <param name="Actor"><c>owner:...</c> or <c>auto</c> once decided, else null.</param>
/// <param name="DecidedAt">When it was decided, or null.</param>
/// <param name="AutoSettingId">For an automatic decision, the row of <c>confidence.kill_settings</c> in force (the setting snapshot), else null.</param>
/// <param name="DismissReason">The owner's reason when the state is <c>dismissed</c>, else null.</param>
public sealed record KillProposalRow(long Id, NewKillProposal Proposal, DateOnly ProposedOn, DateTimeOffset ProposedAt, string State, string? Actor, DateTimeOffset? DecidedAt, long? AutoSettingId, string? DismissReason = null);

/// <summary>Reads kill proposals. Read only.</summary>
public interface IKillProposalReader
{
    /// <summary>One proposal, or null.</summary>
    ValueTask<KillProposalRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest proposals first, optionally of one state and one strategy.</summary>
    ValueTask<IReadOnlyList<KillProposalRow>> ListAsync(string? state, string? strategy, int limit, CancellationToken cancellationToken);

    /// <summary>The newest proposal of a strategy and rule in a paper epoch, in any state, or null (when the rule last fired).</summary>
    ValueTask<KillProposalRow?> LatestAsync(string strategy, string rule, long? epochId, CancellationToken cancellationToken);
}

/// <summary>Writes kill proposals: insert, and one compare-and-set of the state. Nothing else is ever changed.</summary>
public interface IKillProposalStore
{
    /// <summary>Stores a proposal; null when the strategy already has one for this rule on this day (idempotent).</summary>
    ValueTask<long?> InsertAsync(NewKillProposal proposal, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Moves a <c>proposed</c> proposal to <paramref name="state"/> (<c>applied</c> or <c>dismissed</c>); <see cref="KillDecideResult.NotProposed"/> when it was not <c>proposed</c> any more, <see cref="KillDecideResult.Refused"/> when the record refuses the decision.</summary>
    ValueTask<KillDecideResult> DecideAsync(long id, string state, string actor, long? autoSettingId, CancellationToken cancellationToken);

    /// <summary>Moves a <c>proposed</c> proposal to <c>dismissed</c> with the owner and the reason, in one conditional update (CF-12 leftover, ADR 0234); <see cref="KillDecideResult.NotProposed"/> when it was not <c>proposed</c> any more. It changes no lifecycle state.</summary>
    ValueTask<KillDecideResult> DismissAsync(long id, string actor, string reason, CancellationToken cancellationToken);
}

/// <summary>What the record said to a decision.</summary>
public enum KillDecideResult
{
    /// <summary>The proposal was <c>proposed</c> and is decided now.</summary>
    Decided,

    /// <summary>The proposal was decided already (by another caller), or does not exist.</summary>
    NotProposed,

    /// <summary>The record refuses the decision: an automatic decision while the newest switch row is not ON or is not the one recorded, an automatic decision on a proposal-only rule, or a decision whose actor and setting do not agree. Nothing was changed.</summary>
    Refused,
}

/// <summary>The setting <c>confidence.kill.auto</c> as stored: the newest row of <c>confidence.kill_settings</c>, OFF when there is none.</summary>
/// <param name="Id">The row, or null when none exists.</param>
/// <param name="Auto">Whether an automatic demotion is allowed.</param>
public sealed record KillSwitch(long? Id, bool Auto);

/// <summary>Reads the owner's switch <c>confidence.kill.auto</c>.</summary>
public interface IKillSettings
{
    /// <summary>The switch now; OFF with no id when no row is stored.</summary>
    ValueTask<KillSwitch> CurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Appends a change of the owner's switch (insert only). Only the owner's handler calls it.</summary>
public interface IKillSwitchStore
{
    /// <summary>Appends a row (the database stamps its id in commit order and its time) and returns the id.</summary>
    ValueTask<long> AppendAsync(bool auto, string actor, string reason, CancellationToken cancellationToken);
}

/// <summary>One daily evaluation of a strategy to store (insert only).</summary>
/// <param name="Strategy">The strategy.</param>
/// <param name="EpochId">The paper epoch, or null.</param>
/// <param name="SettingsHash">The SHA-256 of the settings.</param>
/// <param name="EdgeState">The state of the edge series.</param>
/// <param name="Trades">Trades in the series.</param>
/// <param name="Verdicts">The four kill verdicts as JSON.</param>
public sealed record NewDecayCheck(string Strategy, long? EpochId, string SettingsHash, string EdgeState, int Trades, string Verdicts);

/// <summary>The daily evaluations of the decay monitor.</summary>
public interface IDecayCheckStore
{
    /// <summary>Whether the strategy was already evaluated on this India day.</summary>
    ValueTask<bool> ExistsAsync(string strategy, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Stores the evaluation; false when one exists for the day.</summary>
    ValueTask<bool> InsertAsync(NewDecayCheck check, DateOnly day, CancellationToken cancellationToken);
}
