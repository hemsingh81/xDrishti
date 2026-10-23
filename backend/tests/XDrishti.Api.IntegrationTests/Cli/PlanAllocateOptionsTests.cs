using XDrishti.Api.Cli;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd plan allocate</c> (spec P7-07): pure parsing, no database.</summary>
public sealed class PlanAllocateOptionsTests
{
    private const string Ticket = "AAA:BUY:100:98:1000:1";
    private static readonly string Account = Guid.NewGuid() + ":paper:trading:1000000:0.5:20:3:1:500000:no";

    [Fact]
    public void A_ticket_and_accounts_parse()
    {
        var (options, errors) = PlanAllocateOptions.Parse(["--ticket", Ticket, "--account", Account, "--account", Guid.NewGuid() + ":manual_live:other:?:?:?:?:?:?:?"]);

        errors.ShouldBeEmpty();
        options!.Ticket.EntryPrice.ShouldBe(100);
        options.Accounts.Count.ShouldBe(2);
        options.Accounts[0].Mode.ShouldBe(TradingMode.Paper);
        options.Accounts[1].Mode.ShouldBe(TradingMode.ManualLive);
        options.Accounts[1].IsTrading.ShouldBeFalse();
        options.Accounts[1].Equity.ShouldBeNull();
        options.Accounts[1].HoldsSymbol.ShouldBeNull();
    }

    [Theory]
    [InlineData("--ticket", "AAA:BUY:100")]
    [InlineData("--account", "not-a-guid:paper:trading:1:1:1:1:1:1:no")]
    [InlineData("--nope", "1")]
    [InlineData("--ticket", "")]
    public void A_bad_command_line_is_refused(string flag, string value)
    {
        var (options, errors) = PlanAllocateOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_bad_mode_or_role_is_refused()
    {
        var (options, _) = PlanAllocateOptions.Parse(["--ticket", Ticket, "--account", Guid.NewGuid() + ":auto:trading:1:1:1:1:1:1:no"]);

        options.ShouldBeNull();
    }

    [Fact]
    public void A_ticket_given_twice_or_no_accounts_is_refused()
    {
        PlanAllocateOptions.Parse(["--ticket", Ticket, "--ticket", Ticket, "--account", Account]).Options.ShouldBeNull();
        PlanAllocateOptions.Parse(["--ticket", Ticket]).Options.ShouldBeNull();
    }
}
