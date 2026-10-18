using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public class TickSpecTests
{
    [Theory]
    [InlineData("1.0000", "0.01")]
    [InlineData("5.0000", "0.05")]
    [InlineData("10.0000", "0.10")]
    public void The_master_gives_paise_and_the_tick_is_in_rupees(string paise, string rupees)
    {
        var result = TickSpec.FromMasterPaise(decimal.Parse(paise, System.Globalization.CultureInfo.InvariantCulture));

        result.Value.Size.ShouldBe(decimal.Parse(rupees, System.Globalization.CultureInfo.InvariantCulture));
        result.Value.FromFallback.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_master_value_falls_back_to_five_paise_and_says_so()
    {
        var result = TickSpec.FromMasterPaise(null);

        result.Value.Size.ShouldBe(0.05m);
        result.Value.Size.ShouldBe(TickSpec.FallbackSize);
        result.Value.FromFallback.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void An_implausible_master_value_is_refused_not_replaced(int paise)
    {
        var result = TickSpec.FromMasterPaise(paise);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.tick_size_invalid");
    }

    [Fact]
    public void One_rupee_is_the_upper_bound()
    {
        TickSpec.FromMasterPaise(100).Value.Size.ShouldBe(1m);
        TickSpec.FromRupees(1m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void FromRupees_accepts_five_paise()
    {
        var result = TickSpec.FromRupees(0.05m);

        result.Value.Size.ShouldBe(0.05m);
        result.Value.FromFallback.ShouldBeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.05")]
    [InlineData("1.01")]
    public void FromRupees_refuses_out_of_range(string size)
    {
        var result = TickSpec.FromRupees(decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture));

        result.Error.Code.ShouldBe("simulation.tick_size_invalid");
    }

    [Fact]
    public void Only_the_factories_can_build_one()
    {
        typeof(TickSpec).GetConstructors().ShouldBeEmpty();
        typeof(TickSpec).GetProperties().ShouldAllBe(p => p.SetMethod == null);
    }
}
