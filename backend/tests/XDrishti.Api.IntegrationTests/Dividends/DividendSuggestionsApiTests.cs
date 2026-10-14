using System.Net;
using XDrishti.Api.IntegrationTests.BrokerHistory;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Portfolio.Dividends;

namespace XDrishti.Api.IntegrationTests.Dividends;

/// <summary>
/// Suggested dividends over HTTP (P2-14, ADR 0026): a ledger entry of class dividend that Dhan reported is offered, and nothing is created until the owner accepts it; accepting and dismissing are final. The ledger is read by the real sync from a fake Dhan
/// (<see cref="BrokerHistoryTestBase.DhanHasALedger"/> has one dividend credit of 1,500 on Wed 7 Oct 2026).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendSuggestionsApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private async Task<(Held Held, DividendSuggestionPage Suggestions)> LedgerReadAsync()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();
        await SyncAndWaitAsync(held.Account.Id);
        return (held, await Client.SuggestionsAsync(held.Account.Id, Ct));
    }

    [Fact]
    public async Task A_dividend_entry_of_the_ledger_is_suggested_and_nothing_is_created_by_reading_it()
    {
        var (held, suggestions) = await LedgerReadAsync();

        var entry = suggestions.Items.ShouldHaveSingleItem();
        (entry.Class, entry.Amount, entry.Date, entry.VoucherNo, entry.Narration).ShouldBe(("dividend", 1500m, Oct(7), "V3", "DIVIDEND CREDIT EXMPL FINAL DIV"));
        suggestions.Total.ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
        (await Client.AccountDividendsAsync(held.Account.Id, "", Ct)).Total.ShouldBe(0);
        (await RowAsync()).Income.ShouldBeNull();
    }

    [Fact]
    public async Task Accepting_creates_the_dividend_for_the_chosen_holding_and_the_entry_is_not_offered_again()
    {
        var (held, suggestions) = await LedgerReadAsync();
        var entryId = suggestions.Items[0].Id;

        using var accepted = await Client.AcceptRawAsync(held.Account.Id, entryId, new { holdingId = held.HoldingId, note = "from the ledger" }, Ct);
        var dividend = await accepted.ReadAsync<DividendView>(Ct);

        (dividend.AccountId, dividend.HoldingId, dividend.Symbol, dividend.PayDate, dividend.Amount, dividend.Note, dividend.Source).ShouldBe((held.Account.Id, held.HoldingId, "EXMPL", Oct(7), 1500m, "from the ledger", "manual"));
        (await Client.SuggestionsAsync(held.Account.Id, Ct)).Items.ShouldBeEmpty();
        (await RowAsync()).Income!.Dividends.ShouldBe(1500m);
        (await CountAsync($"SELECT count(*) FROM portfolio.dividend_suggestions WHERE id = '{entryId}' AND state = 'accepted' AND dividend_id = '{dividend.Id}'")).ShouldBe(1);

        using var again = await Client.AcceptRawAsync(held.Account.Id, entryId, null, Ct);
        using var dismiss = await Client.DismissRawAsync(held.Account.Id, entryId, Ct);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        dismiss.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.conflict");
        (await Client.AccountDividendsAsync(held.Account.Id, "", Ct)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Accepting_without_a_holding_gives_an_unallocated_dividend_and_voiding_it_does_not_bring_the_suggestion_back()
    {
        var (held, suggestions) = await LedgerReadAsync();

        using var accepted = await Client.AcceptRawAsync(held.Account.Id, suggestions.Items[0].Id, null, Ct);
        var dividend = await accepted.ReadAsync<DividendView>(Ct);
        await Client.VoidDividendAsync(dividend.Id, Ct);

        dividend.HoldingId.ShouldBeNull();
        (await Client.SuggestionsAsync(held.Account.Id, Ct)).Items.ShouldBeEmpty();
        var income = (await SummaryAsync()).Income.ShouldNotBeNull(); // only the ledger's DP charges are left
        (income.Dividends, income.Charges).ShouldBe((0m, 15.93m));
    }

    [Fact]
    public async Task Dismissing_hides_the_entry_creates_nothing_and_cannot_be_taken_back()
    {
        var (held, suggestions) = await LedgerReadAsync();
        var entryId = suggestions.Items[0].Id;

        using var dismissed = await Client.DismissRawAsync(held.Account.Id, entryId, Ct);
        var decision = await dismissed.ReadAsync<SuggestionDecisionView>(Ct);
        using var accept = await Client.AcceptRawAsync(held.Account.Id, entryId, null, Ct);

        (decision.LedgerEntryId, decision.State, decision.DividendId).ShouldBe((entryId, "dismissed", null));
        (await Client.SuggestionsAsync(held.Account.Id, Ct)).Items.ShouldBeEmpty();
        accept.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
    }

    [Fact]
    public async Task Only_a_dividend_entry_of_the_account_can_be_decided()
    {
        var (held, _) = await LedgerReadAsync();
        var charge = await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE class = 'charges'");
        var chargeId = await FirstIdAsync("charges");
        var other = await UnconnectedAccountAsync("Example Two", "1000000002");

        using var notDividend = await Client.AcceptRawAsync(held.Account.Id, chargeId, null, Ct);
        using var otherAccount = await Client.DismissRawAsync(other.Id, await FirstIdAsync("dividend"), Ct);
        using var unknown = await Client.DismissRawAsync(held.Account.Id, Guid.NewGuid(), Ct);
        using var noAccount = await Client.DismissRawAsync(Guid.NewGuid(), chargeId, Ct);

        charge.ShouldBe(1);
        notDividend.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        otherAccount.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noAccount.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountAsync("SELECT count(*) FROM portfolio.dividend_suggestions")).ShouldBe(0);
    }

    [Fact]
    public async Task The_holding_chosen_must_belong_to_the_account()
    {
        var (held, suggestions) = await LedgerReadAsync();

        using var response = await Client.AcceptRawAsync(held.Account.Id, suggestions.Items[0].Id, new { holdingId = Guid.NewGuid() }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["holdingId"]);
        (await Client.SuggestionsAsync(held.Account.Id, Ct)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task The_audit_trail_names_the_ids_and_never_the_amount()
    {
        var (held, suggestions) = await LedgerReadAsync();
        var entryId = suggestions.Items[0].Id;
        using var accepted = await Client.AcceptRawAsync(held.Account.Id, entryId, null, Ct);
        var dividend = await accepted.ReadAsync<DividendView>(Ct);

        var details = await DetailsAsync("DividendSuggestionAccepted");

        details.ShouldBe([$"Suggested dividend {entryId} accepted as dividend {dividend.Id}"]);
        details.ShouldAllBe(d => !d.Contains("1500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_ledgers_coverage_date_is_the_earliest_entry_stored_whatever_the_filters()
    {
        var (held, _) = await LedgerReadAsync();

        var filtered = await Client.LedgerAsync(held.Account.Id, "?class=dividend&from=2026-10-06", Ct);

        filtered.Items.Count.ShouldBe(1);
        filtered.CoverageFrom.ShouldBe(Oct(5));
    }

    private async Task<Guid> FirstIdAsync(string entryClass)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand("SELECT id FROM portfolio.ledger_entries WHERE class = @class ORDER BY entry_date LIMIT 1", connection);
        command.Parameters.AddWithValue("class", entryClass);
        return (Guid)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<List<string>> DetailsAsync(string eventName)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand("SELECT detail FROM accounts.account_events WHERE event = @event", connection);
        command.Parameters.AddWithValue("event", eventName);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var details = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            details.Add(reader.GetString(0));
        }

        return details;
    }

    private async Task<XDrishti.Application.Portfolio.HoldingsSummary> SummaryAsync() => await Client.SummaryAsync(string.Empty, Ct);
}
