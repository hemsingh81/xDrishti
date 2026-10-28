using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading.Pairing;

/// <summary>Spec P11-03 criterion 8: the identity plan - missed - missed unknown - entry - exit - charges = actual, on the hand-worked block and as a property with a negative control.</summary>
public sealed class ExecutionGapBuilderTests
{
    private static GapTicket Trip(double plan, double entry, double exit, double? charges) =>
        new("ORB / 2026-11", GapKind.Trip, plan, entry, exit, charges, plan - entry - exit, charges is null ? null : plan - entry - exit - charges);

    [Fact]
    public void The_hand_worked_block_balances()
    {
        var s = ExecutionGapBuilder.Sum("ORB / 2026-11",
        [
            Trip(1.4, 0.0475, 0.2, 0.096),
            new("ORB / 2026-11", GapKind.Missed, 1.2, 0, 0, null, 0, null),
            new("ORB / 2026-11", GapKind.Missed, 0.8, 0, 0, null, 0, null),
        ]);

        s.N.ShouldBe(3);
        s.Plan.ShouldBe(3.4, 1e-9);
        s.Missed.ShouldBe(2.0, 1e-9);
        s.MissedUnknown.ShouldBe(0, 1e-9);
        s.Entry.ShouldBe(0.0475, 1e-9);
        s.Exit.ShouldBe(0.2, 1e-9);
        s.Charges.ShouldNotBeNull().ShouldBe(0.096, 1e-9);
        s.Result.ShouldBe(1.0565, 1e-9);
        s.Balanced.ShouldBeTrue();
        s.Basis.ShouldBe("net");
    }

    [Fact]
    public void A_missed_unknown_ticket_has_its_own_sum_and_the_same_result()
    {
        var s = ExecutionGapBuilder.Sum("g", [Trip(1.4, 0.0475, 0.2, 0.096), new("g", GapKind.Missed, 2.0, 0, 0, null, 0, null), new("g", GapKind.MissedUnknown, 0.5, 0, 0, null, 0, null)]);

        s.Plan.ShouldBe(3.9, 1e-9);
        s.MissedUnknown.ShouldBe(0.5, 1e-9);
        s.Result.ShouldBe(1.0565, 1e-9);
        s.Balanced.ShouldBeTrue();
    }

    [Fact]
    public void A_trip_without_recorded_charges_makes_the_whole_group_gross()
    {
        var s = ExecutionGapBuilder.Sum("g", [Trip(1.0, 0.1, 0.1, 0.05), Trip(1.0, 0.0, 0.2, null)]);

        s.Basis.ShouldBe("gross");
        s.Charges.ShouldBeNull();
        s.Result.ShouldBe(1.6, 1e-9); // gross actual of both, never a net/gross mix
        s.Balanced.ShouldBeTrue();
    }

    [Fact]
    public void The_identity_holds_over_random_tickets_and_a_doctored_exit_breaks_it_on_at_least_ten_seeds()
    {
        var broken = 0;
        for (var seed = 1; seed <= 20; seed++)
        {
            var rng = new Random(seed);
            var entry = 100m + (decimal)Math.Round(rng.NextDouble() * 50, 2);
            var exit = entry + (decimal)Math.Round((rng.NextDouble() * 10) - 3, 2);
            var vwapIn = entry + (decimal)Math.Round((rng.NextDouble() * 0.6) - 0.1, 2);
            var vwapOut = exit - (decimal)Math.Round((rng.NextDouble() * 0.6) - 0.1, 2);
            var w = ExecutionWaterfall.Build(Side.Buy, entry, exit, entry - 5m, vwapIn, vwapOut, 50, 12.5m, 0.01);
            var ticket = new GapTicket("g", GapKind.Trip, w.Rows[0].R!.Value, w.Rows[1].R!.Value, w.Rows[2].R!.Value, w.Rows[4].R, w.Rows[3].R!.Value, w.Rows[5].R);
            ExecutionGapBuilder.Sum("g", [ticket]).Balanced.ShouldBeTrue();

            // negative control: the stored actual is computed from a doctored exit price, so the identity against it fails
            var doctored = ExecutionWaterfall.Build(Side.Buy, entry, exit, entry - 5m, vwapIn, vwapOut + 1m, 50, 12.5m, 0.01);
            var wrong = new GapTicket("g", GapKind.Trip, ticket.PlanR, ticket.EntryR, ticket.ExitR, ticket.ChargesR, doctored.Rows[3].R!.Value, doctored.Rows[5].R);
            if (!ExecutionGapBuilder.Sum("g", [wrong]).Balanced)
            {
                broken++;
            }
        }

        broken.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Adding_a_ticket_to_another_group_never_changes_a_group()
    {
        var a = ExecutionGapBuilder.Sum("a", [Trip(1.0, 0.1, 0.1, 0.0)]);
        _ = ExecutionGapBuilder.Sum("b", [Trip(2.0, 0.3, 0.1, 0.0)]);
        ExecutionGapBuilder.Sum("a", [Trip(1.0, 0.1, 0.1, 0.0)]).ShouldBe(a);
    }
}
