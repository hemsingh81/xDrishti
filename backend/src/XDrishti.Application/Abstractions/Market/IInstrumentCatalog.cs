namespace XDrishti.Application.Abstractions.Market;

/// <summary>Writes the master list. All-or-nothing: a failure leaves the previous list untouched.</summary>
public interface IInstrumentCatalog
{
    /// <summary>
    /// Makes the list equal to <paramref name="candidates"/> in one transaction: new symbols are added, changed ones updated, retired ones that
    /// came back revived, and active ones that are no longer present retired (never deleted) — except instruments whose broker id is in
    /// <paramref name="protectedIds"/> (their row in the file could not be read, so absence proves nothing). When <paramref name="sectorsAvailable"/>
    /// is false the stored sectors are kept as they are.
    /// </summary>
    ValueTask<CatalogApplied> ApplyAsync(
        IReadOnlyList<InstrumentCandidate> candidates,
        IReadOnlyList<BrokerIdentity> protectedIds,
        bool sectorsAvailable,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>How many instruments are active (not retired).</summary>
    ValueTask<int> CountActiveAsync(CancellationToken cancellationToken);
}
