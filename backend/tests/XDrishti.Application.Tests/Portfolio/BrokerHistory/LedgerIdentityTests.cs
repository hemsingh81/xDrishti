using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>The identity of a ledger entry (ADR 0025): the same lines give the same identities, genuinely identical lines are told apart by their occurrence, another account never shares one.</summary>
public sealed class LedgerIdentityTests
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateOnly Day = new(2026, 9, 2);

    private static IEnumerable<string> Ids(Guid account, params XDrishti.Application.Abstractions.Portfolio.BrokerLedgerRow[] rows) =>
        LedgerIdentity.Identify(account, rows).Select(i => i.ExternalId);

    [Fact]
    public void Reading_the_same_lines_again_gives_the_same_identities()
    {
        var rows = new[] { BrokerScenario.Line(Day, "V1", "RECEIPT", "FUNDS DEPOSIT", 100m), BrokerScenario.Line(Day, "V2", "PAYBLE", "FUNDS WITHDRAWAL", -50m) };

        Ids(Account, rows).ShouldBe(Ids(Account, rows));
    }

    [Fact]
    public void An_identity_is_32_hex_characters()
    {
        var id = Ids(Account, BrokerScenario.Line(Day, "V1", "RECEIPT", "x", 100m)).Single();

        id.Length.ShouldBe(32);
        id.ShouldAllBe(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
    }

    [Fact]
    public void Two_identical_lines_of_the_same_answer_are_both_kept_with_different_identities()
    {
        var line = BrokerScenario.Line(Day, "V1", "RECEIPT", "FUNDS DEPOSIT", 100m);

        var ids = Ids(Account, line, line).ToList();

        ids.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void The_narration_and_the_running_balance_are_not_part_of_the_identity_but_the_date_voucher_description_and_amount_are()
    {
        var baseline = Ids(Account, BrokerScenario.Line(Day, "V1", "RECEIPT", "narration one", 100m, 5m)).Single();

        Ids(Account, BrokerScenario.Line(Day, "V1", "RECEIPT", "narration two", 100m, 999m)).Single().ShouldBe(baseline);
        Ids(Account, BrokerScenario.Line(Day.AddDays(1), "V1", "RECEIPT", "narration one", 100m)).Single().ShouldNotBe(baseline);
        Ids(Account, BrokerScenario.Line(Day, "V2", "RECEIPT", "narration one", 100m)).Single().ShouldNotBe(baseline);
        Ids(Account, BrokerScenario.Line(Day, "V1", "PAYBLE", "narration one", 100m)).Single().ShouldNotBe(baseline);
        Ids(Account, BrokerScenario.Line(Day, "V1", "RECEIPT", "narration one", 100.01m)).Single().ShouldNotBe(baseline);
    }

    [Fact]
    public void Another_account_has_other_identities_for_the_same_lines()
    {
        var line = BrokerScenario.Line(Day, "V1", "RECEIPT", "FUNDS DEPOSIT", 100m);

        Ids(Account, line).Single().ShouldNotBe(Ids(Guid.NewGuid(), line).Single());
    }
}
