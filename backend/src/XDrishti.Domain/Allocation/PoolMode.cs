namespace XDrishti.Domain.Allocation;

/// <summary>Which accounts a split is for (spec P11-04, ADR 0148). Paper is the default and what P7 always used; Live is the pool of manual-live accounts, sized separately with the ramp scale already applied by the caller.</summary>
public enum PoolMode
{
    Paper,
    Live,
}
