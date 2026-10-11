using XDrishti.Application.Accounts;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>The automatic refresh fields of PUT /api/accounts/{id}/portfolio and of account creation.</summary>
public sealed class PortfolioRefreshSettingsTests
{
    private readonly AccountsFixture _fx = new();

    private async Task<(Application.Accounts.AccountDetail? Detail, XDrishti.Domain.Common.Error? Error)> UpdateAsync(BrokerAccount account, PortfolioInput input)
    {
        var result = await _fx.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, input, AccountsFixture.Actor), CancellationToken.None);
        return result.IsSuccess ? (result.Value.Account, null) : (null, result.Error);
    }

    [Fact]
    public async Task A_new_account_has_the_refresh_off_with_the_default_time_and_first_import_date_and_no_holdings()
    {
        var account = await _fx.CreatedAsync();
        var detail = (await _fx.GetAccount().HandleAsync(new GetAccountQuery(account.Id), CancellationToken.None)).Value;

        (detail.Portfolio.RefreshMode, detail.Portfolio.RefreshDailyTimeIst, detail.Portfolio.FirstImportDate, detail.Portfolio.HasHoldings)
            .ShouldBe(("off", "16:00", new DateOnly(2025, 1, 1), false));
    }

    [Fact]
    public async Task The_interval_mode_is_no_longer_accepted()
    {
        var account = await _fx.CreatedAsync();

        var (_, error) = await UpdateAsync(account, new PortfolioInput(null, null, "interval"));

        error.ShouldNotBeNull().Fields!.Select(f => f.Field).ShouldBe(["refreshMode"]);
        account.Portfolio.Refresh.ShouldBe(PortfolioRefresh.Default);
    }

    [Fact]
    public async Task The_daily_mode_with_a_time_is_stored_and_audited()
    {
        var account = await _fx.CreatedAsync();

        var (detail, _) = await UpdateAsync(account, new PortfolioInput(null, null, "daily", "09:05"));

        (detail!.Portfolio.RefreshMode, detail.Portfolio.RefreshDailyTimeIst).ShouldBe(("daily", "09:05"));
        _fx.Data.Events.Single(e => e.Event == AccountEventType.PortfolioRefreshSettingsChanged).Detail.ShouldBe("Automatic holdings refresh: daily at 09:05 IST");
    }

    [Fact]
    public async Task Fields_that_are_left_out_keep_the_current_refresh_and_an_unchanged_refresh_is_not_audited()
    {
        var account = await _fx.CreatedAsync();
        await UpdateAsync(account, new PortfolioInput(null, null, "daily", "18:00"));
        _fx.Data.Events.RemoveAll(e => e.Event == AccountEventType.PortfolioRefreshSettingsChanged);

        var (detail, _) = await UpdateAsync(account, new PortfolioInput(null, false));
        var (same, _) = await UpdateAsync(account, new PortfolioInput(null, false, "daily", "18:00"));

        (detail!.Portfolio.RefreshMode, detail.Portfolio.RefreshDailyTimeIst, detail.Portfolio.IncludeInTotals).ShouldBe(("daily", "18:00", false));
        same!.Portfolio.RefreshMode.ShouldBe("daily");
        _fx.Data.Events.Where(e => e.Event == AccountEventType.PortfolioRefreshSettingsChanged).ShouldBeEmpty();
    }

    [Fact]
    public async Task Switching_the_refresh_off_is_audited_and_keeps_the_time()
    {
        var account = await _fx.CreatedAsync();
        await UpdateAsync(account, new PortfolioInput(null, null, "daily", "18:00"));

        var (detail, _) = await UpdateAsync(account, new PortfolioInput(null, null, "off"));

        (detail!.Portfolio.RefreshMode, detail.Portfolio.RefreshDailyTimeIst).ShouldBe(("off", "18:00"));
        _fx.Data.Events.Last(e => e.Event == AccountEventType.PortfolioRefreshSettingsChanged).Detail.ShouldBe("Automatic holdings refresh: off");
    }

    [Theory]
    [InlineData("hourly", null, "refreshMode")]
    [InlineData(null, "4pm", "refreshDailyTimeIst")]
    [InlineData(null, "24:00", "refreshDailyTimeIst")]
    [InlineData(null, "9:5", "refreshDailyTimeIst")]
    [InlineData(null, "16:00:30", "refreshDailyTimeIst")]
    [InlineData("interval", null, "refreshMode")]
    [InlineData("weekly", "99:99", "refreshMode,refreshDailyTimeIst")]
    public async Task Invalid_values_are_rejected_with_their_field_names_and_nothing_changes(string? mode, string? time, string fields)
    {
        var account = await _fx.CreatedAsync();
        var expected = fields.Split(',');
        var (_, error) = await UpdateAsync(account, new PortfolioInput(null, null, mode, time));

        error.ShouldNotBeNull().Code.ShouldBe(AccountErrors.InvalidCode);
        error.Fields!.Select(f => f.Field).Order().ShouldBe(expected.Order());
        account.Portfolio.Refresh.ShouldBe(PortfolioRefresh.Default);
    }

    [Fact]
    public async Task An_account_can_be_created_with_the_refresh_switched_on()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(portfolio: new PortfolioInput(null, null, "daily", "17:15", new DateOnly(2024, 6, 1))));

        account.Portfolio.Refresh.ShouldBe(new PortfolioRefresh(PortfolioRefreshMode.Daily, new TimeOnly(17, 15)));
        account.Portfolio.FirstImportDate.ShouldBe(new DateOnly(2024, 6, 1));
    }

    [Fact]
    public async Task Invalid_refresh_values_at_creation_are_prefixed_with_portfolio()
    {
        var result = await _fx.Create().HandleAsync(new CreateAccountCommand(Inputs.Account(portfolio: new PortfolioInput(null, null, "weekly", "17:15")), AccountsFixture.Actor), CancellationToken.None);

        result.Error.Fields!.Select(f => f.Field).ShouldBe(["portfolio.refreshMode"]);
        _fx.Data.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Omitted_bucket_and_totals_keep_the_current_values_and_an_empty_bucket_selects_the_accounts_own()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        _fx.Data.Buckets.Add(XDrishti.Domain.Accounts.PortfolioBucket.Create("Growth", AccountsFixture.Start).Value);
        await UpdateAsync(account, new PortfolioInput("Growth", false));

        var kept = (await UpdateAsync(account, new PortfolioInput(null, null, "daily"))).Detail!;
        var own = (await UpdateAsync(account, new PortfolioInput(string.Empty, null))).Detail!;
        await UpdateAsync(account, new PortfolioInput("Growth", null));
        var ownByName = (await UpdateAsync(account, new PortfolioInput("Example One · Portfolio", true))).Detail!;

        (kept.Portfolio.DefaultBucket, kept.Portfolio.IncludeInTotals, kept.Portfolio.RefreshMode).ShouldBe(("Growth", false, "daily"));
        (own.Portfolio.DefaultBucket, own.Portfolio.IncludeInTotals).ShouldBe(("Example One · Portfolio", false));
        (ownByName.Portfolio.DefaultBucket, ownByName.Portfolio.IncludeInTotals).ShouldBe(("Example One · Portfolio", true));
        account.Portfolio.DefaultBucketId.ShouldBeNull();
        _fx.Data.Events.Last(e => e.Event == AccountEventType.PortfolioUpdated).Detail.ShouldBe("Portfolio (role on): imports go to the account's own bucket, included in totals");
    }

    [Fact]
    public async Task The_audit_text_of_a_kept_custom_bucket_names_it()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        _fx.Data.Buckets.Add(XDrishti.Domain.Accounts.PortfolioBucket.Create("Growth", AccountsFixture.Start).Value);
        await UpdateAsync(account, new PortfolioInput("Growth", null));

        await UpdateAsync(account, new PortfolioInput(null, false));

        _fx.Data.Events.Last(e => e.Event == AccountEventType.PortfolioUpdated).Detail.ShouldBe("Portfolio (role on): imports go to bucket Growth, excluded from totals");
    }

    [Fact]
    public async Task A_daily_time_of_midnight_is_stored_as_given()
    {
        var account = await _fx.CreatedAsync();

        var (detail, _) = await UpdateAsync(account, new PortfolioInput(null, null, "daily", "00:00"));

        detail!.Portfolio.RefreshDailyTimeIst.ShouldBe("00:00");
        account.Portfolio.Refresh.DailyTimeIst.ShouldBe(new TimeOnly(0, 0));
    }
}
