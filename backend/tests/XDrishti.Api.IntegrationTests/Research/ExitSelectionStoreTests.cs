using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 9 of P4-06 over the real adapter on a real TimescaleDB: an exit choice round-trips with its fold rows, the same choice again is "already stored", another record under its key is <c>store.run_conflict</c>, a choice whose comparison is
/// not stored is refused and writes nothing.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExitSelectionStoreTests(ApiFactory factory)
{
    private const string Entries = "e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ExitCandidate Two = new("FIXED_RR", [new ExitParameterValue("rr", [2])]);

    private static readonly ExitCandidate Three = new("FIXED_RR", [new ExitParameterValue("rr", [3])]);

    private static (SimulationRun A, SimulationRun B) Runs() => (StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo()), StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo()));

    private static ComparisonRun Comparison(SimulationRun a, SimulationRun b)
    {
        CandidateRun[] members = [new("FIXED_RR{rr=2}", Two, a, 0), new("FIXED_RR{rr=3}", Three, b, 0)];
        return new ComparisonRun(members, ComparisonReport.Build(members, true), Entries);
    }

    /// <summary>A small choice with real folds: two candidates, one trade a weekday for 18 months, the second a little better.</summary>
    private static ExitSelection Selection(string comparisonEntries, double edge = 0.3)
    {
        var random = new Random(5);
        var sessions = new List<DateOnly>();
        for (var d = new DateOnly(2024, 4, 1); d <= new DateOnly(2025, 12, 31); d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(d);
            }
        }

        ExitCandidate[] candidates = [Two, Three];
        var trades = sessions.SelectMany(d => candidates.Select((c, i) => new SelectionTrade(c.Id, d, d, (i * edge) + (random.NextDouble() - 0.5)))).ToList();
        return ExitSelector.Select(new SelectionInput("ORB 15m BUY intraday *", "RFilled", false, comparisonEntries, candidates, trades, sessions, new SelectionOptions()));
    }

    private async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<ComparisonRun> StoredComparisonAsync()
    {
        var (a, b) = Runs();
        var comparison = Comparison(a, b);
        await WithScope(async provider =>
        {
            var runs = provider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>();
            foreach (var run in new[] { a, b })
            {
                (await runs.HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
            }

            var header = await provider.GetRequiredService<ICommandHandler<StoreComparisonCommand, StoreOutcome>>().HandleAsync(new StoreComparisonCommand(comparison, SimulateStore.ComparisonRecord(comparison.Report), SimulateStore.Actor), Ct);
            header.IsSuccess.ShouldBeTrue();
            return true;
        });
        return comparison;
    }

    private Task<Result<StoreOutcome>> StoreAsync(ComparisonRun comparison, ExitSelection selection, string? json = null)
    {
        var (text, sha) = SimulateSelect.Record(selection, SimulateCompare.ExportFormat);
        if (json is not null)
        {
            text = json;
            sha = StoreKit.Sha256(json);
        }

        return WithScope(provider => provider.GetRequiredService<ICommandHandler<StoreExitSelectionCommand, StoreOutcome>>()
            .HandleAsync(new StoreExitSelectionCommand(selection, comparison.ComparisonKey, "ORB", "15m", "BUY", "intraday", text, sha, SimulateStore.Actor), Ct).AsTask());
    }

    [Fact]
    public async Task A_choice_is_stored_with_its_fold_rows_and_the_same_choice_again_is_already_stored()
    {
        var comparison = await StoredComparisonAsync();
        var selection = Selection(comparison.EntriesKey);
        var key = ExitSelectionKey.Of(comparison.ComparisonKey, selection);

        var first = await StoreAsync(comparison, selection);
        var second = await StoreAsync(comparison, selection);

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        first.Value.WasAlreadyStored.ShouldBeFalse();
        second.Value.WasAlreadyStored.ShouldBeTrue();
        second.Value.Id.ShouldBe(first.Value.Id);
        selection.Folds.Count.ShouldBeGreaterThan(3);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.exit_selections WHERE selection_key = @k", Ct, ("k", key))).ShouldBe(1);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.exit_selection_folds f JOIN research.exit_selections s ON s.id = f.selection_id WHERE s.selection_key = @k", Ct, ("k", key))).ShouldBe(selection.Folds.Count * 2);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.exit_selection_folds f JOIN research.exit_selections s ON s.id = f.selection_id WHERE s.selection_key = @k AND f.is_winner", Ct, ("k", key))).ShouldBe(selection.Folds.Count(f => f.Winner is not null));
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.exit_selections s JOIN research.sim_comparisons c ON c.id = s.comparison_id WHERE s.selection_key = @k AND c.comparison_key = @c AND s.regime = '*' AND s.style = 'intraday'", Ct, ("k", key), ("c", comparison.ComparisonKey))).ShouldBe(1);
    }

    [Fact]
    public async Task Another_record_under_a_stored_selection_key_is_a_conflict_and_changes_nothing()
    {
        var comparison = await StoredComparisonAsync();
        var selection = Selection(comparison.EntriesKey);
        var key = ExitSelectionKey.Of(comparison.ComparisonKey, selection);
        (await StoreAsync(comparison, selection)).IsSuccess.ShouldBeTrue();

        var again = await StoreAsync(comparison, selection, json: "{\"another\":\"record\"}");

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(StoreErrors.RunConflictCode);
        again.Error.Fields!.Select(f => f.Field).ShouldContain("record");
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.exit_selections WHERE selection_key = @k", Ct, ("k", key))).ShouldBe(1);
    }

    [Fact]
    public async Task A_choice_whose_comparison_is_not_stored_is_refused_and_writes_nothing()
    {
        var (a, b) = Runs();
        var comparison = Comparison(a, b);
        var selection = Selection(comparison.EntriesKey);

        var result = await StoreAsync(comparison, selection);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(StoreErrors.WriteFailedCode);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.exit_selections WHERE selection_key = @k", Ct, ("k", ExitSelectionKey.Of(comparison.ComparisonKey, selection)))).ShouldBe(0);
    }

    [Fact]
    public async Task A_choice_with_a_bad_digest_or_no_selection_is_refused_before_the_database()
    {
        var comparison = await StoredComparisonAsync();
        var selection = Selection(comparison.EntriesKey);

        var none = await WithScope(provider => provider.GetRequiredService<ICommandHandler<StoreExitSelectionCommand, StoreOutcome>>().HandleAsync(new StoreExitSelectionCommand(null, comparison.ComparisonKey, "ORB", "15m", "BUY", "intraday", "{}", new string('a', 64), SimulateStore.Actor), Ct).AsTask());
        var bad = await WithScope(provider => provider.GetRequiredService<ICommandHandler<StoreExitSelectionCommand, StoreOutcome>>().HandleAsync(new StoreExitSelectionCommand(selection, comparison.ComparisonKey, "ORB", "15m", "BUY", "intraday", "{}", "nope", SimulateStore.Actor), Ct).AsTask());

        none.Error.Code.ShouldBe(StoreErrors.RunInvalidCode);
        bad.Error.Code.ShouldBe(StoreErrors.RunInvalidCode);
        bad.Error.Fields!.Select(f => f.Field).ShouldContain("recordSha256");
    }
}
