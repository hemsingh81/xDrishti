using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendHoldings;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>Identity, status, warnings, digest and selection of the dividend planner (ADR 0028, P2-15 criteria 5, 8, 9 and the checks). Pure, on made-up rows.</summary>
public sealed class DividendPlannerTests
{
    private static readonly Guid Account = HoldingFixtures.Account;

    private static DividendFile FileOf(params ParsedDividend[] rows) => new(rows.Length, rows, 0, [], rows.Sum(r => r.Amount), [], 0);

    private static DividendPlan Plan(
        IReadOnlyList<ParsedDividend> rows,
        IReadOnlyList<HoldingData>? holdings = null,
        IReadOnlyList<ShareName>? shares = null,
        IReadOnlyList<DividendAlias>? aliases = null,
        IEnumerable<string>? stored = null,
        IReadOnlyList<StoredDividendKey>? manual = null,
        DateOnly? firstLot = null,
        Guid? account = null)
    {
        var file = FileOf([.. rows]);
        return DividendPlanner.Plan(
            file,
            DividendIdentity.Identify(account ?? Account, rows),
            new DividendNameIndex(holdings ?? [], shares ?? [], aliases ?? []),
            (stored ?? []).ToHashSet(StringComparer.Ordinal),
            manual ?? [],
            firstLot);
    }

    [Fact]
    public void The_same_payment_has_the_same_identity_whatever_the_line_or_the_spelling_of_the_name_and_another_account_has_another()
    {
        var a = Parsed("Orion Steel Limited", line: 2, perShare: 5, quantity: 100, amount: 500);
        var b = Parsed("ORION STEEL LTD.", line: 99, perShare: 5, quantity: 100, amount: 500);

        var first = DividendIdentity.Identify(Account, [a])[0].ExternalId;
        var same = DividendIdentity.Identify(Account, [b])[0].ExternalId;
        var other = DividendIdentity.Identify(Guid.NewGuid(), [a])[0].ExternalId;

        same.ShouldBe(first);
        other.ShouldNotBe(first);
        first.ShouldStartWith("d:");
        first.Length.ShouldBe(34);
    }

