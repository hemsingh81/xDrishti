using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Auto;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Auto;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.Auto;

public sealed class EvaluateAutoProgrammeCheckHandlerTests
{
    private static readonly DateOnly HoldOut = new(2026, 1, 1);
    private static readonly Guid JobId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SimAllSnapshot Job(string state) =>
        new(new SimAllJob(JobId, state, null, new DateOnly(2020, 1, 1), new DateOnly(2025, 12, 31), HoldOut, 1, "owner:x", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null), new SimAllCounts(1, 1, 0, 0, 0, 0, 0, 0));

    private static TrainingRunHeader Header(string key, DateOnly to) => new(key, "orb", new string('a', 64), new DateOnly(2020, 1, 1), to, 1, 1, 1, 0, DateTimeOffset.UnixEpoch);

    private static EvaluateAutoProgrammeCheckHandler Handler(
        AutoStoredFacts stored, IReadOnlyList<SimAllSnapshot> jobs, IReadOnlyList<string> keys, IReadOnlyList<TrainingRunHeader> headers, bool catalogHasEssential = true, bool jobsThrow = false)
    {
        var reader = Substitute.For<IAutoProgrammeFactsReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(stored);
        var catalog = Substitute.For<ISettingsCatalogSource>();
        catalog.Read().Returns(catalogHasEssential ? SettingsRegistry.All.Select(d => new SettingsCatalogEntry(d, null, "default")).ToList() : []);
        var progress = Substitute.For<ITrainingJobProgress>();
        if (jobsThrow)
        {
            progress.ListProgressAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<SimAllSnapshot>>(_ => throw new InvalidOperationException("tables unreadable"));
        }
        else
        {
            progress.ListProgressAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(jobs);
        }

        var store = Substitute.For<ISimAllStore>();
        store.ListFinishedAsync(JobId, Arg.Any<CancellationToken>()).Returns(new SimAllFinished(keys, 0));
        var runs = Substitute.For<ITrainingRunReader>();
        runs.ListByKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(headers);
        return new EvaluateAutoProgrammeCheckHandler(reader, catalog, progress, store, runs, NullLogger<EvaluateAutoProgrammeCheckHandler>.Instance);
    }

    private static readonly AutoStoredFacts AllStored = new(2, 1, true, 2, 1);

    private static async Task<string> VerdictAsync(EvaluateAutoProgrammeCheckHandler handler, string id)
    {
        var result = await handler.HandleAsync(new EvaluateAutoProgrammeCheckQuery(), Ct);
        return result.Value.Report.Items.Single(i => i.Id == id).Verdict;
    }

    [Fact]
    public async Task A_finished_job_whose_runs_are_training_runs_before_the_hold_out_is_met_and_so_is_the_programme()
    {
        var handler = Handler(AllStored, [Job(SimAllStates.Completed)], ["k1"], [Header("k1", HoldOut.AddDays(-1))]);

        var result = await handler.HandleAsync(new EvaluateAutoProgrammeCheckQuery(), Ct);

        result.Value.Report.Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public async Task A_run_the_training_reader_does_not_see_breaches_the_fence()
    {
        var handler = Handler(AllStored, [Job(SimAllStates.Completed)], ["k1", "k2"], [Header("k1", HoldOut.AddDays(-1))]);

        (await VerdictAsync(handler, "job")).ShouldBe(PhaseVerdicts.NotMet);
    }

    [Fact]
    public async Task A_training_run_reaching_the_hold_out_breaches_the_fence()
    {
        var handler = Handler(AllStored, [Job(SimAllStates.CompletedWithFailures)], ["k1"], [Header("k1", HoldOut)]);

        (await VerdictAsync(handler, "job")).ShouldBe(PhaseVerdicts.NotMet);
    }

    [Fact]
    public async Task A_job_still_running_is_not_met_and_no_job_is_not_evaluable()
    {
        (await VerdictAsync(Handler(AllStored, [Job(SimAllStates.Running)], [], []), "job")).ShouldBe(PhaseVerdicts.NotMet);
        (await VerdictAsync(Handler(AllStored, [], [], []), "job")).ShouldBe(PhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public async Task Unreadable_job_tables_are_not_evaluable_never_a_pass()
    {
        var handler = Handler(AllStored, [], [], [], jobsThrow: true);

        (await VerdictAsync(handler, "job")).ShouldBe(PhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public async Task An_empty_catalog_makes_the_essential_item_not_evaluable()
    {
        var handler = Handler(AllStored, [Job(SimAllStates.Completed)], ["k1"], [Header("k1", HoldOut.AddDays(-1))], catalogHasEssential: false);

        (await VerdictAsync(handler, "essential")).ShouldBe(PhaseVerdicts.NotEvaluable);
    }
}
