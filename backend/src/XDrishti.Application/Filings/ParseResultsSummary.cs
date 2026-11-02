namespace XDrishti.Application.Filings;

/// <summary>What one parse step did: filings parsed (outcome <c>parsed</c>), rejected, not XBRL, with no facts, already parsed by another host, and failures (left for the next run).</summary>
public sealed record ParseResultsSummary(int Pending, int Parsed, int Rejected, int NotXbrl, int NoFacts, int Skipped, int Failures, string Version)
{
    public string Text => $"results parse ({Version}): pending {Pending}, parsed {Parsed}, rejected {Rejected}, not xbrl {NotXbrl}, no facts {NoFacts}, failures {Failures}";
}
