using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>AU-11 (ADR 0243): the store itself is the last gate of a training run, inside the write transaction: no hold-out block, or a window that reaches the block or its embargo, stores nothing, whatever the caller did before.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class TrainingStoreWindowTests(ApiFactory factory)
{
    private static readonly DateOnly BlockStart = new(2031, 1, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Result<StoreOutcome>> StoreAsync(ScratchDatabase scratch, DateOnly to)
    {
        using var scope = scratch.Host.Services.CreateScope();
        var run = StoredRunFixtures.Run(to: to) with { IsTraining = true };
        return await scope.ServiceProvider.GetRequiredService<IRunStore>().StoreAsync(StoreKit.ToStore(run), Ct);
    }

    [Fact]
    public async Task A_training_run_is_refused_when_no_hold_out_block_exists_and_nothing_is_stored()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_train_noblock", Ct);

        var result = await StoreAsync(scratch, new DateOnly(2026, 6, 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(StoreErrors.TrainingWindowRefusedCode);
        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM research.sim_runs", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_training_run_is_refused_when_it_ends_inside_the_embargo_or_the_block_and_stored_when_clear()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_train_window", Ct);
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2031-01-01', '2026-01-01', '2031-06-30', 12, repeat('a', 64), now())", Ct);
        var latest = BlockStart.AddDays(-1 - new XDrishti.Domain.Confidence.HoldOutOptions().TrainingEmbargoDays);

        foreach (var to in new[] { BlockStart, BlockStart.AddDays(-1), latest.AddDays(1) })
        {
            var refused = await StoreAsync(scratch, to);
            refused.IsFailure.ShouldBeTrue();
            refused.Error.Code.ShouldBe(StoreErrors.TrainingWindowRefusedCode);
        }

        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM research.sim_runs", Ct)).ShouldBe(0);
        (await StoreAsync(scratch, latest)).IsSuccess.ShouldBeTrue();
        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM research.sim_runs WHERE purpose = 'training'", Ct)).ShouldBe(1);
    }
}
