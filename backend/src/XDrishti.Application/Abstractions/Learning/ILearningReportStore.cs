namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A learning report to store (spec P8-12, ADR 0110).</summary>
/// <param name="Kind">What kind of report: <c>counterfactual</c> (P8-12); later kinds need no migration.</param>
/// <param name="InputHash">64-hex SHA-256 of everything the result depends on; with the kind it is unique.</param>
/// <param name="Outcome">The conclusion word.</param>
/// <param name="AsOf">The last day of the data used.</param>
/// <param name="Basis"><c>gross</c>, <c>costed_unconfirmed</c> or <c>net_confirmed</c>.</param>
/// <param name="SourcesJson">The sources (runs) behind the report.</param>
/// <param name="OptionsJson">The knobs.</param>
/// <param name="ReportJson">The view DTO of the report (never the domain record).</param>
/// <param name="Text">The printed text, which contains the limits.</param>
/// <param name="BuiltBy">Who built it (<c>cli:xd</c>).</param>
/// <param name="CreatedAt">When.</param>
public sealed record NewLearningReport(string Kind, string InputHash, string Outcome, DateOnly AsOf, string Basis, string SourcesJson, string OptionsJson, string ReportJson, string Text, string BuiltBy, DateTimeOffset CreatedAt);

/// <summary>A stored learning report.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Report">What was stored.</param>
public sealed record StoredLearningReport(long Id, NewLearningReport Report);

/// <summary>
/// The generic learning reports (<c>research.learning_reports</c>, spec P8-12, ADR 0110), insert-only and kind-keyed. Heavy fits that must not run on a GET are computed by a verb and stored here; nothing here applies a change or places an order.
/// </summary>
public interface ILearningReportStore
{
    /// <summary>Stores the report unless its kind and input hash are stored; returns the stored one and whether this call wrote it.</summary>
    ValueTask<(StoredLearningReport Stored, bool Created)> InsertAsync(NewLearningReport report, CancellationToken cancellationToken);

    /// <summary>One report by id, or null.</summary>
    ValueTask<StoredLearningReport?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest report of a kind, or null.</summary>
    ValueTask<StoredLearningReport?> NewestAsync(string kind, CancellationToken cancellationToken);

    /// <summary>The newest reports of a kind first.</summary>
    ValueTask<IReadOnlyList<StoredLearningReport>> ListAsync(string kind, int limit, CancellationToken cancellationToken);
}
