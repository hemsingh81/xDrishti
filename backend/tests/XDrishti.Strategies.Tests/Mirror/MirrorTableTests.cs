using System.Security.Cryptography;
using System.Text;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Mirror;

namespace XDrishti.Strategies.Tests.Mirror;

/// <summary>Spec P3-02e criterion 5 and D8: every catalog name, builtin, dotted spelling and constant has a row, and a silent edit of the table fails.</summary>
public sealed class MirrorTableTests
{
    // P15-05 added two rows (days_to_results, days_since_results: side-independent counts, mirrored to themselves) for names that did not exist before; no mirrored definition stored earlier can change, so the version was kept (a point for the owner in ADR 0185)
    private const string PinnedFingerprint = "63c33e9d166f168956bb39d62520244875b60886255b09167a72885364ca77a8";

    private static bool Classified(string name) => MirrorTable.IsRefused(name) || MirrorTable.TryGet(name, out _);

    [Fact]
    public void Every_catalog_name_has_a_row()
    {
        var missing = IndicatorCatalog.All.Select(i => i.Name).Where(n => !Classified(n)).ToList();

        missing.ShouldBeEmpty();
        IndicatorCatalog.All.Count.ShouldBe(58);
    }

    [Fact]
    public void Every_constant_and_dotted_spelling_has_a_row()
    {
        var constants = Vocabulary.ConstantNames.Where(n => !Classified(n)).ToList();
        var dotted = new List<string>();
        foreach (var scope in Vocabulary.ScopeNames)
        {
            Vocabulary.TryScope(scope, out var members);
            dotted.AddRange(members.Select(m => scope + "_" + m).Where(n => !Classified(n)));
        }

        constants.ShouldBeEmpty();
        dotted.ShouldBeEmpty();
    }

    [Fact]
    public void Every_builtin_is_rewritten_or_refused()
    {
        var unknown = Vocabulary.BuiltinNames.Where(n => n != "regime" && !MirrorTable.Builtins.Contains(n)).ToList();

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void A_mirrored_name_is_itself_in_the_table_and_mirrors_back()
    {
        foreach (var name in MirrorTable.Names.Where(n => !MirrorTable.IsRefused(n)))
        {
            MirrorTable.TryGet(name, out var row).ShouldBeTrue(name);
            MirrorTable.TryGet(row.Mirrored, out var back).ShouldBeTrue(row.Mirrored);
            back.Mirrored.ShouldBe(name);
            back.Kind.ShouldBe(row.Kind);
        }
    }

    [Fact]
    public void The_table_is_pinned_so_an_edit_must_bump_the_version()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(MirrorTable.Fingerprint())));

        hash.ShouldBe(PinnedFingerprint, "the mirror table changed: bump MirrorTable.Version and update this pin");
        MirrorTable.Version.ShouldBe("mirror-1");
    }
}
