using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Tests.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// Which security a scrip name of the global transaction report means (ADR 0029, P2-17 criterion 5): the owner's choice, a remembered alias, the ticker in brackets, the account's holdings, the master, the names that files taught, and spellings that differ by a word's
/// ending - in that order, with ambiguity never guessed. Companies are made up.
/// </summary>
public sealed class SecurityIndexTests
{
    private static readonly Guid Account = Guid.CreateVersion7();

    private const string OrionSteel = "INE902B01012";

    private const string NovaOxides = "INE903C01013";

    private const string ZenithEnergy = "INE904D01014";

    private const string ArcadiaBeverages = "INE905E01015";

    private const string OrionGold = "INF901A01011";

    private static readonly ShareName[] Shares =
    [
        DividendHoldings.Share("Orion Steel Limited", "ORIONSTL", OrionSteel),
        DividendHoldings.Share("Nova Oxides & Chemicals Ltd.", "NOVAOX", NovaOxides),
        DividendHoldings.Share("Zenith Energy Systems Limited", "ZENITHE", ZenithEnergy),
        DividendHoldings.Share("Arcadia Beverages Ltd", "ARCADIA", ArcadiaBeverages),
        DividendHoldings.Share("Orion Gold ETF", "ORIONGLD", OrionGold),
    ];

    private static SecurityIndex Index(IReadOnlyList<HoldingData>? holdings = null, IReadOnlyList<ShareName>? shares = null, IReadOnlyList<DividendAlias>? aliases = null, IReadOnlyList<SecurityAlias>? learned = null) =>
        new(holdings ?? [], shares ?? Shares, aliases ?? [], learned ?? []);

    private static SecurityMatch Match(SecurityIndex index, string name) => index.Match(BillName.Parse(name));

    [Theory]
    [InlineData("ORION STEEL LTD.", OrionSteel)]
    [InlineData("Orion Steel", OrionSteel)]
    [InlineData("ORION STEEL LIMITED", OrionSteel)]
    [InlineData("NOVA OXIDES & CHEM LTD", NovaOxides)]
    [InlineData("NOVA OXIDES & CHEMICALS LTD.", NovaOxides)]
    [InlineData("ZENITH ENERGY SYSTEM LTD", ZenithEnergy)]
    [InlineData("Zenith Energy Systems Ltd", ZenithEnergy)]
    [InlineData("ARCADIA BEVERAGES LIMITED", ArcadiaBeverages)]
    public void The_name_variants_of_the_sample_each_resolve_to_one_isin(string name, string isin)
    {
        var match = Match(Index(), name);

        match.IsResolved.ShouldBeTrue();
        match.Security!.Isin.ShouldBe(isin);
    }

    [Fact]
    public void A_spelling_that_differs_in_a_word_ending_is_a_partial_match_and_an_equal_one_a_master_match()
    {
        var index = Index();

        Match(index, "NOVA OXIDES & CHEM LTD").Kind.ShouldBe(SecurityMatchKind.Partial);
        Match(index, "ZENITH ENERGY SYSTEM LTD").Kind.ShouldBe(SecurityMatchKind.Partial);
        Match(index, "ORION STEEL LTD.").Kind.ShouldBe(SecurityMatchKind.Master);
    }

    [Fact]
    public void The_ticker_in_brackets_decides_before_the_name()
    {
        var match = Match(Index(), "Some Gold Fund (ORIONGLD)");

        match.Kind.ShouldBe(SecurityMatchKind.Symbol);
        match.Security!.Isin.ShouldBe(OrionGold);
    }

    [Fact]
    public void A_bracket_that_is_not_a_known_ticker_is_part_of_the_name()
    {
        var shares = new[] { DividendHoldings.Share("Indus Motors (India) Limited", "INDUSMTR", "INE906F01016") };

        var match = Match(Index(shares: shares), "INDUS MOTORS (INDIA) LIMITED");

        match.Security?.Isin.ShouldBe("INE906F01016");
    }

    [Fact]
    public void The_accounts_holdings_come_before_the_master()
    {
        var held = DividendHoldings.Held("Orion Steel Ltd", "ORIONSTEEL", "INE000ZZ99999");
        var index = Index([held]);

        var match = Match(index, "ORION STEEL LTD.");

        match.Kind.ShouldBe(SecurityMatchKind.Holding);
        match.Security!.Isin.ShouldBe("INE000ZZ99999");
        match.Security.HoldingId.ShouldBe(held.Id);
    }

    [Fact]
    public void A_closed_holding_still_names_its_security_but_an_open_one_wins()
    {
        var closed = DividendHoldings.Held("Orion Steel Ltd", "ORIONOLD", "INE111AA11111", open: false);
        var open = DividendHoldings.Held("Orion Steel Ltd", "ORIONSTEEL", "INE222BB22222");

        Match(Index([closed]), "Orion Steel").Security!.Isin.ShouldBe("INE111AA11111");
        Match(Index([closed, open]), "Orion Steel").Security!.Isin.ShouldBe("INE222BB22222");
    }

    [Fact]
    public void A_remembered_alias_comes_before_the_holdings_and_the_master()
    {
        var held = DividendHoldings.Held("Orion Steel Ltd", "ORIONSTEEL", "INE000ZZ99999");
        var other = DividendHoldings.Held("Something Else", "SOMETHING", "INE777XX77777");
        var alias = DividendHoldings.AliasToHolding("ORION STEEL LTD.", other);

        var match = Match(Index([held, other], aliases: [alias]), "ORION STEEL LTD.");

        match.Kind.ShouldBe(SecurityMatchKind.Alias);
        match.Security!.Isin.ShouldBe("INE777XX77777");
    }

