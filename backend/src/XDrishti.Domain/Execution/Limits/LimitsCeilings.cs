namespace XDrishti.Domain.Execution.Limits;

/// <summary>
/// The compiled ceilings of the per-account limits sheet (spec P12-09 Contract, D3): code constants, not settings, so the owner cannot exceed them. Money caps have no ceiling by design (they depend on capital); a raise of one waits for the
/// cooling-off instead. The database checks the same numbers (migration <c>AddAutoLiveLimits</c>).
/// </summary>
public static class LimitsCeilings
{
    public const int MaxOrdersPerDay = 20;

    public const int MaxOpenPositions = 10;

    public const int MaxAllowList = 50;

    public const int MaxReasonLength = 300;

    /// <summary>The only product of this phase: Dhan <c>INTRADAY</c> on NSE equity (P12-02 criterion 4). Futures, CNC, MTF, CO and BO cannot be expressed.</summary>
    public const string EquityIntraday = "EQ_MIS";

    public static readonly TimeOnly WindowEarliest = new(9, 16);

    public static readonly TimeOnly WindowLatest = new(15, 0);

    public static readonly IReadOnlyList<string> Products = [EquityIntraday];
}
