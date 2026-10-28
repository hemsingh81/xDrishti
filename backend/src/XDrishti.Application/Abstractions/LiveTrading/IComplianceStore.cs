using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>A stored compliance record: its row, the owner who wrote it and when.</summary>
public sealed record StoredCompliance(long Id, ComplianceRecord Record, string Actor, DateTimeOffset CreatedAt);

/// <summary>The compliance record of the live phase (<c>trading.compliance_records</c>, spec P11-05): insert-only, newest first. Reads only; P12-11 and the P11 phase check read the same store.</summary>
public interface IComplianceStore
{
    /// <summary>Appends the record (the database checks its rules again and stamps the time) and returns the stored row.</summary>
    ValueTask<StoredCompliance> AppendAsync(ComplianceRecord record, string actor, CancellationToken cancellationToken);

    /// <summary>The records, newest first; one kind or all.</summary>
    ValueTask<IReadOnlyList<StoredCompliance>> ListAsync(string? kind, int limit, CancellationToken cancellationToken);

    /// <summary>The newest record of each kind that has one.</summary>
    ValueTask<IReadOnlyList<StoredCompliance>> LatestPerKindAsync(CancellationToken cancellationToken);
}

/// <summary>The capability gate of the live writes (<c>Live:Writes:Enabled</c>, default OFF, spec P11-00 and P11-05). A deployment setting, changed with a restart; no endpoint changes it.</summary>
public interface ILiveWritesGate
{
    /// <summary>True when the live writes of the web are allowed now.</summary>
    bool Enabled { get; }
}
