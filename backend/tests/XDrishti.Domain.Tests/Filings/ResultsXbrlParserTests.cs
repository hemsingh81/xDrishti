using System.Globalization;
using System.Text;
using XDrishti.Domain.Filings;
using static XDrishti.Domain.Tests.Filings.XbrlSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>Spec P15-03 criterion 2 (synthetic fixtures), 7 (unavailable is explicit), 10 (determinism) and the pure rules of the parser.</summary>
public sealed class ResultsXbrlParserTests
{
    private static ParsedFact Fact(ParsedResults r, string field, string span = "q", string basis = "consolidated") => r.Facts.Single(f => f.Field == field && f.Span == span && f.Basis == basis);

    [Fact]
    public void The_parser_version_is_labelled_synthetic_until_the_real_filings_pass()
    {
        ResultsParser.Version.ShouldBe("xbrl-1-synthetic");
    }

    [Fact]
    public void A_quarter_gives_the_seven_numbers_and_the_audited_flag_exactly_as_filed()
    {
        var result = Parse(QuarterDocument());

        result.Outcome.ShouldBe("parsed");
        result.Facts.Count.ShouldBe(8);
        Fact(result, "revenue").Value.ShouldBe(1000.50m);
        Fact(result, "total_income").Value.ShouldBe(1100.75m);
        Fact(result, "pbt").Value.ShouldBe(200m);
        Fact(result, "pat").Value.ShouldBe(150m);
        Fact(result, "eps_basic").Value!.Value.ToString(CultureInfo.InvariantCulture).ShouldBe("2.50", "the filed decimals are kept");
        Fact(result, "eps_diluted").Value.ShouldBe(2.45m);
        Fact(result, "exceptional_items").Value.ShouldBe(-5m);
        Fact(result, "audited").Value.ShouldBe(0m);
        result.Facts.ShouldAllBe(f => f.Span == "q" && f.Basis == "consolidated");
        (Fact(result, "revenue").Unit, Fact(result, "eps_basic").Unit, Fact(result, "audited").Unit).ShouldBe(("inr", "inr_per_share", "flag"));
    }

