using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The entities of the dividend file import (ADR 0028): a dividend read from a file, its batch and the remembered scrip names.</summary>
public sealed class DividendImportEntitiesTests
{
    private static readonly Guid Account = HoldingData.Account;

    private static readonly Guid Batch = Guid.CreateVersion7();

    private static Dividend FromFile(Guid? holding = null, string? scrip = "  Orion\tSteel  Ltd ", decimal? perShare = 4m, long? quantity = 25, string externalId = "d:abc", decimal amount = 100m) =>
        Dividend.FromFile(Account, holding, holding is null ? null : Guid.NewGuid(), "ine000a01011", "nse:orion-eq", scrip, new DateOnly(2024, 6, 13), amount, perShare, quantity, Batch, externalId, " owner ", HoldingData.T0);

    [Fact]
    public void A_dividend_of_a_file_has_its_batch_identity_and_quantity_and_the_source_csv()
    {
        var dividend = FromFile(Guid.NewGuid());

        (dividend.Source, dividend.BatchId, dividend.ExternalId, dividend.Quantity, dividend.PerShare, dividend.Amount, dividend.Isin, dividend.TradingSymbol, dividend.CreatedBy, dividend.Note, dividend.IsVoided).ShouldBe(
            (DividendSource.Csv, Batch, "d:abc", 25L, 4m, 100m, "INE000A01011", "ORION", "owner", null, false));
    }

    [Fact]
    public void The_scrip_name_is_kept_as_plain_text_only_when_the_dividend_belongs_to_no_holding()
    {
        var unallocated = FromFile(scrip: "  Orion\tSteel  Ltd ");
        var allocated = FromFile(Guid.NewGuid());

        unallocated.ScripName.ShouldBe("Orion Steel  Ltd");
        allocated.ScripName.ShouldBeNull();
    }

    [Fact]
    public void A_per_share_of_zero_is_not_kept_and_a_quantity_must_be_above_zero_when_given()
    {
        FromFile(perShare: 0m).PerShare.ShouldBeNull();
        FromFile(perShare: null, quantity: null).Quantity.ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => FromFile(quantity: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => FromFile(quantity: -5));
    }

