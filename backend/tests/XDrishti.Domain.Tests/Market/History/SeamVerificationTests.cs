using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.History;

namespace XDrishti.Domain.Tests.Market.History;

/// <summary>CF-04d decisions 2 and 3 (acceptance criteria 3, 4, 5): the verification rule, its order of precedence and the basis hash. Hand instrument X: split 1:5 ex 2023-03-10, bonus 1:1 ex 2024-08-01, seam 2024-04-02 09:15 IST.</summary>
public sealed class SeamVerificationTests
{
    private static readonly DateTimeOffset Seam = new(2024, 4, 2, 9, 15, 0, TimeSpan.FromMinutes(330));

    private static readonly DateOnly Through = new(2026, 4, 30);

    private static AppliedAction Split(string exDate, int numerator, int denominator) =>
        new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Split, numerator, denominator, CorporateActionRatio.Factor(CorporateActionKind.Split, numerator, denominator));

    private static AppliedAction Bonus(string exDate) =>
        new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Bonus, 1, 1, CorporateActionRatio.Factor(CorporateActionKind.Bonus, 1, 1));

    private static readonly AppliedAction[] X = [Split("2023-03-10", 1, 5), Bonus("2024-08-01")];

    private static string Basis(DateOnly? through = null, params AppliedAction[] extra) => SeamBasis.Of(Seam, [.. X, .. extra], through ?? Through);

    private static SeamVerificationInput Verified() => new(true, ProbeStatus.Passed, Seam, Seam, Seam, false, Basis(), false, Basis(), Basis(), false);

    [Fact]
    public void An_instrument_that_meets_every_condition_is_verified()
    {
        SeamVerification.Decide(Verified()).ShouldBeNull();
    }

    [Theory]
    [InlineData("switch", SeamVerification.SwitchOff)]
    [InlineData("none", SeamVerification.NotProbed)]
    [InlineData("unresolved", SeamVerification.SeamUnresolved)]
    [InlineData("garbage", SeamVerification.NotProbed)]
    [InlineData("moved_earlier", SeamVerification.SeamMoved)]
    [InlineData("moved_later", SeamVerification.SeamMoved)]
    [InlineData("no_current", SeamVerification.SeamMoved)]
    [InlineData("high_water", SeamVerification.SeamMoved)]
    [InlineData("unfinished", SeamVerification.BackfillUnfinished)]
    [InlineData("unchecked", SeamVerification.ContinuityUnchecked)]
    [InlineData("jump", SeamVerification.SeamJump)]
    [InlineData("probe_basis_stale", SeamVerification.BasisChanged)]
    [InlineData("probe_basis_null", SeamVerification.BasisChanged)]
    [InlineData("continuity_basis_stale", SeamVerification.BasisChanged)]
    [InlineData("dhan", SeamVerification.DhanInCsvSpan)]
    public void Each_failing_condition_is_excluded_with_its_own_reason(string failure, string reason)
    {
        var stale = Basis(null, Split("2026-06-15", 1, 2));
        var input = failure switch
        {
            "switch" => Verified() with { SwitchOn = false },
            "none" => Verified() with { ProbeStatus = ProbeStatus.None },
            "unresolved" => Verified() with { ProbeStatus = ProbeStatus.Unresolved },
            "garbage" => Verified() with { ProbeStatus = "maybe" },
            "moved_earlier" => Verified() with { RecordedSeam = Seam.AddDays(-30), CurrentSeam = Seam.AddDays(-30) },
            "moved_later" => Verified() with { CurrentSeam = Seam.AddDays(5) },
            "no_current" => Verified() with { CurrentSeam = null },
            "high_water" => Verified() with { HighWater = Seam.AddDays(3) },
            "unfinished" => Verified() with { UnfinishedChunks = true },
            "unchecked" => Verified() with { ContinuityBasis = null },
            "jump" => Verified() with { SeamJump = true },
            "probe_basis_stale" => Verified() with { ProbeBasis = stale },
            "probe_basis_null" => Verified() with { ProbeBasis = null },
            "continuity_basis_stale" => Verified() with { ContinuityBasis = stale },
            _ => Verified() with { DhanInCsvSpan = true },
        };

        SeamVerification.Decide(input).ShouldBe(reason);
    }

    [Fact]
    public void A_seam_jump_flag_without_a_continuity_check_never_counts_as_a_pass()
    {
        // seam_jump = false and jump_pct = null mean the same whether or not the check ran: only the stored basis tells (fail closed)
        SeamVerification.Decide(Verified() with { ContinuityBasis = null, SeamJump = false }).ShouldBe(SeamVerification.ContinuityUnchecked);
    }

    [Fact]
    public void When_several_conditions_fail_the_first_in_the_listed_order_is_the_reason()
    {
        var all = Verified() with { SwitchOn = false, ProbeStatus = ProbeStatus.None, UnfinishedChunks = true, SeamJump = true, DhanInCsvSpan = true, ContinuityBasis = null };
        SeamVerification.Decide(all).ShouldBe(SeamVerification.SwitchOff);
        all = all with { SwitchOn = true };
        SeamVerification.Decide(all).ShouldBe(SeamVerification.NotProbed);
        all = all with { ProbeStatus = ProbeStatus.Passed };
        SeamVerification.Decide(all).ShouldBe(SeamVerification.BackfillUnfinished);
        all = all with { UnfinishedChunks = false };
        SeamVerification.Decide(all).ShouldBe(SeamVerification.ContinuityUnchecked);
        all = all with { ContinuityBasis = Basis() };
        SeamVerification.Decide(all).ShouldBe(SeamVerification.SeamJump);
        all = all with { SeamJump = false };
        SeamVerification.Decide(all).ShouldBe(SeamVerification.DhanInCsvSpan);
    }

    [Fact]
    public void A_split_added_after_the_probe_changes_the_basis_and_the_csv_end_moving_without_crossing_an_ex_date_does_not()
    {
        var added = Basis(null, Split("2026-06-15", 1, 2));

        added.ShouldNotBe(Basis());
        Basis(new DateOnly(2026, 5, 29), Split("2026-06-15", 1, 2)).ShouldBe(Basis(new DateOnly(2026, 4, 30), Split("2026-06-15", 1, 2)));
        Basis(new DateOnly(2026, 5, 29)).ShouldBe(Basis(new DateOnly(2026, 4, 30)));
        Basis(new DateOnly(2026, 6, 30), Split("2026-06-15", 1, 2)).ShouldNotBe(Basis(new DateOnly(2026, 5, 29), Split("2026-06-15", 1, 2)));
    }

    [Fact]
    public void The_basis_is_a_64_character_hash_that_follows_the_seam_the_actions_and_their_order_does_not_matter()
    {
        var basis = Basis();

        basis.Length.ShouldBe(64);
        SeamBasis.Of(Seam.AddMinutes(1), X, Through).ShouldNotBe(basis);
        SeamBasis.Of(Seam, X.Reverse(), Through).ShouldBe(basis);
        SeamBasis.Of(Seam, [X[0]], Through).ShouldNotBe(basis);
        SeamBasis.Of(Seam, X, null).ShouldNotBe(basis, "onCsv flips for the bonus");
    }
}
