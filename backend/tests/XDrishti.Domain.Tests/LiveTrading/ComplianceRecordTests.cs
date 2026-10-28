using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-05 criteria 3 to 5: every refusal of the record, the runbook's own sources, the lock end and the kind-to-outcome table.</summary>
public sealed class ComplianceRecordTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    private static ComplianceDraft Sebi(Action<Dictionary<string, object?>>? change = null) => Build(ComplianceKinds.SebiRecheck, "rechecked_no_change", change);

    private static ComplianceDraft Build(string kind, string outcome, Action<Dictionary<string, object?>>? change)
    {
        var v = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["outcome"] = outcome,
            ["decided"] = Today,
            ["summary"] = "Read the circulars; no change to the order-rate threshold.",
            ["sources"] = new[] { "https://dhanhq.co/docs/v2/authentication/", "https://support.fyers.in/portal/en/kb/articles/what-are-the-new-sebi-rules-for-retail-algo-trading-from-april-01-2026", "NSE/INVG/73992", "SEBI/HO/MIRSD/2025/0000013" },
            ["state"] = null,
            ["isp"] = null,
            ["cost"] = null,
            ["version"] = null,
            ["whitelisted"] = null,
            ["next"] = Today.AddDays(90),
        };
        change?.Invoke(v);
        return new ComplianceDraft((string?)v["kind"], (string?)v["outcome"], (DateOnly)v["decided"]!, (string?)v["summary"], (string[]?)v["sources"], (string?)v["state"], (string?)v["isp"], (decimal?)v["cost"], (string?)v["version"], (DateOnly?)v["whitelisted"], (DateOnly)v["next"]!);
    }

    private static ComplianceDraft Static(string state, DateOnly? whitelisted = null, Action<Dictionary<string, object?>>? change = null) =>
        Build(ComplianceKinds.StaticIp, state == "whitelisted" ? "whitelisted" : "requested", v =>
        {
            v["state"] = state;
            v["isp"] = "ACT Broadband";
            v["cost"] = 350m;
            v["version"] = "v4";
            v["whitelisted"] = whitelisted;
            change?.Invoke(v);
        });

    private static string Refused(ComplianceDraft draft)
    {
        var result = ComplianceRecord.Create(draft, Today);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("live.invalid");
        return result.Error.Message;
    }

    [Fact]
    public void The_runbooks_own_sources_and_the_decided_date_of_the_day_pass()
    {
        var record = ComplianceRecord.Create(Sebi(), Today).Value;

        record.Sources.Count.ShouldBe(4);
        record.DecidedOn.ShouldBe(Today);
        record.Summary.ShouldStartWith("Read the circulars");
    }

    [Fact]
    public void Each_kind_has_its_own_outcomes_and_another_kinds_outcome_is_refused()
    {
        ComplianceKinds.OutcomesOf("static_ip").Count.ShouldBe(6);
        ComplianceKinds.OutcomesOf("sebi_recheck").ShouldBe(["rechecked_no_change", "rechecked_changed", "blocked"]);
        ComplianceKinds.OutcomesOf("dhan_ip_read_policy").ShouldBe(["reads_not_ip_restricted", "reads_ip_restricted", "unanswered"]);
        ComplianceKinds.OutcomesOf("other").ShouldBeEmpty();
        Refused(Sebi(v => v["outcome"] = "whitelisted")).ShouldStartWith("outcome ");
        Refused(Sebi(v => v["kind"] = "nope")).ShouldStartWith("kind ");
        Refused(Sebi(v => v["kind"] = null)).ShouldStartWith("kind ");
        Refused(Sebi(v => v["outcome"] = null)).ShouldStartWith("outcome ");
    }

    [Theory]
    [InlineData("192.168.1.10", "summary")]
    [InlineData("2001:db8::1", "summary")]
    [InlineData("1100000001", "summary")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789ABCD", "summary")]
    public void An_identifier_in_the_summary_is_refused_naming_the_field(string text, string field) =>
        Refused(Sebi(v => v["summary"] = "noted " + text)).ShouldStartWith(field + " ");

    [Fact]
    public void An_identifier_in_the_provider_name_is_refused_naming_isp() =>
        Refused(Static("requested", change: v => v["isp"] = "ACT 192.168.1.10")).ShouldStartWith("isp ");

    [Fact]
    public void Plain_words_in_the_summary_pass_and_an_empty_or_long_one_does_not()
    {
        ComplianceRecord.Create(Sebi(v => v["summary"] = "7 days, Rs 350"), Today).IsSuccess.ShouldBeTrue();
        Refused(Sebi(v => v["summary"] = "   ")).ShouldStartWith("summary ");
        Refused(Sebi(v => v["summary"] = string.Join(' ', Enumerable.Repeat("word", 201)))).ShouldStartWith("summary ");
        ComplianceRecord.Create(Sebi(v => v["summary"] = string.Join(' ', Enumerable.Repeat("word", 200))), Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_compact_date_in_a_date_field_is_a_date_not_a_client_id()
    {
        // 20261024 is not text: the dates are typed, so the screens never see them.
        ComplianceRecord.Create(Sebi(v => v["decided"] = new DateOnly(2026, 10, 24)), Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_decided_date_cannot_be_in_the_future_and_the_review_date_must_follow_it_within_400_days()
    {
        Refused(Sebi(v => v["decided"] = Today.AddDays(1))).ShouldStartWith("decidedOn ");
        Refused(Sebi(v => v["next"] = Today)).ShouldStartWith("nextReviewOn ");
        Refused(Sebi(v => v["next"] = Today.AddDays(-1))).ShouldStartWith("nextReviewOn ");
        Refused(Sebi(v => v["next"] = Today.AddDays(401))).ShouldStartWith("nextReviewOn ");
        ComplianceRecord.Create(Sebi(v => v["next"] = Today.AddDays(400)), Today).IsSuccess.ShouldBeTrue();
        ComplianceRecord.Create(Sebi(v => v["next"] = Today.AddDays(1)), Today).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://dhanhq.co/docs")]
    [InlineData("https://192.168.1.1/x")]
    [InlineData("https://user@dhanhq.co/x")]
    [InlineData("ftp://dhanhq.co/x")]
    [InlineData("NSE/x")]
    [InlineData("XYZ/INVG/73992")]
    [InlineData("not a source")]
    [InlineData("")]
    public void A_source_that_is_neither_an_https_url_nor_a_circular_id_is_refused(string source) =>
        Refused(Sebi(v => v["sources"] = new[] { source })).ShouldStartWith("sources ");

    [Fact]
    public void More_than_ten_sources_or_an_item_over_300_characters_is_refused()
    {
        Refused(Sebi(v => v["sources"] = Enumerable.Repeat("NSE/INVG/73992", 11).ToArray())).ShouldStartWith("sources ");
        ComplianceRecord.Create(Sebi(v => v["sources"] = Enumerable.Repeat("NSE/INVG/73992", 10).ToArray()), Today).IsSuccess.ShouldBeTrue();
        Refused(Sebi(v => v["sources"] = new[] { "https://example.com/" + new string('a', 290) })).ShouldStartWith("sources ");
        ComplianceRecord.Create(Sebi(v => v["sources"] = Array.Empty<string>()), Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_whitelisted_record_needs_its_date_and_the_server_derives_the_lock_end_seven_days_later()
    {
        Refused(Static("whitelisted")).ShouldStartWith("whitelistedAt ");

        var record = ComplianceRecord.Create(Static("whitelisted", new DateOnly(2026, 10, 20)), Today).Value;

        record.WhitelistedAt.ShouldBe(new DateOnly(2026, 10, 20));
        record.LockEndsOn.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void Another_state_refuses_the_whitelisting_date_and_has_no_lock_end()
    {
        Refused(Static("requested", new DateOnly(2026, 10, 20))).ShouldStartWith("whitelistedAt ");
        var record = ComplianceRecord.Create(Static("active"), Today).Value;
        (record.WhitelistedAt, record.LockEndsOn).ShouldBe((null, null));
    }

    [Fact]
    public void The_whitelisting_date_cannot_follow_the_decision()
    {
        Refused(Static("whitelisted", Today.AddDays(1))).ShouldStartWith("whitelistedAt ");
    }

    [Fact]
    public void The_static_ip_fields_are_for_the_static_ip_kind_and_that_kind_needs_a_state()
    {
        Refused(Sebi(v => v["isp"] = "ACT")).ShouldStartWith("staticIpState ");
        Refused(Sebi(v => v["state"] = "none")).ShouldStartWith("staticIpState ");
        Refused(Sebi(v => v["cost"] = 1m)).ShouldStartWith("staticIpState ");
        Refused(Build(ComplianceKinds.StaticIp, "requested", null)).ShouldStartWith("staticIpState ");
        Refused(Static("bogus")).ShouldStartWith("staticIpState ");
        Refused(Static("requested", change: v => v["version"] = "v5")).ShouldStartWith("ipVersion ");
        Refused(Static("requested", change: v => v["cost"] = -1m)).ShouldStartWith("monthlyCost ");
        Refused(Static("requested", change: v => v["isp"] = new string('a', 81))).ShouldStartWith("isp ");
    }

    [Fact]
    public void A_static_ip_record_keeps_the_provider_cost_and_version()
    {
        var record = ComplianceRecord.Create(Static("requested"), Today).Value;

        (record.Isp, record.MonthlyCost, record.IpVersion, record.StaticIpState).ShouldBe(("ACT Broadband", 350m, "v4", "requested"));
    }
}
