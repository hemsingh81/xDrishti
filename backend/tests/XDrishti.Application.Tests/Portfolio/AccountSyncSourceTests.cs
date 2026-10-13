using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>The per-account sync as the Services screen's read-only row gets it, through the platform's port.</summary>
public sealed class AccountSyncSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();

    private readonly IHoldingSyncStore _syncs = Substitute.For<IHoldingSyncStore>();

    private AccountSyncSource Source() => new(_accounts, _syncs, new FakeTimeProvider(Now));

    [Fact]
    public async Task Without_accounts_the_refresh_is_off_and_there_is_nothing_to_show()
    {
        _accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BrokerAccount>)[]);
        _syncs.GetLastAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((HoldingSync?)null);

        var state = await Source().GetAsync(TestContext.Current.CancellationToken);

        (state.AnyOn, state.Last, state.NextRunAt).ShouldBe((false, null, null));
    }

    [Theory]
    [InlineData(SyncTrigger.Auto, "scheduled")]
    [InlineData(SyncTrigger.Manual, "manual")]
    [InlineData(SyncTrigger.Import, "manual")]
    public async Task The_latest_run_is_given_with_the_services_spelling_of_its_trigger_and_status(SyncTrigger trigger, string expected)
    {
        var run = HoldingSync.Start(Guid.NewGuid(), trigger, "owner", Now.AddHours(-2));
        run.Fail(Now.AddHours(-2).AddSeconds(3), "Dhan refused the token");
        _accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BrokerAccount>)[]);
        _syncs.GetLastAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(run);

        var state = await Source().GetAsync(TestContext.Current.CancellationToken);

        var last = state.Last.ShouldNotBeNull();
        (last.Id, last.Trigger, last.Status, last.Message).ShouldBe((run.Id, expected, "failed", "Dhan refused the token"));
    }
}
