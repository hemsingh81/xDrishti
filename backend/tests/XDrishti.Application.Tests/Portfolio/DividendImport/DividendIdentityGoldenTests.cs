using XDrishti.Application.Portfolio.DividendImport;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>
/// Pins the identities and name keys of made-up rows to the exact values (ADR 0028). The identity of a stored dividend is a hash of the account and the row's content, the content holds the normalised name, so changing a rule of
/// <see cref="DividendNames.Key"/>, of the cleaning of names, or the content of <see cref="ParsedDividend.ContentKey"/> would give the rows already stored new identities and the same file would be imported a second time. If one of
/// these tests fails, do not just update the numbers: first write the migration that re-computes the stored identities (or accept the duplicates knowingly), and note it in ADR 0028.
/// </summary>
public sealed class DividendIdentityGoldenTests
{
    private static readonly Guid Account = new("11111111-2222-3333-4444-555555555555");

    public static TheoryData<string, string> Keys => new()
    {
        { "Power Finance Corporation Ltd.", "power finance" },
        { "Mahindra & Mahindra Limited", "mahindra and mahindra" },
        { "N.M.D.C. Limited", "nmdc" },
        { "Nestle's Foods India", "nestles foods" },
        { "Steel Authority of India", "steel authority of" },
        { "Kestrel Foods and Beverages Ltd", "kestrel foods and beverages" },
        { "India Ltd", "india ltd" },
        { "Dr. Lal-Path Labs", "dr lal path labs" },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public void Name_keys_are_pinned(string name, string key)
    {
        DividendNames.Key(name).ShouldBe(key);
    }

    private static ParsedDividend[] Rows() =>
    [
        Parsed("Orion Steel Limited", "2025-03-18", 500m, perShare: 5m, quantity: 100),
        Parsed("ORION STEEL LTD.", "2025-03-18", 500m, perShare: 5m, quantity: 100, line: 40),
        Parsed("Lumen Oils", "2025-03-18", 450m, perShare: 4.5m, quantity: 100),
        Parsed("Delta Power Corporation", "2024-09-23", 100m),
        Parsed("Mahindra & Mahindra Limited", "2024-06-13", 24.5m, perShare: 3.5m, quantity: 7),
        Parsed("Mahindra and Mahindra", "2024-06-13", 24.5m, perShare: 3.5m, quantity: 7),
        Parsed("N.M.D.C. Limited", "2023-12-01", 1134m, perShare: 5.67m, quantity: 200),
    ];

    [Fact]
    public void Identities_are_pinned()
    {
        var identities = DividendIdentity.Identify(Account, Rows()).Select(i => i.ExternalId).ToList();

        identities.ShouldBe(
        [
            "d:e76aac1cb4e055aed0f5bbc2d8690b3d",
            "d:e76aac1cb4e055aed0f5bbc2d8690b3d",
            "d:af38910e391a286fa54f100600ab64af",
            "d:b535e33745a2e217b71433c86484a7ab",
            "d:346b9c4069f31c35c8f2e7f4639529eb",
            "d:346b9c4069f31c35c8f2e7f4639529eb",
            "d:905717022189f7fc4fa68f2015d025fb",
        ]);
    }
}
