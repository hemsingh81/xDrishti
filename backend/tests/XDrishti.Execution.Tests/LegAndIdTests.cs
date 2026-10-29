using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

public sealed class LegAndIdTests
{
    [Fact]
    public void A_late_lower_status_is_ignored_and_counted_and_a_final_status_stays()
    {
        var leg = new Leg(LegKind.Entry, 10);

        leg.Apply(LegStatus.PartTraded, 4).ShouldBeTrue();
        leg.Apply(LegStatus.Pending, 4).ShouldBeFalse();
        leg.Apply(LegStatus.Traded, 10).ShouldBeTrue();
        leg.Apply(LegStatus.Cancelled, 10).ShouldBeTrue();
        leg.Apply(LegStatus.Traded, 10).ShouldBeFalse();

        leg.Status.ShouldBe(LegStatus.Cancelled);
        leg.StaleUpdates.ShouldBe(2);
    }

    [Fact]
    public void Property_5000_seeds_a_leg_never_moves_back_and_the_filled_quantity_only_grows_within_the_quantity()
    {
        var statuses = Enum.GetValues<LegStatus>();
        for (var seed = 0; seed < 5000; seed++)
        {
            var rnd = new Random(seed);
            var leg = new Leg(LegKind.Stop, 10);
            for (var n = rnd.Next(1, 30); n > 0; n--)
            {
                var (status, filled, stale) = (leg.Status, leg.FilledQty, leg.StaleUpdates);
                var wasFinal = Leg.IsFinal(status);
                var applied = leg.Apply(statuses[rnd.Next(statuses.Length)], rnd.Next(-2, 15));

                leg.FilledQty.ShouldBeGreaterThanOrEqualTo(filled);
                leg.FilledQty.ShouldBeLessThanOrEqualTo(10);
                if (applied)
                {
                    wasFinal.ShouldBeFalse();
                    Leg.Rank(leg.Status).ShouldBeGreaterThanOrEqualTo(Leg.Rank(status));
                }
                else
                {
                    leg.Status.ShouldBe(status);
                    leg.StaleUpdates.ShouldBe(stale + 1);
                }
            }
        }
    }

    [Fact]
    public void The_hand_case_of_the_client_order_id()
    {
        var id = ClientOrderId.Create(new DateOnly(2026, 10, 24), 361, LegKind.Entry, 1).Value;

        id.ToString().ShouldBe("XD2610240000A1E1");
    }

    [Fact]
    public void Property_the_id_is_16_characters_deterministic_and_parses_back()
    {
        var rnd = new Random(7);
        var kinds = Enum.GetValues<LegKind>();
        for (var n = 0; n < 5000; n++)
        {
            var kind = kinds[rnd.Next(kinds.Length)];
            var date = new DateOnly(2026, 1, 1).AddDays(rnd.Next(0, 1000));
            var intent = rnd.NextInt64(0, ClientOrderId.MaxIntentId + 1);
            var attempt = kind == LegKind.Entry ? 1 : rnd.Next(1, 10);
            var id = ClientOrderId.Create(date, intent, kind, attempt).Value;
            var text = id.ToString();

            text.Length.ShouldBe(16);
            text.ShouldAllBe(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c));
            ClientOrderId.TryParse(text, out var back).ShouldBeTrue();
            back.ShouldBe(id);
            ClientOrderId.Create(date, intent, kind, attempt).Value.ToString().ShouldBe(text);
        }
    }

    [Theory]
    [InlineData(-1, LegKind.Stop, 1)]
    [InlineData(2_176_782_336, LegKind.Stop, 1)]
    [InlineData(1, LegKind.Stop, 0)]
    [InlineData(1, LegKind.Stop, 10)]
    [InlineData(1, LegKind.Entry, 2)]
    public void Out_of_range_ids_are_refused(long intent, LegKind kind, int attempt)
    {
        ClientOrderId.Create(new DateOnly(2026, 10, 24), intent, kind, attempt).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("XD2610240000A1E")]
    [InlineData("XX2610240000A1E1")]
    [InlineData("XD2610240000a1E1")]
    [InlineData("XD2610240000A1Z1")]
    [InlineData("XD2610240000A1E0")]
    public void Bad_text_does_not_parse(string text)
    {
        ClientOrderId.TryParse(text, out _).ShouldBeFalse();
    }
}
