using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Tests.Shadow;

/// <summary>AM-01b (GAP-09): the evidence-set rule is fixed, canonical and fails closed.</summary>
public sealed class EvidenceSetRulesTests
{
    private static readonly string A = new('a', 64);

    private static readonly string B = new('b', 64);

    [Fact]
    public void The_default_is_the_distinct_versions_of_the_eligible_cells_in_ordinal_order()
    {
        var json = "{\"cells.eligible/X|1D|BUY|swing|FIXED_RR{rr=2}|*\":\"" + B + "\",\"cells.eligible/Y|1D|BUY|swing|FIXED_RR{rr=2}|*\":\"" + A + "\",\"cells.eligible/Z|1D|BUY|swing|FIXED_RR{rr=2}|*\":\"" + A + "\",\"cells.snapshot\":\"" + B + "\"}";

        EvidenceSetRules.Default(json).ShouldBe([A, B]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"cells.eligible/X\":\"unknown\"}")]
    public void The_default_is_null_when_nothing_can_be_named(string? json) => EvidenceSetRules.Default(json).ShouldBeNull();

    [Fact]
    public void Canonical_sorts_and_removes_duplicates() => EvidenceSetRules.Canonical([B, A, B]).ShouldBe([A, B]);

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    public void Canonical_refuses_a_malformed_version(string bad) => EvidenceSetRules.Canonical([A, bad]).ShouldBeNull();

    [Fact]
    public void Canonical_refuses_an_empty_set_and_an_oversized_one()
    {
        EvidenceSetRules.Canonical([]).ShouldBeNull();
        EvidenceSetRules.Canonical(Enumerable.Range(0, EvidenceSetRules.MaxVersions + 1).Select(i => i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture))).ShouldBeNull();
    }

    [Fact]
    public void The_set_hash_follows_the_versions_and_the_epoch_key_is_zero_without_an_epoch()
    {
        EvidenceSetRules.SetHash([A, B]).ShouldNotBe(EvidenceSetRules.SetHash([A]));
        EvidenceSetRules.SetHash([A, B]).ShouldBe(EvidenceSetRules.SetHash([A, B]));
        EvidenceSetRules.EpochKey(null).ShouldBe(0);
        EvidenceSetRules.EpochKey(4).ShouldBe(4);
    }
}
