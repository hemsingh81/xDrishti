namespace XDrishti.Application.Portfolio.Attention;

/// <summary>
/// Keeps the remembered attention items true to the facts (ADR 0030): the only writer of <c>portfolio.attention_items</c>. The healer tick calls it every minute and the end of every holdings, trades and ledger run (and a renewed Dhan login) calls it too, so the notices
/// only ever read. Both methods are bookkeeping: they never fail their caller (a failure is logged and the next call tries again); only cancellation is passed on.
/// </summary>
public interface IAttentionReconciler
{
    /// <summary>Works out the items from the current facts, remembers them and resolves what no longer holds.</summary>
    ValueTask ReconcileAsync(CancellationToken cancellationToken);

    /// <summary>Deletes the resolved items older than the retention (<c>Portfolio:Heal:AttentionRetentionDays</c>). Unresolved items are never deleted.</summary>
    ValueTask ApplyRetentionAsync(CancellationToken cancellationToken);
}
