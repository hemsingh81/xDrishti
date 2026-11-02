namespace XDrishti.Domain.Filings;

/// <summary>What <see cref="AnnouncementSampler"/> drew: the rows in their fixed order (main first, then reserve), the strata and the eligible count of each half.</summary>
public sealed record AnnouncementSamplePlan(string Version, long Seed, IReadOnlyList<SampledAnnouncement> Rows, IReadOnlyList<SampleStratum> Strata, int EligibleMaterial, int EligibleOther);
