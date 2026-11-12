namespace XDrishti.Domain.Replay;

/// <summary>Which rule picks the trades of a replay run (SR-02). The selection arm is the live system; the baseline arm takes every candidate the same session found, with no choosing.</summary>
public enum ReplayArm
{
    /// <summary>The live plan builder: filters, ranking, smart rules, cluster cap, portfolio fill and size scaling.</summary>
    Selection,

    /// <summary>The no-selection baseline (SR-02, D1): every candidate at the same risk, under the same capital, costs, scope, versions and cells.</summary>
    Baseline,
}
