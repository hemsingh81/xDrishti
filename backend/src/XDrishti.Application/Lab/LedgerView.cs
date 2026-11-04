using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Lab;

/// <summary>One ledger row as the API shows it (<c>window</c> is the posted window).</summary>
public sealed record LedgerRowView(Guid RunId, string RunKey, string StrategyHash, string? ParentHash, int Version, string Universe, LedgerWindow Window, string Method, DateTimeOffset CreatedAt, string TrialKind = "run", int ExtraTrials = 0);

/// <summary>The window of a run.</summary>
public sealed record LedgerWindow(DateOnly From, DateOnly To);

/// <summary>The ledger of a family, the number of variants in it and the factor the gates are tightened by now (1 when the adjustment is off). <c>Trials</c> (CF-06) is the honest count of looks with its parts.</summary>
public sealed record LedgerView(string StrategyId, int VariantCount, double AdjustmentFactor, IReadOnlyList<LedgerRowView> Rows, TrialsView? Trials = null)
{
    internal static LedgerRowView Of(LedgerRow r) => new(r.RunId, r.RunKey, r.StrategyHash, r.ParentHash, r.Version, r.Universe, new LedgerWindow(r.From, r.To), r.Method, r.CreatedAt, r.TrialKind, r.ExtraTrials);
}