    [Fact]
    public void Payments_that_differ_in_date_per_share_quantity_or_amount_are_different_identities()
    {
        var basis = Parsed("Lumen Oils", "2025-03-18", 500, perShare: 5, quantity: 100);
        var variants = new[]
        {
            Parsed("Lumen Oils", "2025-03-19", 500, perShare: 5, quantity: 100),
            Parsed("Lumen Oils", "2025-03-18", 450, perShare: 4.5m, quantity: 100),
            Parsed("Lumen Oils", "2025-03-18", 500, perShare: 5, quantity: 99),
            Parsed("Lumen Oils", "2025-03-18", 500, perShare: null, quantity: 100),
            Parsed("Lumen Oils", "2025-03-18", 501, perShare: 5, quantity: 100),
            Parsed("Lumen Power", "2025-03-18", 500, perShare: 5, quantity: 100),
        };

        var ids = DividendIdentity.Identify(Account, [basis, .. variants]).Select(i => i.ExternalId).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void A_row_that_is_new_matched_unmatched_ambiguous_stored_or_a_duplicate_of_a_typed_in_dividend_gets_that_status()
    {
        var orion = Held("Orion Steel Limited", "ORION");
        var twinA = Held("Delta Power Limited", "DELTA");
        var twinB = Held("DELTA POWER LTD", "DELTA-B");
        var rows = new[]
        {
            Parsed("Orion Steel", "2025-03-18", 100, line: 2),
            Parsed("Nobody Knows", "2025-03-18", 100, line: 3),
            Parsed("Delta Power", "2025-03-18", 100, line: 4),
            Parsed("Orion Steel", "2025-04-18", 100, line: 5),
            Parsed("Orion Steel", "2025-05-18", 250, line: 6),
        };
        var identities = DividendIdentity.Identify(Account, rows);

        var plan = Plan(rows, [orion, twinA, twinB], stored: [identities[3].ExternalId], manual: [new StoredDividendKey(orion.Id, new DateOnly(2025, 5, 18), 250m)]);

        plan.Rows.Select(r => r.Status).ShouldBe([DividendStatus.New, DividendStatus.NeedsMatch, DividendStatus.Ambiguous, DividendStatus.Imported, DividendStatus.PossibleDuplicate]);
    }

    [Fact]
    public void A_row_equal_to_a_typed_in_dividend_needs_the_same_holding_date_and_amount()
    {
        var orion = Held("Orion Steel Limited", "ORION");
        var delta = Held("Delta Power Limited", "DELTA");
        var rows = new[] { Parsed("Orion Steel", "2025-03-18", 100, line: 2), Parsed("Orion Steel", "2025-03-19", 100, line: 3), Parsed("Orion Steel", "2025-03-20", 101, line: 4), Parsed("Delta Power", "2025-03-18", 100, line: 5) };
        var manual = new[] { new StoredDividendKey(orion.Id, new DateOnly(2025, 3, 18), 100m), new StoredDividendKey(orion.Id, new DateOnly(2025, 3, 20), 100m), new StoredDividendKey(null, new DateOnly(2025, 3, 19), 100m) };

        var plan = Plan(rows, [orion, delta], manual: manual);

        plan.Rows.Select(r => r.Status).ShouldBe([DividendStatus.PossibleDuplicate, DividendStatus.New, DividendStatus.New, DividendStatus.New]);
    }

    [Fact]
    public void A_remembered_nothing_is_new_not_a_name_that_needs_a_match()
    {
        var plan = Plan([Parsed("Nimbus Chemicals")], aliases: [AliasToNothing("Nimbus Chemicals", Account)]);

        plan.Rows[0].Status.ShouldBe(DividendStatus.New);
        plan.Rows[0].Match.IsMatched.ShouldBeFalse();
    }

    [Theory]
    [InlineData(5, 100, 500.00, false)]
    [InlineData(5, 100, 500.01, false)]
    [InlineData(5, 100, 499.99, false)]
    [InlineData(5, 100, 500.02, true)]
    [InlineData(4.5, 100, 500, true)]
    [InlineData(2.2, 100, 230, true)]
    public void The_paid_amount_is_flagged_when_it_differs_from_per_share_times_quantity_by_more_than_a_paisa(double perShare, long quantity, double paid, bool flagged)
    {
        var plan = Plan([Parsed("Orion Steel", perShare: (decimal)perShare, quantity: quantity, amount: (decimal)paid)]);

        plan.Rows[0].Warnings.Contains(DividendStatuses.AmountMismatch).ShouldBe(flagged);
    }

    [Fact]
    public void A_row_without_per_share_or_quantity_has_nothing_to_compare()
    {
        var plan = Plan([Parsed("Orion Steel", amount: 500), Parsed("Orion Steel", "2025-03-19", 500, perShare: 5), Parsed("Orion Steel", "2025-03-20", 500, quantity: 100)]);

        plan.Rows.SelectMany(r => r.Warnings).ShouldBeEmpty();
    }

    [Fact]
    public void Dividends_dated_before_the_first_lot_are_flagged_and_counted_in_the_report_and_without_a_lot_nothing_is_flagged()
    {
        var rows = new[] { Parsed("Orion Steel", "2024-12-31", 100, line: 2), Parsed("Orion Steel", "2025-01-01", 200, line: 3), Parsed("Orion Steel", "2025-06-01", 300, line: 4) };

        var with = DividendImportViews.Report("f.csv", Plan(rows, firstLot: new DateOnly(2025, 1, 1)));
        var without = DividendImportViews.Report("f.csv", Plan(rows));

        with.Items.Select(i => i.Warnings.Contains(DividendStatuses.BeforeFirstLot)).ShouldBe([true, false, false]);
        with.BeforeFirstLot.ShouldBe(new DividendBeforeFirstLot(1, 100m, new DateOnly(2025, 1, 1)));
        without.BeforeFirstLot.ShouldBe(new DividendBeforeFirstLot(0, 0m, null));
        without.Items.SelectMany(i => i.Warnings).ShouldBeEmpty();
    }

    [Fact]
    public void A_match_by_part_of_the_name_carries_a_warning()
    {
        var holding = Held("Kirloskar Oil Engines Marine", "KOEM");

        var plan = Plan([Parsed("Kirloskar Oil")], [holding]);

        plan.Rows[0].Warnings.ShouldBe([DividendStatuses.PartialNameMatch]);
    }

    [Fact]
    public void The_totals_count_and_sum_only_what_would_be_added_by_year()
    {
        var rows = new[]
        {
            Parsed("Orion Steel", "2024-06-13", 100, line: 2),
            Parsed("Orion Steel", "2025-06-13", 200, line: 3),
            Parsed("Nobody Knows", "2025-07-13", 50, line: 4),
            Parsed("Orion Steel", "2026-06-13", 400, line: 5),
            Parsed("Orion Steel", "2026-07-13", 800, line: 6),
        };
        var identities = DividendIdentity.Identify(Account, rows);
        var holding = Held("Orion Steel Limited", "ORION");

        var report = DividendImportViews.Report("f.csv", Plan(rows, [holding], stored: [identities[3].ExternalId], manual: [new StoredDividendKey(holding.Id, new DateOnly(2026, 7, 13), 800m)]));

        report.Totals.Count.ShouldBe(3);
        report.Totals.Amount.ShouldBe(350m);
        report.Totals.ByYear.ShouldBe([new DividendYearTotal(2024, 1, 100m), new DividendYearTotal(2025, 2, 250m)]);
        (report.File.AlreadyImported, report.File.PossibleDuplicates, report.File.ToAdd, report.File.NeedsMatch).ShouldBe((1, 1, 3, 1));
    }

    [Fact]
    public void The_digest_is_the_same_for_the_same_world_and_changes_when_a_match_or_a_stored_row_changes()
    {
        var rows = new[] { Parsed("Orion Steel", line: 2), Parsed("Delta Power", "2025-04-01", line: 3) };
        var orion = Held("Orion Steel Limited", "ORION");
        var delta = Held("Delta Power Limited", "DELTA");
        var identities = DividendIdentity.Identify(Account, rows);

        var basis = Plan(rows, [orion, delta]).Digest;
        var same = Plan(rows, [orion, delta]).Digest;
        var newHolding = Plan(rows, [orion, delta, Held("Delta Power Corp", "DELTAX")]).Digest;
        var stored = Plan(rows, [orion, delta], stored: [identities[1].ExternalId]).Digest;
        var aliased = Plan(rows, [orion, delta], aliases: [AliasToHolding("Delta Power", orion)]).Digest;
        var unrelated = Plan(rows, [orion, delta, Held("Zephyr Motors", "ZEPHYR")]).Digest;

        same.ShouldBe(basis);
        unrelated.ShouldBe(basis);
        new[] { newHolding, stored, aliased }.ShouldAllBe(d => d != basis);
        basis.Length.ShouldBe(64);
    }

    [Fact]
    public void The_digest_changes_when_the_file_does()
    {
        var one = Plan([Parsed("Orion Steel", amount: 100)]).Digest;
        var other = Plan([Parsed("Orion Steel", amount: 101)]).Digest;

        other.ShouldNotBe(one);
    }

    [Fact]
    public void Select_takes_the_rows_to_add_and_leaves_out_stored_ones_and_unticked_duplicates()
    {
        var orion = Held("Orion Steel Limited", "ORION");
        var rows = new[] { Parsed("Orion Steel", "2025-01-10", 100, line: 2), Parsed("Orion Steel", "2025-02-10", 100, line: 3), Parsed("Orion Steel", "2025-03-10", 100, line: 4), Parsed("Orion Steel", "2025-04-10", 100, line: 5) };
        var identities = DividendIdentity.Identify(Account, rows);
        var plan = Plan(
            rows,
            [orion],
            stored: [identities[0].ExternalId],
            manual: [new StoredDividendKey(orion.Id, new DateOnly(2025, 2, 10), 100m), new StoredDividendKey(orion.Id, new DateOnly(2025, 3, 10), 100m)]);

        var none = DividendPlanner.Select(plan, [], new HashSet<int>()).Value;
        var ticked = DividendPlanner.Select(plan, [], new HashSet<int> { 4, 99 }).Value;

        none.ToStore.Select(s => s.Planned.Row.Line).ShouldBe([5]);
        (none.AlreadyImported, none.PossibleDuplicatesSkipped).ShouldBe((1, 2));
        ticked.ToStore.Select(s => s.Planned.Row.Line).ShouldBe([4, 5]);
        ticked.PossibleDuplicatesSkipped.ShouldBe(1);
    }

    [Fact]
    public void A_choice_of_a_holding_applies_to_every_row_with_that_name_whatever_its_spelling_and_can_be_remembered()
    {
        var delta = Held("Delta Power Limited", "DELTA");
        var rows = new[] { Parsed("Delta  Power Corp", line: 2), Parsed("DELTA POWER CORPORATION LTD", "2025-04-01", line: 3), Parsed("Other Name", "2025-05-01", line: 4) };
        var plan = Plan(rows, [delta, Held("Delta Power Pumps", "DELTAP")]);

        var selection = DividendPlanner.Select(plan, [new DividendChoice("delta power", HoldingId: delta.Id, Remember: true)], new HashSet<int>()).Value;

        selection.ToStore.Select(s => (s.Planned.Row.Line, s.Match.HoldingId)).ShouldBe([(2, delta.Id), (3, delta.Id), (4, null)]);
        selection.Aliases.ShouldBe([new AliasToSave("delta power", delta.Id, null)]);
    }

    [Fact]
    public void A_choice_of_an_instrument_or_of_nothing_is_taken_and_only_remembered_when_asked()
    {
        var share = Share("ZEPHYR MOTORS LTD", "ZEPHYR", "INE222C01033");
        var plan = Plan([Parsed("Zephyr Motors", line: 2), Parsed("Quartz Engineering", line: 3)], shares: [share]);

        var selection = DividendPlanner.Select(plan, [new DividendChoice("Zephyr Motors", InstrumentId: share.Id), new DividendChoice("Quartz Engineering", None: true, Remember: true)], new HashSet<int>()).Value;

        selection.ToStore.Select(s => (s.Match.InstrumentId, s.Match.HoldingId)).ShouldBe([(share.Id, null), (null, null)]);
        selection.Aliases.ShouldBe([new AliasToSave("quartz engineering", null, null)]);
    }

    [Fact]
    public void A_choice_that_makes_a_row_equal_to_a_typed_in_dividend_leaves_it_out_unless_ticked()
    {
        var delta = Held("Delta Power Limited", "DELTA");
        var plan = Plan([Parsed("Mystery", "2025-04-01", 100, line: 2)], [delta], manual: [new StoredDividendKey(delta.Id, new DateOnly(2025, 4, 1), 100m)]);
        var choice = new DividendChoice("Mystery", HoldingId: delta.Id);

        var left = DividendPlanner.Select(plan, [choice], new HashSet<int>()).Value;
        var ticked = DividendPlanner.Select(plan, [choice], new HashSet<int> { 2 }).Value;

        (left.ToStore.Count, left.PossibleDuplicatesSkipped).ShouldBe((0, 1));
        ticked.ToStore.Count.ShouldBe(1);
    }

    [Fact]
    public void An_ambiguous_name_without_a_choice_is_stored_unallocated_and_nothing_is_guessed()
    {
        var plan = Plan([Parsed("Delta Power")], [Held("Delta Power Limited", "DELTA"), Held("DELTA POWER LTD", "DELTA-B")]);

        var selection = DividendPlanner.Select(plan, [], new HashSet<int>()).Value;

        selection.ToStore.ShouldHaveSingleItem().Match.IsMatched.ShouldBeFalse();
    }

    public static TheoryData<DividendChoice, string> BadChoices => new()
    {
        { new DividendChoice("Nobody In The File", None: true), "No row of the file" },
        { new DividendChoice("Orion Steel"), "exactly one" },
        { new DividendChoice("Orion Steel", HoldingId: Guid.NewGuid(), None: true), "exactly one" },
        { new DividendChoice("Orion Steel", HoldingId: Guid.NewGuid()), "not one of the account's" },
        { new DividendChoice("Orion Steel", InstrumentId: Guid.NewGuid()), "NSE share or ETF" },
    };

    [Theory]
    [MemberData(nameof(BadChoices))]
    public void A_choice_naming_no_row_no_holding_of_the_account_no_share_or_not_one_target_is_refused_on_the_field_choices(DividendChoice choice, string reason)
    {
        var plan = Plan([Parsed("Orion Steel")], [Held("Orion Steel Limited", "ORION")]);

        var result = DividendPlanner.Select(plan, [choice], new HashSet<int>());

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Field.ShouldBe("choices");
        result.Error.Fields!.Single().Message.ShouldContain(reason);
    }

    [Fact]
    public void Too_many_choices_are_refused()
    {
        var plan = Plan([Parsed("Orion Steel")]);

        var many = DividendPlanner.Select(plan, [.. Enumerable.Repeat(new DividendChoice("Orion Steel", None: true), DividendCsvLimits.MaxChoices + 1)], new HashSet<int>());

        many.Error.Fields!.Single().Message.ShouldContain("At most");
    }

    [Fact]
    public void Two_spellings_of_one_company_have_one_key_and_choices_for_both_that_agree_are_one_choice_remembered_once()
    {
        var delta = Held("Delta Power Limited", "DELTA");
        var plan = Plan([Parsed("Kestrel Foods & Beverages", line: 2), Parsed("Kestrel Foods and Beverages Ltd", "2025-04-01", line: 3)], [delta]);

        var selection = DividendPlanner.Select(
            plan,
            [new DividendChoice("Kestrel Foods & Beverages", HoldingId: delta.Id, Remember: true), new DividendChoice("Kestrel Foods and Beverages Ltd", HoldingId: delta.Id, Remember: true)],
            new HashSet<int>()).Value;

        plan.Rows.Select(r => r.Row.NameKey).Distinct().ShouldBe(["kestrel foods and beverages"]);
        selection.ToStore.Select(s => (s.Planned.Row.Line, s.Match.HoldingId)).ShouldBe([(2, delta.Id), (3, delta.Id)]);
        selection.Aliases.ShouldBe([new AliasToSave("kestrel foods and beverages", delta.Id, null)]);
    }

    [Fact]
    public void Agreeing_choices_for_one_key_remember_the_name_when_any_of_them_asks_to()
    {
        var plan = Plan([Parsed("Kestrel Foods & Beverages", line: 2), Parsed("Kestrel Foods and Beverages Ltd", "2025-04-01", line: 3)]);

        var selection = DividendPlanner.Select(
            plan,
            [new DividendChoice("Kestrel Foods & Beverages", None: true), new DividendChoice("Kestrel Foods and Beverages Ltd", None: true, Remember: true)],
            new HashSet<int>()).Value;

        selection.Aliases.ShouldBe([new AliasToSave("kestrel foods and beverages", null, null)]);
    }

    [Fact]
    public void Choices_for_one_key_that_disagree_are_refused_naming_the_key()
    {
        var delta = Held("Delta Power Limited", "DELTA");
        var plan = Plan([Parsed("Kestrel Foods & Beverages", line: 2), Parsed("Kestrel Foods and Beverages Ltd", "2025-04-01", line: 3)], [delta]);

        var result = DividendPlanner.Select(
            plan,
            [new DividendChoice("Kestrel Foods & Beverages", HoldingId: delta.Id), new DividendChoice("Kestrel Foods and Beverages Ltd", None: true)],
            new HashSet<int>());

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Field.ShouldBe("choices");
        result.Error.Fields!.Single().Message.ShouldContain("kestrel foods and beverages");
        result.Error.Fields!.Single().Message.ShouldContain("disagree");
    }

    [Fact]
    public void A_choice_may_name_the_scrip_by_the_key_the_server_gave_the_row()
    {
        var delta = Held("Delta Power Limited", "DELTA");
        var plan = Plan([Parsed("Delta Power Corp", line: 2), Parsed("Other Name", "2025-05-01", line: 3)], [delta, Held("Delta Power Pumps", "DELTAP")]);
        var key = plan.Rows[0].Row.NameKey;

        var selection = DividendPlanner.Select(plan, [new DividendChoice(key, HoldingId: delta.Id, Remember: true)], new HashSet<int>()).Value;

        selection.ToStore.Select(s => (s.Planned.Row.Line, s.Match.HoldingId)).ShouldBe([(2, delta.Id), (3, null)]);
        selection.Aliases.ShouldBe([new AliasToSave(key, delta.Id, null)]);
    }

    [Fact]
    public void The_key_of_a_key_is_the_key()
    {
        foreach (var name in new[] { "Kestrel Foods & Beverages", "Power Finance Corporation Ltd.", "India Ltd", "N.M.D.C.", "Bank of India" })
        {
            var key = DividendNames.Key(name);

            DividendNames.Key(key).ShouldBe(key);
        }
    }

    [Fact]
    public void A_row_without_a_holding_is_a_possible_duplicate_only_of_a_typed_in_dividend_of_the_same_scrip_or_of_none_in_particular()
    {
        var zephyr = Share("ZEPHYR MOTORS LTD", "ZEPHYR", "INE222C01033");
        var day = new DateOnly(2025, 4, 1);
        var rows = new[] { Parsed("Zephyr Motors", "2025-04-01", 100, line: 2), Parsed("Quartz Engineering", "2025-04-01", 100, line: 3), Parsed("Nimbus Chemicals", "2025-04-01", 100, line: 4), Parsed("Lumen Oils", "2025-04-01", 100, line: 5, isin: "INE333D01044") };
        var byInstrument = new StoredDividendKey(null, day, 100m, InstrumentId: zephyr.Id);

        var sameInstrument = Plan(rows, shares: [zephyr], manual: [byInstrument]);
        var sameTicker = Plan(rows, shares: [zephyr], manual: [new StoredDividendKey(null, day, 100m, Symbol: "QUARTZ-ENGINEERING")]);
        var sameIsin = Plan(rows, manual: [new StoredDividendKey(null, day, 100m, Isin: "ine333d01044")]);
        var saysNothing = Plan(rows, shares: [zephyr], manual: [new StoredDividendKey(null, day, 100m)]);

        sameInstrument.Rows.Select(r => r.Status).ShouldBe([DividendStatus.PossibleDuplicate, DividendStatus.NeedsMatch, DividendStatus.NeedsMatch, DividendStatus.NeedsMatch]);
        sameTicker.Rows.Select(r => r.Status).ShouldBe([DividendStatus.New, DividendStatus.PossibleDuplicate, DividendStatus.NeedsMatch, DividendStatus.NeedsMatch]);
        sameIsin.Rows.Select(r => r.Status).ShouldBe([DividendStatus.NeedsMatch, DividendStatus.NeedsMatch, DividendStatus.NeedsMatch, DividendStatus.PossibleDuplicate]);
        saysNothing.Rows.Select(r => r.Status).ShouldBe([DividendStatus.PossibleDuplicate, DividendStatus.PossibleDuplicate, DividendStatus.PossibleDuplicate, DividendStatus.PossibleDuplicate]);
    }
}
