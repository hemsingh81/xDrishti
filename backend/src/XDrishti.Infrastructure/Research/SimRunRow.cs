namespace XDrishti.Infrastructure.Research;

/// <summary>
/// A row of <c>research.sim_runs</c> (spec P3-10, D2): the persistence shape of a stored run's header. It exists for the model, the migration and the projections of the read path; the Domain's record is never mapped to it by EF, and no
/// EF type leaves Infrastructure. Written only by the adapter's SQL.
/// </summary>
internal sealed class SimRunRow
{
    public long Id { get; set; }

    public string RunKey { get; set; } = string.Empty;

    public string EngineVersion { get; set; } = string.Empty;

    public short RecordSchemaVersion { get; set; }

    public short TradeSchemaVersion { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public string DefinitionSha256 { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public string UniverseKind { get; set; } = string.Empty;

    public string UniverseLabel { get; set; } = string.Empty;

    public int Instruments { get; set; }

    public int InstrumentsRun { get; set; }

    public long TradesInWindow { get; set; }

    public long CandidatesInWindow { get; set; }

    public int TradeCount { get; set; }

    public int OpenAtEndCount { get; set; }

    public bool AcknowledgeCsvAdjusted { get; set; }

    public string Fills { get; set; } = string.Empty;

    public string? CostsSheetId { get; set; }

    public string? CostsRatesSha256 { get; set; }

    public string RecordJson { get; set; } = string.Empty;

    public string RecordSha256 { get; set; } = string.Empty;

    public string TradesSha256 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
