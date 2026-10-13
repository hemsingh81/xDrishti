using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The price alert (ADR 0022): what a level can be, when the condition holds (equality counts), and the guards of the factory.</summary>
public sealed class PriceAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0.0001", true)]
    [InlineData("1234.5678", true)]
    [InlineData("10000000", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("10000000.0001", false)]
    [InlineData("1.00001", false)]
    public void A_level_is_above_zero_at_most_ten_million_with_at_most_four_decimals(string level, bool valid) =>
        PriceAlert.IsValidLevel(decimal.Parse(level, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(valid);

    [Theory]
    [InlineData(AlertDirection.AtOrBelow, 99.99, true)]
    [InlineData(AlertDirection.AtOrBelow, 100, true)]
    [InlineData(AlertDirection.AtOrBelow, 100.01, false)]
    [InlineData(AlertDirection.AtOrAbove, 100.01, true)]
    [InlineData(AlertDirection.AtOrAbove, 100, true)]
    [InlineData(AlertDirection.AtOrAbove, 99.99, false)]
    public void The_condition_holds_at_the_level_and_beyond_it(AlertDirection direction, double price, bool holds) =>
        PriceAlert.Holds(direction, 100m, (decimal)price).ShouldBe(holds);

    [Fact]
    public void A_new_alert_is_active_with_the_actor_and_time_and_a_trimmed_note()
    {
        var alert = PriceAlert.Create(Guid.NewGuid(), AlertDirection.AtOrAbove, 1500m, "  take profit  ", " owner@example.com ", Now);

        (alert.Status, alert.Direction, alert.Level, alert.Note, alert.CreatedBy, alert.CreatedAt).ShouldBe((AlertStatus.Active, AlertDirection.AtOrAbove, 1500m, "take profit", "owner@example.com", Now));
        (alert.TriggeredAt, alert.TriggerPrice).ShouldBe((null, null));
        alert.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void A_blank_note_is_no_note_and_a_blank_actor_is_unknown()
    {
        var alert = PriceAlert.Create(Guid.NewGuid(), AlertDirection.AtOrBelow, 10m, "  ", "  ", Now);

        (alert.Note, alert.CreatedBy).ShouldBe((null, "unknown"));
    }

    [Fact]
    public void The_factory_guards_what_the_handler_should_have_checked()
    {
        Should.Throw<ArgumentException>(() => PriceAlert.Create(Guid.Empty, AlertDirection.AtOrBelow, 10m, null, "o", Now));
        Should.Throw<ArgumentOutOfRangeException>(() => PriceAlert.Create(Guid.NewGuid(), AlertDirection.AtOrBelow, 0m, null, "o", Now));
        Should.Throw<ArgumentException>(() => PriceAlert.Create(Guid.NewGuid(), AlertDirection.AtOrBelow, 10m, new string('x', 201), "o", Now));
    }

    [Theory]
    [InlineData(AlertDirection.AtOrBelow, "at_or_below")]
    [InlineData(AlertDirection.AtOrAbove, "at_or_above")]
    public void The_direction_has_a_stable_code_that_reads_back(AlertDirection direction, string code)
    {
        direction.ToCode().ShouldBe(code);
        AlertDirections.TryParse(code, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(direction);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AT_OR_BELOW")]
    [InlineData("below")]
    public void Any_other_text_is_not_a_direction(string? code) => AlertDirections.TryParse(code, out _).ShouldBeFalse();
}
