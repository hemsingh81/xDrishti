using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>Basket 13 reads the dated flags (BK-05): a flag that is unknown (the lists' last good check is behind the date) is never a pass.</summary>
public sealed class Basket13FlagTests
{
    private static readonly DateOnly AsOf = DerivedFixtures.Day(60);

    private static DerivedResult Eval(DayFlag[]? flags) =>
        DerivedFixtures.Eval(13, [DerivedFixtures.Inst("AAA", DerivedFixtures.Flat(70, 100), flags: flags)], AsOf);

    private static bool Excluded(DerivedResult result) => result.Excluded.Any(e => e.Reason == ExclusionReason.NotInUniverseFilter);

    [Fact]
    public void A_known_clean_flag_passes_the_filter()
    {
        Excluded(Eval([new DayFlag(DerivedFixtures.Day(10), false, false, "EQ")])).ShouldBeFalse();
    }

    [Fact]
    public void An_unknown_flag_after_the_last_good_check_excludes_the_instrument()
    {
        Excluded(Eval([new DayFlag(DerivedFixtures.Day(10), false, false, "EQ"), new DayFlag(DerivedFixtures.Day(50), false, false, string.Empty, Unknown: true)])).ShouldBeTrue();
    }

    [Fact]
    public void No_flag_at_all_excludes_the_instrument()
    {
        Excluded(Eval(null)).ShouldBeTrue();
    }

    [Fact]
    public void A_surveillance_or_ban_flag_excludes_the_instrument()
    {
        Excluded(Eval([new DayFlag(DerivedFixtures.Day(10), true, false, "EQ")])).ShouldBeTrue();
        Excluded(Eval([new DayFlag(DerivedFixtures.Day(10), false, true, "EQ")])).ShouldBeTrue();
    }
}