    [Fact]
    public void An_alias_to_an_instrument_leads_to_the_master_share_and_an_alias_to_nothing_says_nothing_about_a_trade()
    {
        var share = Shares[1];
        var toShare = DividendHoldings.AliasToShare("Mystery Name", share, Account);
        var toNothing = DividendHoldings.AliasToNothing("Orion Steel", Account);

        Match(Index(aliases: [toShare]), "Mystery Name").Security!.Isin.ShouldBe(NovaOxides);
        Match(Index(aliases: [toNothing]), "Orion Steel").Kind.ShouldBe(SecurityMatchKind.Master);
    }

    [Fact]
    public void The_owners_choice_beats_everything()
    {
        var index = Index();
        index.Choose(BillName.Parse("ORION STEEL LTD.").Key, index.ByIsin("INE555CC55555"));

        var match = Match(index, "ORION STEEL LTD.");

        match.Kind.ShouldBe(SecurityMatchKind.Choice);
        match.Security!.Isin.ShouldBe("INE555CC55555");
    }

    [Fact]
    public void A_name_a_demat_or_depository_file_taught_resolves_when_nothing_else_knows_it()
    {
        var taught = SecurityAlias.Create(Account, DividendNames.Key("DELISTED RAIL WORKS LTD"), "INE888DD88888", "DELISTED RAIL WORKS LTD", SecurityAliasSource.Demat, "owner", DateTimeOffset.UnixEpoch);

        var match = Match(Index(learned: [taught]), "Delisted Rail Works Limited");

        match.Kind.ShouldBe(SecurityMatchKind.Learned);
        match.Security!.Isin.ShouldBe("INE888DD88888");
    }

    [Fact]
    public void A_learned_isin_takes_the_ticker_of_the_master_share_that_has_it()
    {
        var taught = SecurityAlias.Create(Account, DividendNames.Key("Old Name Of Orion"), OrionSteel, "Old Name Of Orion", SecurityAliasSource.Dp, "owner", DateTimeOffset.UnixEpoch);

        var match = Match(Index(learned: [taught]), "Old Name Of Orion");

        match.Security!.Symbol.ShouldBe("ORIONSTL");
    }

    [Fact]
    public void A_name_that_fits_two_securities_is_ambiguous_and_lists_both_never_guessed()
    {
        var shares = new[] { DividendHoldings.Share("Atlas Power Ltd", "ATLASP", "INE001AA00001"), DividendHoldings.Share("Atlas Power Limited", "ATLASQ", "INE002BB00002") };

        var match = Match(Index(shares: shares), "ATLAS POWER");

        match.IsResolved.ShouldBeFalse();
        match.Kind.ShouldBe(SecurityMatchKind.Ambiguous);
        match.Candidates.Select(c => c.Symbol).ShouldBe(["ATLASP", "ATLASQ"]);
    }

    [Fact]
    public void Two_rows_of_one_security_are_not_ambiguous_and_a_retired_share_gives_way_to_a_current_one()
    {
        var shares = new[] { DividendHoldings.Share("Atlas Power Ltd", "ATLASP", "INE001AA00001"), DividendHoldings.Share("Atlas Power Ltd", "ATLASOLD", "INE003CC00003", retired: true) };

        var match = Match(Index(shares: shares), "Atlas Power");

        match.Security!.Isin.ShouldBe("INE001AA00001");
    }

    [Fact]
    public void A_spelling_variant_that_fits_two_securities_is_ambiguous()
    {
        var shares = new[] { DividendHoldings.Share("Atlas Power Systems", "ATLASP", "INE001AA00001"), DividendHoldings.Share("Atlas Power System", "ATLASQ", "INE002BB00002") };

        Match(Index(shares: shares), "Atlas Power Sys").Kind.ShouldBe(SecurityMatchKind.Ambiguous);
    }

    [Fact]
    public void A_name_nothing_knows_is_none()
    {
        Match(Index(), "UNKNOWN WIDGETS LTD").Kind.ShouldBe(SecurityMatchKind.None);
    }

    [Theory]
    [InlineData("nova oxides and chem", "nova oxides and chemicals", true)]
    [InlineData("zenith energy system", "zenith energy systems", true)]
    [InlineData("orion steel", "orion steel", true)]
    [InlineData("tata power", "tata steel", false)]
    [InlineData("bank of india", "bank of indus", false)]
    [InlineData("alpha beta", "alpha betas gamma", false)]
    [InlineData("al be", "alpha beta", false)]
    [InlineData("atlas", "atlas power", false)]
    public void Two_keys_are_similar_when_they_differ_by_the_ending_of_one_word(string first, string second, bool similar)
    {
        BillName.Similar(first, second).ShouldBe(similar);
    }

    [Fact]
    public void A_security_is_held_when_any_holding_of_the_account_has_its_isin_even_a_closed_one()
    {
        var index = Index([DividendHoldings.Held("Orion Steel Ltd", "ORIONSTEEL", OrionSteel, open: false)]);

        index.Holds(OrionSteel).ShouldBeTrue();
        index.Holds(NovaOxides).ShouldBeFalse();
        index.Holds(null).ShouldBeFalse();
    }

    [Fact]
    public void The_bracket_is_taken_from_the_end_of_the_name_only()
    {
        var name = BillName.Parse("Orion Gold ETF (ORIONGLD)");

        name.Base.ShouldBe("Orion Gold ETF");
        name.SymbolHint.ShouldBe("ORIONGLD");
        name.Key.ShouldBe("orion gold etf");
        BillName.Parse("(Strange) Name").SymbolHint.ShouldBeNull();
        BillName.Parse("  NOVA   OXIDES  &  CHEM  LTD ").Display.ShouldBe("NOVA OXIDES & CHEM LTD");
    }
}
