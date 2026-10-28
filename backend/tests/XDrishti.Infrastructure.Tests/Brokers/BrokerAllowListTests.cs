using XDrishti.Domain.Planning;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// The broker allow-list as the P7 phase check reads it (spec P7-18, ADR 0097): the entries in the code equal the pinned list, so adding an endpoint fails here until the pin is changed on purpose (an owner-approved item); every order, position and fund
/// probe of the phase check is refused; and every declared entry is something the guard really allows.
/// </summary>
public sealed class BrokerAllowListTests
{
    [Fact]
    public void The_entries_in_the_code_equal_the_pinned_list()
    {
        new BrokerAllowList().Entries().ShouldBe(PlanPhaseCheck.PinnedAllowList);
    }

    [Fact]
    public void Every_probe_that_names_an_order_a_position_or_a_fund_is_refused()
    {
        var allowList = new BrokerAllowList();

        PlanPhaseCheck.RefusedProbes.Where(p => allowList.Allows(p.Method, p.Url)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "https://api.dhan.co/v2/holdings")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")]
    [InlineData("GET", "https://api.dhan.co/v2/trades")]
    [InlineData("GET", "https://api.dhan.co/v2/orders")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30")]
    public void The_declared_data_endpoints_are_allowed(string method, string url)
    {
        new BrokerAllowList().Allows(method, url).ShouldBeTrue();
    }

    [Fact]
    public void The_probe_set_of_P11_01_is_in_the_phase_check_and_refused()
    {
        PlanPhaseCheck.RefusedProbes.ShouldContain(("GET", "https://api.dhan.co/v2/orders/1"));
        PlanPhaseCheck.RefusedProbes.ShouldContain(("GET", "https://api.dhan.co/v2/orders?x=1"));
        PlanPhaseCheck.RefusedProbes.ShouldContain(("POST", "https://api.dhan.co/v2/trades"));
        PlanPhaseCheck.RefusedProbes.ShouldContain(("POST", "https://api.dhan.co/v2/orders"));
        PlanPhaseCheck.RefusedProbes.ShouldNotContain(("GET", "https://api.dhan.co/v2/orders"));
    }

    [Theory]
    [InlineData("", "https://api.dhan.co/v2/holdings")]
    [InlineData("GET", "not a url")]
    [InlineData("GET", "")]
    public void A_request_that_cannot_be_read_is_refused(string method, string url)
    {
        new BrokerAllowList().Allows(method, url).ShouldBeFalse();
    }

    [Fact]
    public void The_entries_name_query_keys_and_never_a_value_or_a_secret()
    {
        BrokerEndpoints.Describe().ShouldAllBe(e => !e.Contains('=', StringComparison.Ordinal));
    }
}
