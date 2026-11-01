namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>One change of the owner's option-chain-recorder switch (<c>options.chain_recorder_switch</c>, insert-only).</summary>
public sealed record ChainSwitchRow(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The owner's stored start switch for the option-chain recorder (P14-02). No row means OFF.</summary>
public interface IOptionChainSwitchStore
{
    /// <summary>The newest row; null when nothing was ever stored (OFF).</summary>
    ValueTask<ChainSwitchRow?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<ChainSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken);

    /// <summary>Appends a row (the database stamps the time) and returns it.</summary>
    ValueTask<ChainSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}