    [Fact]
    public void The_same_file_gives_identical_facts_every_time()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + FullSet("c-q") + Duration("c-y", "2026-04-01", "2026-06-30", "StandaloneMember") + FullSet("c-y"));

        var first = string.Join("|", Parse(xml).Facts.Select(f => $"{f.Field}/{f.Span}/{f.Basis}/{f.Value}/{f.Reason}"));
        var second = string.Join("|", Parse(xml).Facts.Select(f => $"{f.Field}/{f.Span}/{f.Basis}/{f.Value}/{f.Reason}"));

        second.ShouldBe(first);
    }

    [Theory]
    [InlineData("2026-04-01", "2026-06-30", "2026-06-30", "q")]
    [InlineData("2026-04-01", "2026-09-30", "2026-09-30", "ytd")]
    [InlineData("2026-04-01", "2026-12-31", "2026-12-31", "ytd")]
    [InlineData("2026-01-01", "2026-03-31", "2026-03-31", "q")]
    [InlineData("2025-04-01", "2026-03-31", "2026-03-31", "fy")]
    public void The_span_follows_the_context_period(string start, string end, string periodEnd, string expected)
    {
        SpanRule.Classify(start, end, null, DateOnly.Parse(periodEnd, CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-04-01", "2026-06-30", "2026-07-31")]   // ends on another date
    [InlineData("2026-05-01", "2026-06-30", "2026-06-30")]   // 61 days
    [InlineData("2026-02-01", "2026-09-30", "2026-09-30")]   // long, not from 1 April
    [InlineData("2025-04-01", "2026-08-31", "2026-08-31")]   // above 375 days is not a full year, still from 1 April: ytd
    [InlineData("2026-06-30", "2026-04-01", "2026-04-01")]   // reversed
    [InlineData("not-a-date", "2026-06-30", "2026-06-30")]
    public void Any_other_duration_has_no_span_unless_it_is_year_to_date(string start, string end, string periodEnd)
    {
        var span = SpanRule.Classify(start, end, null, DateOnly.Parse(periodEnd, CultureInfo.InvariantCulture));

        if (start == "2025-04-01")
        {
            span.ShouldBe("ytd");
        }
        else
        {
            span.ShouldBeNull();
        }
    }

    [Fact]
    public void An_instant_has_no_span()
    {
        SpanRule.Classify(null, null, "2026-06-30", Q1).ShouldBeNull();
    }

    [Fact]
    public void Quarter_half_year_nine_months_and_full_year_are_four_spans_of_one_file()
    {
        var annual = new DateOnly(2027, 3, 31);
        var xml = Document(
            Duration("q", "2027-01-01", "2027-03-31") + FullSet("q", audited: "Audited") + Duration("h", "2026-10-01", "2027-03-31") + Duration("y", "2026-04-01", "2027-03-31") + FullSet("y", revenue: "9000", audited: "Audited"));

        var result = Parse(xml, annual);

        result.Facts.Select(f => f.Span).Distinct().ShouldBe(["q", "fy"]);
        Fact(result, "revenue", "fy").Value.ShouldBe(9000m);
        Fact(result, "audited", "fy").Value.ShouldBe(1m);
    }

    [Fact]
    public void Year_to_date_spans_of_six_and_nine_months_are_read_when_they_end_on_the_period_end()
    {
        var six = Parse(Document(Duration("h", "2026-04-01", "2026-09-30") + FullSet("h", revenue: "2100")), new DateOnly(2026, 9, 30));
        var nine = Parse(Document(Duration("n", "2026-04-01", "2026-12-31") + FullSet("n", revenue: "3200")), new DateOnly(2026, 12, 31));

        Fact(six, "revenue", "ytd").Value.ShouldBe(2100m);
        Fact(nine, "revenue", "ytd").Value.ShouldBe(3200m);
    }

    [Fact]
    public void Both_bases_in_one_file_come_apart_by_the_basis_axis()
    {
        var xml = Document(Duration("cc", "2026-04-01", "2026-06-30", "ConsolidatedMember") + FullSet("cc", revenue: "5000") + Duration("cs", "2026-04-01", "2026-06-30", "StandaloneMember") + FullSet("cs", revenue: "3000"), basis: null);

        var result = Parse(xml);

        Fact(result, "revenue", basis: "consolidated").Value.ShouldBe(5000m);
        Fact(result, "revenue", basis: "standalone").Value.ShouldBe(3000m);
        result.Facts.Count.ShouldBe(16);
    }

    [Theory]
    [InlineData("Consolidated", "consolidated")]
    [InlineData("standalone", "standalone")]
    public void A_file_with_one_basis_takes_it_from_the_marker(string marker, string expected)
    {
        Parse(QuarterDocument(marker)).Facts.ShouldAllBe(f => f.Basis == expected);
    }

    [Fact]
    public void A_file_with_no_readable_basis_stores_no_fact_and_says_why()
    {
        var none = Parse(QuarterDocument(basis: null));
        var odd = Parse(QuarterDocument(basis: "Partly"));

        (none.Outcome, none.Reason, none.Facts.Count).ShouldBe(("no_facts", "basis_unknown", 0));
        (odd.Outcome, odd.Reason).ShouldBe(("no_facts", "basis_unknown"));
        none.Flags["basis_unknown"].ShouldBe(8);
    }

    [Fact]
    public void A_missing_eps_is_unavailable_not_in_file_and_never_zero()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + Num("RevenueFromOperations", "c-q", Inr, "100") + Num("ProfitBeforeTax", "c-q", Inr, "10"));

        var result = Parse(xml);

        var eps = Fact(result, "eps_basic");
        (eps.Value, eps.Reason, eps.IsValue).ShouldBe((null, "not_in_file", false));
        Fact(result, "pat").Reason.ShouldBe("not_in_file");
        Fact(result, "revenue").Value.ShouldBe(100m);
    }

    [Fact]
    public void Two_different_values_for_one_field_are_conflicting_and_two_equal_ones_are_one_value()
    {
        var xml = Document(Duration("a", "2026-04-01", "2026-06-30") + Duration("b", "2026-04-01", "2026-06-30") + Num("RevenueFromOperations", "a", Inr, "100") + Num("RevenueFromOperations", "b", Inr, "101") + Num("Income", "a", Inr, "7") + Num("Income", "b", Inr, "7.0"));

        var result = Parse(xml);

        Fact(result, "revenue").Reason.ShouldBe("conflicting_facts");
        Fact(result, "total_income").Value.ShouldBe(7m);
    }

    [Fact]
    public void A_fact_with_a_segment_dimension_is_skipped_and_counted()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + Duration("c-seg", "2026-04-01", "2026-06-30", segmentDimension: "SegmentsAxis") + Num("RevenueFromOperations", "c-q", Inr, "100") + Num("RevenueFromOperations", "c-seg", Inr, "60"));

        var result = Parse(xml);

        Fact(result, "revenue").Value.ShouldBe(100m);
        result.Flags["dimension_skipped"].ShouldBe(1);
    }

    [Fact]
    public void A_unit_that_is_not_inr_is_unavailable_unit_unknown()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + Num("RevenueFromOperations", "c-q", Usd, "100") + Num("ProfitBeforeTax", "c-q", PerShare, "10") + Num("Income", "c-q", "u-missing", "5"));

        var result = Parse(xml);

        Fact(result, "revenue").Reason.ShouldBe("unit_unknown");
        Fact(result, "pbt").Reason.ShouldBe("unit_unknown");
        Fact(result, "total_income").Reason.ShouldBe("unit_unknown");
    }

    [Fact]
    public void A_context_ending_on_another_date_is_skipped_and_counted_and_leaves_no_facts()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-07-31") + FullSet("c-q"));

        var result = Parse(xml);

        (result.Outcome, result.Reason, result.Facts.Count).ShouldBe(("no_facts", "nothing_mapped", 0));
        result.Flags["unknown_span"].ShouldBe(8);
    }

    [Fact]
    public void A_flow_item_in_an_instant_context_is_skipped_as_unknown_span()
    {
        var result = Parse(Document(Instant("i", "2026-06-30") + Duration("c-q", "2026-04-01", "2026-06-30") + Num("RevenueFromOperations", "i", Inr, "1") + Num("Income", "c-q", Inr, "2")));

        Fact(result, "revenue").Reason.ShouldBe("not_in_file");
        result.Flags["unknown_span"].ShouldBe(1);
    }

    [Fact]
    public void A_file_with_a_root_and_nothing_mapped_is_no_facts()
    {
        var result = Parse(Document(string.Empty));

        (result.Outcome, result.Reason, result.Facts.Count).ShouldBe(("no_facts", "nothing_mapped", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"><b></xbrli:xbrl>")]
    public void Text_that_is_not_xml_is_rejected(string xml)
    {
        var result = Parse(xml);

        (result.Outcome, result.Reason).ShouldBe(("rejected", "not_xml"));
    }

    [Fact]
    public void An_xml_document_that_is_not_an_xbrl_instance_is_not_xbrl()
    {
        Parse("<html><body>hello</body></html>").Outcome.ShouldBe("not_xbrl");
    }

    [Fact]
    public void A_pat_without_the_owners_line_falls_back_to_the_profit_for_the_period_and_counts_it()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + Num("ProfitLossForPeriod", "c-q", Inr, "140"));

        var result = Parse(xml);

        Fact(result, "pat").Value.ShouldBe(140m);
        result.Flags["pat_from_profit_for_period"].ShouldBe(1);
    }

    [Fact]
    public void The_owners_profit_wins_over_the_profit_for_the_period()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + Num("ProfitLossForPeriod", "c-q", Inr, "140") + Num("ProfitOrLossAttributableToOwnersOfParent", "c-q", Inr, "130"));

        var result = Parse(xml);

        Fact(result, "pat").Value.ShouldBe(130m);
        result.Flags.ContainsKey("pat_from_profit_for_period").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Lakhs")]
    [InlineData("Crores")]
    [InlineData("Thousands")]
    public void A_stated_rounding_level_makes_the_money_unavailable_scale_unknown_and_never_scales_a_number(string rounding)
    {
        var result = Parse(Document(Duration("c-q", "2026-04-01", "2026-06-30") + FullSet("c-q"), rounding: rounding));

        Fact(result, "revenue").Reason.ShouldBe("scale_unknown");
        Fact(result, "eps_basic").Value.ShouldBe(2.50m);
    }

    [Fact]
    public void A_value_that_is_not_a_number_a_nil_fact_and_an_unreadable_audited_word_are_handled_without_a_guess()
    {
        var xml = Document(
            Duration("c-q", "2026-04-01", "2026-06-30") + Num("RevenueFromOperations", "c-q", Inr, "12,000") + "<in:Income contextRef=\"c-q\" unitRef=\"u-inr\" xsi:nil=\"true\" />" + Num("ProfitBeforeTax", "c-q", Inr, "1") + Word("WhetherResultsAreAuditedOrUnaudited", "c-q", "reviewed"));

        var result = Parse(xml);

        Fact(result, "revenue").Reason.ShouldBe("bad_value");
        Fact(result, "total_income").Reason.ShouldBe("not_in_file");
        Fact(result, "audited").Reason.ShouldBe("bad_value");
        result.Flags["nil_skipped"].ShouldBe(1);
    }

    [Fact]
    public void The_banking_format_is_recognised_and_every_field_is_format_not_supported()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + FullSet("c-q"), rootNamespaces: " xmlns:bk=\"http://synthetic.example/taxonomy/in-bank-results\"");

        var result = Parse(xml);

        (result.Outcome, result.Reason, result.Facts.Count).ShouldBe(("no_facts", "format_not_supported", 0));
    }

    [Fact]
    public void Sanity_flags_are_counted_and_no_number_is_corrected()
    {
        var xml = Document(Duration("c-q", "2026-04-01", "2026-06-30") + FullSet("c-q", revenue: "900", income: "800", pat: "100", basic: "-2", diluted: "-3"));

        var result = Parse(xml);

        result.Flags["revenue_gt_total_income"].ShouldBe(1);
        result.Flags["eps_sign_differs_from_pat"].ShouldBe(1);
        Fact(result, "revenue").Value.ShouldBe(900m);
        Fact(result, "total_income").Value.ShouldBe(800m);
    }

    [Fact]
    public void Basic_eps_below_diluted_eps_for_a_profit_is_flagged()
    {
        var result = Parse(Document(Duration("c-q", "2026-04-01", "2026-06-30") + FullSet("c-q", basic: "1.00", diluted: "1.50")));

        result.Flags["eps_basic_below_diluted"].ShouldBe(1);
    }

    [Fact]
    public void A_schema_location_attribute_is_refused_as_an_external_reference()
    {
        var xml = Document(string.Empty).Replace("<link:schemaRef", "<in:Thing xsi:schemaLocation=\"http://127.0.0.1:1/x.xsd\" /><link:schemaRef", StringComparison.Ordinal);

        Parse(xml).Reason.ShouldBe("external_reference");
    }

    [Fact]
    public void A_document_type_declaration_is_never_read()
    {
        var result = Parse("<!DOCTYPE x [<!ENTITY a \"b\">]><xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">&a;</xbrli:xbrl>");

        (result.Outcome, result.Facts.Count).ShouldBe(("rejected", 0));
    }

    [Fact]
    public void Sixty_four_levels_are_read_and_sixty_five_are_rejected_too_deep()
    {
        static string Nested(int levels) => "<xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">" + string.Concat(Enumerable.Repeat("<a>", levels - 1)) + string.Concat(Enumerable.Repeat("</a>", levels - 1)) + "</xbrli:xbrl>";

        Parse(Nested(64)).Outcome.ShouldBe("no_facts");
        Parse(Nested(65)).Reason.ShouldBe("too_deep");
    }

    [Fact]
    public void More_than_two_hundred_thousand_elements_are_rejected()
    {
        var body = new StringBuilder("<xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">");
        for (var i = 0; i < ResultsParser.MaxElements; i++)
        {
            body.Append("<a/>");
        }

        body.Append("</xbrli:xbrl>");

        Parse(body.ToString()).Reason.ShouldBe("too_many_elements");
    }

    [Fact]
    public void The_domain_parser_never_creates_a_reader()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Domain", "Filings", "ResultsXbrlParser.cs"));

        source.ShouldNotContain("XmlReader.Create");
        source.ShouldNotContain("XmlDocument");
        source.ShouldNotContain("XmlResolver");
    }
}
