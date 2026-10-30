using System.Globalization;
using XDrishti.Domain.Execution.Confirm;

namespace XDrishti.Domain.Tests.Execution;

/// <summary>
/// Spec P12-08 criterion 2: the ticket text is a pure function of the stored parameters. The hand example reads exactly as the spec writes it; over 2,000 generated tickets the same parameters give the same text and hash, every changed
/// field changes the hash, and every parameter appears in the text.
/// </summary>
public sealed class ConfirmTextTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static ConfirmTicketParams Example(bool canary = false) => new(
        "BUY", 120, "TATAPOWER", 412.35m, 405.10m, 425.80m, "Dhan", "Main", new DateTimeOffset(2026, 10, 23, 19, 41, 0, Ist), new DateTimeOffset(2026, 10, 27, 9, 47, 30, Ist), canary);

    [Fact]
    public void The_hand_example_reads_exactly_as_the_spec_writes_it()
    {
        var text = ConfirmText.Render(Example());

        text.ShouldBe(
            "BUY 120 TATAPOWER at LIMIT Rs 412.35 (NSE, intraday), valid today. Stop-loss Rs 405.10 and target Rs 425.80 are placed at the broker with the order (one super order). Notional Rs 49,482.00. Worst case at the stop Rs 870.00 (120 x 7.25). " +
            "Account: Dhan · Main. Plan ticket approved by you on 23 Oct at 19:41. This card expires at 09:47:30.");
        ConfirmText.Hash(text).ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void A_canary_ticket_says_one_share()
    {
        var text = ConfirmText.Render(Example(canary: true) with { Quantity = 1 });

        text.ShouldStartWith("BUY 1 TATAPOWER at LIMIT Rs 412.35 (NSE, intraday, canary: 1 share), valid today.");
        text.ShouldContain("Worst case at the stop Rs 7.25 (1 x 7.25).");
    }

    [Theory]
    [InlineData(1, "Rs 412.35.")]
    [InlineData(1000, "Rs 4,12,350.00.")]
    [InlineData(100000, "Rs 4,12,35,000.00.")]
    public void Money_is_grouped_the_indian_way(int quantity, string notional)
    {
        ConfirmText.Render(Example() with { Quantity = quantity }).ShouldContain("Notional " + notional);
    }

    [Fact]
    public void An_apostrophe_or_an_angle_bracket_in_the_account_name_is_kept_as_text()
    {
        var text = ConfirmText.Render(Example() with { AccountName = "Hem's <Main> & co" });

        text.ShouldContain("Account: Dhan · Hem's <Main> & co.");
    }

    [Fact]
    public void Over_2000_generated_tickets_the_text_is_a_pure_injective_function_of_the_parameters_and_names_every_one()
    {
        var random = new Random(20261031);
        for (var n = 0; n < 2000; n++)
        {
            var p = Generate(random);
            var text = ConfirmText.Render(p);
            var hash = ConfirmText.Hash(text);

            ConfirmText.Render(p with { }).ShouldBe(text);
            ConfirmText.Hash(ConfirmText.Render(p)).ShouldBe(hash);
            text.Length.ShouldBeLessThanOrEqualTo(ConfirmText.MaxLength);
            foreach (var part in Parts(p))
            {
                text.ShouldContain(part, Case.Sensitive, $"ticket {n}");
            }

            foreach (var (field, changed) in Mutations(p))
            {
                ConfirmText.Hash(ConfirmText.Render(changed)).ShouldNotBe(hash, $"ticket {n}: changing {field} must change the hash");
            }
        }
    }

    private static ConfirmTicketParams Generate(Random random)
    {
        var limit = random.Next(100, 500_000) / 100m + (random.Next(4) == 0 ? random.Next(1, 100) / 10_000m : 0m);
        var stop = Math.Max(0.05m, limit - (random.Next(5, Math.Max(6, (int)(limit * 10))) / 100m));
        var target = limit + (random.Next(5, 5_000) / 100m);
        var approved = new DateTimeOffset(2026, 10, 1, 0, 0, 0, Ist).AddMinutes(random.Next(0, 60 * 24 * 30));
        var expires = approved.AddDays(1).AddSeconds(random.Next(0, 86_400));
        return new ConfirmTicketParams(
            "BUY", random.Next(1, 5000), Word(random, 3, 12).ToUpperInvariant(), limit, stop, target, "Dhan", Word(random, 1, 20) + (random.Next(5) == 0 ? "'s <x>" : string.Empty), approved, expires, random.Next(2) == 0);
    }

    private static string Word(Random random, int min, int max) => new([.. Enumerable.Range(0, random.Next(min, max + 1)).Select(_ => (char)('a' + random.Next(26)))]);

    private static IEnumerable<string> Parts(ConfirmTicketParams p)
    {
        var ist = (DateTimeOffset t) => t.ToOffset(Ist);
        yield return p.Side + " " + p.Quantity.ToString(CultureInfo.InvariantCulture) + " " + p.Symbol + " ";
        yield return "LIMIT Rs " + p.Limit.ToString("0.00##", CultureInfo.InvariantCulture) + " ";
        yield return "Stop-loss Rs " + p.Stop.ToString("0.00##", CultureInfo.InvariantCulture) + " ";
        yield return "target Rs " + p.Target.ToString("0.00##", CultureInfo.InvariantCulture) + " ";
        yield return "Account: " + p.Broker + " · " + p.AccountName + ". ";
        yield return " on " + ist(p.ApprovedAt).ToString("d MMM 'at' HH:mm", CultureInfo.InvariantCulture) + ". ";
        yield return "expires at " + ist(p.ExpiresAt).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ".";
        yield return p.Canary ? "canary: 1 share" : "(NSE, intraday)";
    }

    private static IEnumerable<(string Field, ConfirmTicketParams Changed)> Mutations(ConfirmTicketParams p)
    {
        yield return ("side", p with { Side = "SELL" });
        yield return ("quantity", p with { Quantity = p.Quantity + 1 });
        yield return ("symbol", p with { Symbol = p.Symbol + "X" });
        yield return ("limit", p with { Limit = p.Limit + 0.01m });
        yield return ("stop", p with { Stop = p.Stop - 0.01m });
        yield return ("target", p with { Target = p.Target + 0.01m });
        yield return ("broker", p with { Broker = "Dhan2" });
        yield return ("account", p with { AccountName = p.AccountName + "x" });
        yield return ("approvedAt", p with { ApprovedAt = p.ApprovedAt.AddMinutes(1) });
        yield return ("expiresAt", p with { ExpiresAt = p.ExpiresAt.AddSeconds(1) });
        yield return ("canary", p with { Canary = !p.Canary });
    }
}
