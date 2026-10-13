using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The per-account holdings sync as a read-only row of the Services screen (ADR 0018 §8): it stays where ADR 0013 put it (Accounts → Portfolio settings), so the row only shows the state the portfolio module
/// reports through <see cref="IAccountSyncSource"/> — the latest run and the earliest next automatic run — and links there.
/// </summary>
internal sealed class AccountSyncEntry(IAccountSyncSource source)
{
    public const string ManageUrl = "/accounts";

    public async ValueTask<ServiceView> ReadAsync(CancellationToken cancellationToken)
    {
        var state = await source.GetAsync(cancellationToken);
        var status = state.Last is { Status: "running" } ? "running" : !state.AnyOn ? "paused" : state.Last is { Status: "failed" } ? "failed" : "healthy";
        return new ServiceView(
            ServiceIds.AccountSync,
            "Account & portfolio sync",
            "Fetches the holdings of each account with the Portfolio role. Set per account under Accounts, Portfolio settings.",
            "xd-api",
            true,
            ManageUrl,
            state.AnyOn,
            status,
            new ScheduleSpec("daily", TradingDaysOnly: false),
            state.AnyOn ? "Once a day per account, at the time set on each account" : "Off for every account",
            [],
            new ExecutionView(0, 10, true, false),
            [],
            state.Last is null ? null : new ServiceRunView(state.Last.Id, ServiceIds.AccountSync, state.Last.Trigger, 1, state.Last.StartedAt, state.Last.FinishedAt, state.Last.Status, state.Last.Message, null),
            state.NextRunAt,
            [],
            true,
            0);
    }
}
