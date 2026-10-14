using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendHoldings;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The scrip-name matcher (ADR 0028, P2-15 criteria 4 to 6): the six rules in order, ambiguity never guessed, aliases first. Table driven, with made-up companies.</summary>
public sealed class DividendNameIndexTests
{
    private static DividendNameIndex Index(IReadOnlyList<HoldingData>? holdings = null, IReadOnlyList<ShareName>? shares = null, IReadOnlyList<DividendAlias>? aliases = null) =>
        new(holdings ?? [], shares ?? [], aliases ?? []);

    private static DividendMatch Match(DividendNameIndex index, string name, string? isin = null, string? symbol = null) => index.Match(Parsed(name, isin: isin, symbol: symbol));

    [Theory]
    [InlineData("Orion Steel", "ORION STEEL LIMITED")]
    [InlineData("ORION STEEL LTD.", "Orion Steel Industries")]
    [InlineData("Orion Steel", "Orion Steel Corporation")]
    [InlineData("Mahindra & Mahindra", "MAHINDRA AND MAHINDRA LTD")]
    [InlineData("Dr. Lal Path Labs", "DR LAL PATH LABS LTD")]
    public void An_equal_name_after_case_punctuation_ampersand_and_suffixes_matches_an_open_holding(string fileName, string holdingName)
    {
        var holding = Held(holdingName, "SYM1");

        var match = Match(Index([holding]), fileName);

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Name, holding.Id));
    }

    [Fact]
    public void A_closed_holding_is_not_matched_by_name_and_the_name_falls_through()
    {
        var closed = Held("Orion Steel Limited", "ORION", open: false);

        var match = Match(Index([closed]), "Orion Steel");

        match.Kind.ShouldBe(DividendMatchKind.None);
        match.IsMatched.ShouldBeFalse();
    }

    [Fact]
    public void The_trading_symbol_is_a_name_too_so_a_file_that_writes_the_ticker_matches()
    {
        var holding = Held("Orion Steel Limited", "ORIONST");

        var match = Match(Index([holding]), "OrionSt");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Name, holding.Id));
    }

    [Fact]
    public void Two_open_holdings_with_the_same_name_are_ambiguous_with_both_listed_and_nothing_applied()
    {
        var nse = Held("Orion Steel Limited", "ORION");
        var bse = Held("ORION STEEL LTD", "ORION-B");

        var match = Match(Index([bse, nse]), "Orion Steel");

        match.IsAmbiguous.ShouldBeTrue();
        match.IsMatched.ShouldBeFalse();
        match.Candidates.Select(c => c.TradingSymbol).ShouldBe(["ORION", "ORION-B"]);
        match.HoldingId.ShouldBeNull();
    }

    [Fact]
    public void An_exact_name_beats_a_containing_name()
    {
        var exact = Held("Kirloskar Oil Engines Limited", "KOEL");
        var longer = Held("Kirloskar Oil Engines Marine Division", "KOEM");

        var match = Match(Index([longer, exact]), "Kirloskar Oil Engines");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Name, exact.Id));
    }

    [Fact]
    public void A_name_inside_exactly_one_open_holdings_name_matches_by_containment()
    {
        var holding = Held("Kirloskar Oil Engines Limited Marine", "KOEM");

        var match = Match(Index([holding, Held("Orion Steel", "ORION")]), "Kirloskar Oil");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Contains, holding.Id));
    }

    [Fact]
    public void A_holdings_name_inside_the_files_longer_name_matches_by_containment_too()
    {
        var holding = Held("Kirloskar Oil", "KOIL");

        var match = Match(Index([holding]), "Kirloskar Oil Engines Marine");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Contains, holding.Id));
    }

    [Fact]
    public void A_name_that_fits_two_holdings_by_containment_is_ambiguous_and_not_applied()
    {
        var first = Held("Kirloskar Oil Engines Marine", "KOEM");
        var second = Held("Kirloskar Oil Pumps", "KOP");

        var match = Match(Index([first, second]), "Kirloskar Oil");

        match.IsAmbiguous.ShouldBeTrue();
        match.Kind.ShouldBe(DividendMatchKind.Contains);
        match.Candidates.Select(c => c.Id).ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public void A_single_significant_word_never_matches_by_containment()
    {
        var holding = Held("Union Bank of India", "UNIONBANK");

        var match = Match(Index([holding]), "Bank of India");

        match.Kind.ShouldBe(DividendMatchKind.None);
    }

    [Fact]
    public void The_isin_column_beats_a_name_and_two_holdings_with_one_isin_are_ambiguous()
    {
        var named = Held("Orion Steel Limited", "ORION", "INE000A01011");
        var byIsin = Held("Something Else", "OTHER", "INE111B01022");
        var twin = Held("Something Else Too", "OTHERB", "INE111B01022");

        var one = Match(Index([named, byIsin]), "Orion Steel", isin: "INE111B01022");
        var two = Match(Index([named, byIsin, twin]), "Orion Steel", isin: "INE111B01022");

        (one.Kind, one.HoldingId).ShouldBe((DividendMatchKind.Isin, byIsin.Id));
        (two.Kind, two.IsAmbiguous).ShouldBe((DividendMatchKind.Isin, true));
    }

    [Fact]
    public void The_symbol_column_matches_an_open_holding_and_an_isin_nobody_holds_falls_back_to_the_name()
    {
        var holding = Held("Orion Steel Limited", "ORION", "INE000A01011");

        var bySymbol = Match(Index([holding]), "Unrelated Words", symbol: "ORION");
        var unknownIsin = Match(Index([holding]), "Orion Steel", isin: "INE999Z01099");

        (bySymbol.Kind, bySymbol.HoldingId).ShouldBe((DividendMatchKind.Symbol, holding.Id));
        (unknownIsin.Kind, unknownIsin.HoldingId).ShouldBe((DividendMatchKind.Name, holding.Id));
    }

    [Fact]
    public void A_remembered_alias_wins_over_every_other_rule()
    {
        var byName = Held("Orion Steel Limited", "ORION", "INE000A01011");
        var chosen = Held("Delta Power Limited", "DELTA");
        var alias = AliasToHolding("Orion Steel", chosen);

        var match = Match(Index([byName, chosen], aliases: [alias]), "ORION STEEL LTD", isin: "INE000A01011");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Alias, chosen.Id));
    }

    [Fact]
    public void An_alias_can_point_to_an_instrument_that_is_not_held_or_to_nothing()
    {
        var share = Share("ZEPHYR MOTORS LTD", "ZEPHYR", "INE222C01033");
        var account = Guid.NewGuid();
        var index = Index(shares: [share], aliases: [AliasToShare("Zephyr", share, account), AliasToNothing("Nimbus Chemicals", account)]);

        var toShare = Match(index, "Zephyr");
        var toNothing = Match(index, "Nimbus Chemicals Ltd");

        (toShare.Kind, toShare.HoldingId, toShare.InstrumentId).ShouldBe((DividendMatchKind.Alias, null, share.Id));
        (toNothing.Kind, toNothing.IsMatched, toNothing.InstrumentId).ShouldBe((DividendMatchKind.Alias, false, null));
    }

    [Fact]
    public void An_alias_to_a_holding_the_account_no_longer_has_is_ignored_and_the_rules_go_on()
    {
        var gone = Held("Gone Limited", "GONE");
        var present = Held("Orion Steel Limited", "ORION");
        var alias = AliasToHolding("Orion Steel", gone);

        var match = Match(Index([present], aliases: [alias]), "Orion Steel");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Name, present.Id));
    }

    [Fact]
    public void An_alias_may_point_to_a_closed_holding_because_the_owner_chose_it()
    {
        var closed = Held("Orion Steel Limited", "ORION", open: false);
        var alias = AliasToHolding("Orion Steel", closed);

        var match = Match(Index([closed], aliases: [alias]), "Orion Steel");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Alias, closed.Id));
    }

    [Fact]
    public void A_name_no_holding_has_but_one_master_share_has_keeps_the_instrument_and_no_holding()
    {
        var share = Share("DELTA POWER CORPORATION LTD", "DELTAPWR", "INE333D01044", displayName: "Delta Power");

        var byName = Match(Index(shares: [share]), "Delta Power");
        var byIsin = Match(Index(shares: [share]), "Anything", isin: "INE333D01044");
        var bySymbol = Match(Index(shares: [share]), "Anything", symbol: "DELTAPWR");

        foreach (var match in new[] { byName, byIsin, bySymbol })
        {
            (match.Kind, match.HoldingId, match.InstrumentId).ShouldBe((DividendMatchKind.Master, null, share.Id));
        }
    }

    [Fact]
    public void An_open_holding_is_preferred_to_the_master_for_the_same_name()
    {
        var holding = Held("Delta Power Limited", "DELTAPWR");
        var share = Share("DELTA POWER LTD", "DELTAPWR");

        var match = Match(Index([holding], [share]), "Delta Power");

        (match.Kind, match.HoldingId).ShouldBe((DividendMatchKind.Name, holding.Id));
    }

    [Fact]
    public void Two_current_master_shares_with_one_name_are_not_guessed_and_a_retired_one_gives_way_to_a_current_one()
    {
        var current = Share("DELTA POWER LTD", "DELTAPWR");
        var retired = Share("DELTA POWER LIMITED", "DELTAOLD", retired: true);
        var other = Share("DELTA POWER CORPORATION", "DELTAB");

        var withRetired = Match(Index(shares: [retired, current]), "Delta Power");
        var twoCurrent = Match(Index(shares: [current, other]), "Delta Power");
        var onlyRetired = Match(Index(shares: [retired]), "Delta Power");

        withRetired.InstrumentId.ShouldBe(current.Id);
        (twoCurrent.Kind, twoCurrent.IsMatched).ShouldBe((DividendMatchKind.None, false));
        onlyRetired.InstrumentId.ShouldBe(retired.Id);
    }

    [Fact]
    public void A_name_nothing_knows_is_none_and_the_answers_are_remembered_per_name_isin_and_symbol()
    {
        var index = Index([Held("Orion Steel Limited", "ORION")]);

        var first = Match(index, "Unknown Company");
        var again = Match(index, "UNKNOWN COMPANY LTD");

        first.Kind.ShouldBe(DividendMatchKind.None);
        again.ShouldBeSameAs(first);
    }

    [Fact]
    public void Holdings_and_shares_are_found_by_id_for_the_owners_choices()
    {
        var holding = Held("Orion Steel Limited", "ORION");
        var share = Share("DELTA", "DELTA");
        var index = Index([holding], [share]);

        index.Holding(holding.Id).ShouldBe(holding);
        index.Share(share.Id).ShouldBe(share);
        index.Holding(Guid.NewGuid()).ShouldBeNull();
        index.Share(Guid.NewGuid()).ShouldBeNull();
    }
}
