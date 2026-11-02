namespace XDrishti.Domain.Filings;

/// <summary>One announcement of the sample: its id (<c>a001</c> to <c>a200</c> are the main rows, <c>a201</c> on the reserve in a fixed order), the half it was drawn in and its stratum counts. The inclusion weight is the stratum's eligible count over its sampled count.</summary>
public sealed record SampledAnnouncement(string Id, SampleCandidate Candidate, string Half, bool Reserve, int StratumEligible, int StratumSampled)
{
    public double Weight => Math.Round((double)StratumEligible / StratumSampled, 6);
}
