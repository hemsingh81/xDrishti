using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>A ledger entry of Dhan (ADR 0025): what it keeps, how its texts are cleaned and capped, and the class codes.</summary>
public sealed class LedgerEntryTests
{
    private static readonly Guid Account = HoldingData.Account;

    private static LedgerEntry Entry(string? description = "RECEIPT", string? narration = "FUNDS DEPOSIT", decimal amount = 100.005m, string? exchange = "NSE-CAPITAL", decimal? balance = 12.345m, string externalId = "abc") =>
        LedgerEntry.Create(Account, new DateOnly(2026, 9, 2), " V1 ", description, narration, exchange, amount, LedgerClass.FundsIn, balance, externalId, Guid.NewGuid(), HoldingData.T0);

    [Fact]
    public void An_entry_rounds_amounts_to_paise_and_trims_the_voucher_number()
    {
        var entry = Entry();

        (entry.Amount, entry.RunningBalance, entry.VoucherNo, entry.Exchange, entry.EntryClass).ShouldBe((100.01m, 12.35m, "V1", "NSE-CAPITAL", LedgerClass.FundsIn));
    }

    [Fact]
    public void Free_text_loses_control_characters_and_is_capped()
    {
        var entry = Entry(description: new string('d', 300), narration: "line one\nline\ttwo\r" + new string('n', 700));

        entry.Description.Length.ShouldBe(LedgerEntry.MaxDescriptionLength);
        entry.Narration.Length.ShouldBe(LedgerEntry.MaxNarrationLength);
        entry.Narration.ShouldStartWith("line one line two ");
        entry.Narration.ShouldNotContain('\n');
    }

    [Fact]
    public void Missing_texts_are_empty_and_a_blank_exchange_is_none()
    {
        var entry = Entry(description: null, narration: null, exchange: "  ", balance: null);

        (entry.Description, entry.Narration, entry.Exchange, entry.RunningBalance).ShouldBe((string.Empty, string.Empty, null, null));
    }

    [Fact]
    public void An_entry_needs_an_account_and_an_identity()
    {
        Should.Throw<ArgumentException>(() => LedgerEntry.Create(Guid.Empty, new DateOnly(2026, 9, 2), "V", "d", "n", null, 1m, LedgerClass.Other, null, "id", Guid.NewGuid(), HoldingData.T0));
        Should.Throw<ArgumentException>(() => Entry(externalId: ""));
        Should.Throw<ArgumentException>(() => Entry(externalId: new string('x', LedgerEntry.MaxExternalIdLength + 1)));
    }

    [Fact]
    public void The_class_codes_round_trip_and_unknown_codes_are_refused()
    {
        foreach (var value in LedgerClasses.All)
        {
            LedgerClasses.TryParse(LedgerClasses.Code(value), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(value);
        }

        LedgerClasses.All.Select(LedgerClasses.Code).ShouldBe(["funds_in", "funds_out", "dividend", "charges", "other"]);
        LedgerClasses.TryParse("gifts", out _).ShouldBeFalse();
        LedgerClasses.TryParse(null, out _).ShouldBeFalse();
    }
}
