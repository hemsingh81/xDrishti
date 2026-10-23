namespace XDrishti.Domain.Autonomy;

/// <summary>
/// One stored snapshot of the autonomy settings (spec P7-12, ADR 0091): the two switches, the threshold and the daily cap, with the id and hash of the row it was read from. The newest row is the current one; with no row the defaults
/// apply, every switch OFF (manual approval), and <see cref="Id"/> is null: a snapshot with no row can never be the basis of an auto approval. Setting ON means no manual approval is needed for what the rule allows; OFF is manual.
/// </summary>
/// <param name="Id">The row id, or null for the defaults (no row stored).</param>
/// <param name="Hash">SHA-256 (hex) of the canonical text of the values, stamped by the database; null for the defaults.</param>
/// <param name="AutoApproveTickets"><c>autonomy.auto_approve_tickets</c>: the rule may approve primary tickets before the review cutoff. Default false.</param>
/// <param name="PromoteFromReserve"><c>autonomy.promote_from_reserve</c>: the pre-open check may promote a qualifying reserve ticket (P7-11 uses it). Default false.</param>
/// <param name="MinPWin">The least lower-bound P(win) of the shrunk cell estimate for an auto approval (design default 0.58).</param>
/// <param name="MaxAutoPerDay">The most auto approvals per planned session (default 3).</param>
public sealed record AutonomySettings(long? Id, string? Hash, bool AutoApproveTickets, bool PromoteFromReserve, double MinPWin, int MaxAutoPerDay)
{
    /// <summary>The setting key of the first switch (doc 16 style).</summary>
    public const string AutoApproveKey = "autonomy.auto_approve_tickets";

    /// <summary>The setting key of the second switch.</summary>
    public const string PromoteFromReserveKey = "autonomy.promote_from_reserve";

    /// <summary>The design default threshold (doc 09 section 13).</summary>
    public const double DefaultMinPWin = 0.58;

    /// <summary>The default daily cap.</summary>
    public const int DefaultMaxAutoPerDay = 3;

    /// <summary>The least threshold the owner can set: below a coin flip the rule would approve losers.</summary>
    public const double LowestMinPWin = 0.50;

    /// <summary>The highest threshold.</summary>
    public const double HighestMinPWin = 0.99;

    /// <summary>The most auto approvals per day that can be set.</summary>
    public const int HighestMaxAutoPerDay = 20;

    /// <summary>The settings in force when nothing is stored: everything OFF.</summary>
    public static AutonomySettings Defaults { get; } = new(null, null, false, false, DefaultMinPWin, DefaultMaxAutoPerDay);

    /// <summary>Whether the values are inside the ranges the database also checks.</summary>
    public static bool ValuesAreValid(double minPWin, int maxAutoPerDay) =>
        double.IsFinite(minPWin) && minPWin is >= LowestMinPWin and <= HighestMinPWin && maxAutoPerDay is >= 1 and <= HighestMaxAutoPerDay;
}
