using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// CF-10 part 10d-1 review (ADR 0220): <c>IMlInfluenceSwitch</c> promises false whenever the switch cannot be read. The store is pointed at a server that is not there (nothing listens on port 1): the influence switch reads OFF, the read of all three switches lets the failure through (an automatic
/// act that needs its switch does nothing), and a cancelled read is still a cancellation.
/// </summary>
public sealed class MlSwitchSettingsFailureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MlSwitchSettings Unreachable() =>
        new(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false"), NullLogger<MlSwitchSettings>.Instance);

    [Fact]
    public async Task A_switch_that_cannot_be_read_because_the_database_fails_reads_as_off()
    {
        (await Unreachable().IsOnAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_read_of_all_three_switches_lets_the_failure_through_so_an_automatic_act_does_nothing()
    {
        await Should.ThrowAsync<NpgsqlException>(async () => await Unreachable().CurrentAsync(Ct));
    }

    [Fact]
    public async Task A_cancelled_read_is_a_cancellation_not_an_off()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Unreachable().IsOnAsync(cancelled.Token));
    }
}
