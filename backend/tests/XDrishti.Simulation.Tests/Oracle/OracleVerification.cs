namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The version binding of the engine label (spec P3-08, Annex C, D13): the engine version for which the oracle comparison of this suite was shown to agree. <c>RunLabels.EngineVerified</c> (Domain) is true only through this constant:
/// <c>OracleVerifiedForTests</c> fails when the flag is true while the engine is at another version, so the next engine version cannot ship the label until someone has rerun the comparison and edited this constant on purpose. It is a
/// compile-time constant committed beside the suite and not a file the suite writes (a failing run would leave a stale file behind, and a Domain test would have to read a path in another project).
/// </summary>
internal static class OracleVerification
{
    /// <summary>The engine version the oracle comparison was shown for. Edit it only after the comparison (walker, gate and costing) has been rerun against the new version and agrees.</summary>
    internal const string OracleVerifiedFor = "sim-5";

    /// <summary>The rule: the label may be true only for the engine version the comparison was shown for; a false label binds nothing.</summary>
    internal static bool LabelIsBound(bool engineVerified, string verifiedFor, string engineVersion) => !engineVerified || string.Equals(verifiedFor, engineVersion, StringComparison.Ordinal);
}
