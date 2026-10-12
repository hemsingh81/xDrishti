using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The calendar rows against a real database under concurrency (ADR 0017 §1): an observed pass that withdraws a proposal works from rows it read earlier; if the owner confirms the
/// proposal meanwhile, the pass must not delete or overwrite it. Two scopes stand for the two requests; the row version (xmin) refuses the stale save.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCalendarConcurrencyTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Date = new(2026, 11, 9);

    private async Task InsertAsync(string kind, string source, string status)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.trading_days (date, kind, source, status, note, updated_at) VALUES (@date, @kind, @source, @status, 'seen', now())",
            ("date", Date), ("kind", kind), ("source", source), ("status", status));
    }

    [Fact]
    public async Task A_stale_proposal_is_not_deleted_when_the_owner_confirmed_it_after_the_pass_read_it()
    {
        await InsertAsync("holiday", "observed", "proposed");
        using var pass = Factory.Services.CreateScope();
        using var owner = Factory.Services.CreateScope();
        var passStore = pass.ServiceProvider.GetRequiredService<ITradingDayStore>();
        var passRows = await passStore.ListAsync(Ct);
        var ownersRow = (await owner.ServiceProvider.GetRequiredService<ITradingDayStore>().FindAsync(Date, Ct))!;
        ownersRow.Confirm(Factory.Clock.GetUtcNow()).ShouldBeTrue();
        (await owner.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();

        passStore.Remove(passRows.Single());
        var saved = await pass.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct);

        saved.ShouldBeFalse();
        (await ScalarAsync<string>("SELECT status FROM market.trading_days WHERE date = @date", ("date", Date))).ShouldBe("confirmed");
    }

    [Fact]
    public async Task A_withdrawal_from_rows_that_are_still_current_deletes_the_proposal()
    {
        await InsertAsync("holiday", "observed", "proposed");
        using var pass = Factory.Services.CreateScope();
        var store = pass.ServiceProvider.GetRequiredService<ITradingDayStore>();

        store.Remove((await store.ListAsync(Ct)).Single());
        var saved = await pass.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct);

        saved.ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM market.trading_days")).ShouldBe(0);
    }

    [Fact]
    public async Task A_proposal_next_to_a_confirmed_day_is_not_cleared_over_the_owners_dismissal_made_meanwhile()
    {
        await InsertAsync("holiday", "nse", "confirmed");
        using var pass = Factory.Services.CreateScope();
        using var owner = Factory.Services.CreateScope();
        var passRow = (await pass.ServiceProvider.GetRequiredService<ITradingDayStore>().ListAsync(Ct)).Single();
        var ownersRow = (await owner.ServiceProvider.GetRequiredService<ITradingDayStore>().FindAsync(Date, Ct))!;
        ownersRow.Dismiss(Factory.Clock.GetUtcNow()).ShouldBeTrue();
        (await owner.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();

        passRow.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Factory.Clock.GetUtcNow()).ShouldBeTrue();
        var saved = await pass.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct);

        saved.ShouldBeFalse();
        (await ScalarAsync<string>("SELECT status FROM market.trading_days WHERE date = @date", ("date", Date))).ShouldBe("dismissed");
        (await ScalarAsync<bool>("SELECT proposed_kind IS NULL FROM market.trading_days WHERE date = @date", ("date", Date))).ShouldBeTrue();
    }
}
