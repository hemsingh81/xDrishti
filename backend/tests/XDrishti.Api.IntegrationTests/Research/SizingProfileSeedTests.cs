using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 10 of P3-03c: the sizing profiles of the simulator are code defaults (a run stores the numbers it used) that must equal the seeded rows of <c>accounts.risk_profiles</c>, so that a change of the seed or of the constants is
/// noticed. The owner's edits to the stored rows do not reach a run in P3; this test reads the seeded rows by their fixed ids and names.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SizingProfileSeedTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_standard_and_conservative_profiles_of_the_simulator_equal_the_seeded_rows()
    {
        var rows = await factory.WithDbAsync(db => db.RiskProfiles.AsNoTracking().Where(p => p.IsSeeded).Select(p => new { p.Name, p.RiskPerTradePct, p.MaxCapitalPerTradePct }).ToListAsync(Ct));

        var standard = rows.Single(r => r.Name == "Standard");
        var conservative = rows.Single(r => r.Name == "Conservative");
        (standard.RiskPerTradePct, standard.MaxCapitalPerTradePct).ShouldBe((SizingProfile.Standard.RiskPerTradePct, SizingProfile.Standard.MaxCapitalPerTradePct));
        (conservative.RiskPerTradePct, conservative.MaxCapitalPerTradePct).ShouldBe((SizingProfile.Conservative.RiskPerTradePct, SizingProfile.Conservative.MaxCapitalPerTradePct));
        SizingProfile.Standard.Name.ShouldBe(standard.Name.ToLowerInvariant());
        SizingProfile.Conservative.Name.ShouldBe(conservative.Name.ToLowerInvariant());
    }
}