    [Fact]
    public void A_dividend_of_a_file_needs_a_batch_an_identity_of_at_most_64_characters_and_a_real_amount()
    {
        Should.Throw<ArgumentException>(() => Dividend.FromFile(Account, null, null, null, null, "x", new DateOnly(2024, 6, 13), 1m, null, null, Guid.Empty, "d:abc", "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => FromFile(externalId: " "));
        Should.Throw<ArgumentException>(() => FromFile(externalId: new string('x', DividendRules.MaxExternalIdLength + 1)));
        Should.Throw<ArgumentOutOfRangeException>(() => FromFile(amount: 0m));
        FromFile(externalId: new string('x', DividendRules.MaxExternalIdLength)).ExternalId!.Length.ShouldBe(64);
    }

    [Fact]
    public void A_dividend_of_a_file_is_voided_like_any_other()
    {
        var dividend = FromFile();

        var first = dividend.Void("o", HoldingData.T0);
        var second = dividend.Void("o", HoldingData.T0);

        (first, second, dividend.IsVoided).ShouldBe((true, false, true));
    }

    [Fact]
    public void A_typed_in_dividend_has_no_batch_identity_quantity_or_scrip_name()
    {
        var dividend = Dividend.Create(Account, null, null, null, null, new DateOnly(2024, 6, 13), 10m, null, null, "o", HoldingData.T0);

        (dividend.BatchId, dividend.ExternalId, dividend.Quantity, dividend.ScripName, dividend.Source).ShouldBe((null, null, null, null, DividendSource.Manual));
    }

    [Theory]
    [InlineData("  Orion\n Steel ", "Orion  Steel")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_scrip_name_is_made_plain_text(string? name, string? expected)
    {
        DividendRules.CleanScripName(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Orion\u202E Steel", "Orion Steel")]
    [InlineData("\u202Dor\u202Cion\u2066 Steel\u2069", "orion Steel")]
    [InlineData("Orion\u200B Steel\u200D", "Orion Steel")]
    [InlineData("\uFEFFOrion Steel", "Orion Steel")]
    [InlineData("Orion\U000E0041 Steel", "Orion Steel")]
    [InlineData("\u200B\u202E", null)]
    public void A_scrip_name_loses_invisible_format_characters_such_as_bidi_overrides_and_zero_width_marks(string name, string? expected)
    {
        DividendRules.CleanScripName(name).ShouldBe(expected);
    }

    [Fact]
    public void A_scrip_name_keeps_letters_of_other_scripts_and_emoji()
    {
        DividendRules.CleanScripName("नेस्ले इंडिया \U0001F4C8").ShouldBe("नेस्ले इंडिया \U0001F4C8");
    }

    [Fact]
    public void A_scrip_name_is_cut_at_200_characters()
    {
        DividendRules.CleanScripName(new string('a', 300))!.Length.ShouldBe(DividendRules.MaxScripNameLength);
    }

    [Fact]
    public void A_batch_keeps_the_file_name_without_a_path_its_hash_lower_case_and_its_counts()
    {
        var batch = DividendBatch.Start(Batch, Account, @"C:\Users\me/Downloads\Dividend Payout.csv", new string('A', 64), 40, 38, 2, 3, 1234.504m, " owner ", HoldingData.T0);

        (batch.FileName, batch.Sha256, batch.Parsed, batch.Imported, batch.Duplicates, batch.Bad, batch.Amount, batch.Status, batch.CreatedBy, batch.UndoneAt).ShouldBe(
            ("Dividend Payout.csv", new string('a', 64), 40, 38, 2, 3, 1234.50m, DividendBatchStatus.Imported, "owner", null));
    }

    [Fact]
    public void A_batch_without_a_name_gets_a_default_one_and_a_long_name_is_cut()
    {
        DividendBatch.Start(Batch, Account, " \n ", new string('a', 64), 1, 1, 0, 0, 1m, "o", HoldingData.T0).FileName.ShouldBe("dividends.csv");
        DividendBatch.Start(Batch, Account, new string('x', 400), new string('a', 64), 1, 1, 0, 0, 1m, "o", HoldingData.T0).FileName.Length.ShouldBe(DividendBatch.MaxFileNameLength);
    }

    [Fact]
    public void A_batch_needs_an_id_an_account_a_64_character_hash_and_counts_that_are_not_negative()
    {
        Should.Throw<ArgumentException>(() => DividendBatch.Start(Guid.Empty, Account, "f", new string('a', 64), 1, 1, 0, 0, 1m, "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendBatch.Start(Batch, Guid.Empty, "f", new string('a', 64), 1, 1, 0, 0, 1m, "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendBatch.Start(Batch, Account, "f", "abc", 1, 1, 0, 0, 1m, "o", HoldingData.T0));
        Should.Throw<ArgumentOutOfRangeException>(() => DividendBatch.Start(Batch, Account, "f", new string('a', 64), -1, 1, 0, 0, 1m, "o", HoldingData.T0));
        Should.Throw<ArgumentOutOfRangeException>(() => DividendBatch.Start(Batch, Account, "f", new string('a', 64), 1, 1, 0, 0, -1m, "o", HoldingData.T0));
    }

    [Fact]
    public void A_batch_is_undone_once()
    {
        var batch = DividendBatch.Start(Batch, Account, "f", new string('a', 64), 1, 1, 0, 0, 1m, "o", HoldingData.T0);
        var at = HoldingData.T0.AddHours(1);

        var first = batch.Undo(at);
        var second = batch.Undo(at.AddHours(1));

        (first, second, batch.Status, batch.UndoneAt).ShouldBe((true, false, DividendBatchStatus.Undone, at));
    }

    [Fact]
    public void An_alias_points_to_a_holding_an_instrument_or_nothing_and_can_be_pointed_elsewhere()
    {
        var holding = Guid.NewGuid();
        var instrument = Guid.NewGuid();
        var alias = DividendAlias.Create(Account, " orion steel ", holding, null, " owner ", HoldingData.T0);
        var later = HoldingData.T0.AddDays(1);

        var toInstrument = alias.Retarget(null, instrument, later);
        var same = alias.Retarget(null, instrument, later.AddDays(1));
        var toNothing = alias.Retarget(null, null, later.AddDays(2));

        (alias.NameKey, alias.CreatedBy, alias.CreatedAt).ShouldBe(("orion steel", "owner", HoldingData.T0));
        (toInstrument, same, toNothing).ShouldBe((true, false, true));
        (alias.Target, alias.HoldingId, alias.InstrumentId, alias.UpdatedAt).ShouldBe((DividendAliasTarget.None, null, null, later.AddDays(2)));
        DividendAlias.Create(Account, "x", holding, null, "o", HoldingData.T0).Target.ShouldBe(DividendAliasTarget.Holding);
        DividendAlias.Create(Account, "x", null, instrument, "o", HoldingData.T0).Target.ShouldBe(DividendAliasTarget.Instrument);
    }

    [Fact]
    public void An_alias_cannot_point_to_a_holding_and_an_instrument_have_an_empty_or_long_name_or_no_account()
    {
        Should.Throw<ArgumentException>(() => DividendAlias.Create(Account, "x", Guid.NewGuid(), Guid.NewGuid(), "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendAlias.Create(Account, " ", null, null, "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendAlias.Create(Account, new string('x', DividendAlias.MaxNameKeyLength + 1), null, null, "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendAlias.Create(Guid.Empty, "x", null, null, "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendAlias.Create(Account, "x", null, null, "o", HoldingData.T0).Retarget(Guid.NewGuid(), Guid.NewGuid(), HoldingData.T0));
    }

    [Fact]
    public void A_wrong_file_kind_error_is_a_validation_error_that_names_what_was_detected()
    {
        var error = PortfolioErrors.WrongFileKind("dividends", "This is a dividend list.");

        (error.Code, error.Kind, error.Payload!["detected"]).ShouldBe(("portfolio.wrong_file_kind", XDrishti.Domain.Common.ErrorKind.Validation, "dividends"));
    }
}
