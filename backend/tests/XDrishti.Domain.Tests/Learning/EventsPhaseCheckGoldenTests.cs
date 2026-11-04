using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P14-17 criterion 5: the P15 phase check (<c>xd events phase-check</c>) is unchanged by the P14 check. Its text and JSON for fixed facts are pinned by hash; the family list it iterates is still the five of P15 and never names a P14 family.</summary>
public sealed class EventsPhaseCheckGoldenTests
{
    private static readonly IReadOnlyDictionary<string, string> Rule = new Dictionary<string, string> { ["rule_version"] = "evf-1", ["lag_minutes"] = "0" };

    private static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string> { ["catalog"] = "ev-1", ["lag_minutes"] = "0" };

    private static EventsPhaseReport Report()
    {
        var families = ExtensionFamilies.All.Select(f => new EventsFamilyFacts(
            f,
            true,
            [new EventsRecordFact("rec-" + f, f == ExtensionFamilies.Filter ? "F" : f == ExtensionFamilies.Pead ? "P" : "X", f == ExtensionFamilies.Pead ? "DROP" : "KEEP", false, 3, 500, [], f == ExtensionFamilies.Filter ? Rule : Catalog)],
            f == ExtensionFamilies.Filter ? Rule : Catalog)).ToList();
        return EventsPhaseCheck.Evaluate(
            new EventsPhaseFacts(families.Select(f => f.Family == ExtensionFamilies.Pead ? f with { Current = new Dictionary<string, string> { ["engine"] = "sim-9" } } : f).ToList(), new EventsFilterFact(true, false, 2, null, null), 24, [new EventsKindFact("results", 120, 8, 4)], 200, 150, 0.125, "rec-filter:results"),
            []);
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void The_p15_text_and_json_are_pinned()
    {
        var report = Report();

        (Hash(report.ToText()), Hash(report.ToJson())).ShouldBe(("80f9c6f425fed8fb8e43b9196a3f203f9fa7c55720fc32664ba9775d9dfcd98e", "23522ed2c9a7da800a177adec59038f309946995efecf6662f7c40805ff10bc9"));
    }

    [Fact]
    public void The_p15_family_list_is_the_five_families_and_names_no_p14_family()
    {
        ExtensionFamilies.All.ShouldBe(["filter:results", "ext:ev-1", "ext:fu-1", "ext:llm-1", "pead"]);
        ExtensionFamilies.All.ShouldNotContain(f => f.StartsWith("expr:", StringComparison.Ordinal) || f == "pairs:intraday-1" || f == "ext:iv-1" || f == "basis:carry-1");
        Report().Families.Select(f => f.Family).ShouldBe(ExtensionFamilies.All);
    }
}
