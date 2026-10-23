using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The learning overview (spec P6-18): counts and the newest stored verdicts, nothing computed; an empty system is an answer.</summary>
public sealed class GetLearningOverviewHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ICellStatsStore _cells = Substitute.For<ICellStatsStore>();

    private readonly IModelRegistry _models = Substitute.For<IModelRegistry>();

    private readonly IExperimentLedger _experiments = Substitute.For<IExperimentLedger>();

    private readonly IHypothesisLog _hypotheses = Substitute.For<IHypothesisLog>();

    private readonly ILessonLog _lessons = Substitute.For<ILessonLog>();

    private readonly ILearningChangelog _changelog = Substitute.For<ILearningChangelog>();

    public GetLearningOverviewHandlerTests()
    {
        _cells.ListLatestAsync(default, default, default).ReturnsForAnyArgs([]);
        _models.ListAsync(default, default, default).ReturnsForAnyArgs([]);
        _experiments.ListAsync(default, default).ReturnsForAnyArgs([]);
        _hypotheses.ListAsync(default, default, default).ReturnsForAnyArgs([]);
        _lessons.ListAsync(default, default, default).ReturnsForAnyArgs([]);
        _changelog.ListAsync(default, default, default, default).ReturnsForAnyArgs([]);
    }

    private GetLearningOverviewHandler Handler() => new(_cells, _models, _experiments, _hypotheses, _lessons, _changelog);

    private static StoredModel Model(long id, ModelRole role, ValidationOutcome verdict) =>
        new(id, role, new ModelToRegister(
            new string('f', 64), new string('e', 64), 3, "FastTree", null, new string('a', 64), "tf-1", 3, 30, 10, 40, new DateOnly(2024, 8, 1), new DateOnly(2026, 6, 1), null, verdict, 3, 2, 2, false, 6.5, 0.7, 0.25,
            string.Empty, "report", "params", [0, 1], [0.4, 0.6], [0.5, 0.5, 0.5], 1, 3, DateTimeOffset.UnixEpoch, "t"),
            1);

    [Fact]
    public async Task An_empty_system_gives_zero_counts_no_newest_and_the_limits()
    {
        var result = await Handler().HandleAsync(new GetLearningOverviewQuery(), Ct);

        var view = result.Value;
        view.CellRows.ShouldBe(0);
        view.CellsAsOf.ShouldBeNull();
        new[] { view.Models, view.Experiments, view.Hypotheses, view.Lessons, view.Changelog }.ShouldAllBe(s => s.Counted == 0 && !s.Truncated && s.Newest == null && s.Split.Count == 0);
        view.Limits.ShouldContain(l => l.Contains("2.1 years", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Models_are_split_by_role_and_the_newest_and_the_champion_carry_their_stored_verdict()
    {
        var newest = Model(9, ModelRole.Challenger, ValidationOutcome.Inconclusive);
        var champion = Model(4, ModelRole.Champion, ValidationOutcome.Eligible);
        _models.ListAsync(null, 200, Ct).Returns([newest, champion, Model(2, ModelRole.Retired, ValidationOutcome.CannotBeatBaselines)]);
        _models.ListAsync(ModelRole.Champion, 1, Ct).Returns([champion]);

        var result = await Handler().HandleAsync(new GetLearningOverviewQuery(), Ct);

        var models = result.Value.Models;
        models.Counted.ShouldBe(3);
        models.Split.Select(s => s.Name + "=" + s.Count).ShouldBe(["champion=1", "challenger=1", "retired=1"], ignoreOrder: true);
        models.Newest!.Id.ShouldBe(9);
        models.Newest.Verdict.ShouldBe("Inconclusive");
        models.Newest.Detail.ShouldContain("hold-out none kept");
        models.Champion!.Id.ShouldBe(4);
        models.Champion.Verdict.ShouldBe("Eligible");
    }

    [Fact]
    public async Task A_section_at_the_count_limit_is_flagged_as_possibly_more()
    {
        var many = Enumerable.Range(1, 200).Select(i => Model(i, ModelRole.Challenger, ValidationOutcome.Inconclusive)).ToList();
        _models.ListAsync(null, 200, Ct).Returns(many);

        var result = await Handler().HandleAsync(new GetLearningOverviewQuery(), Ct);

        result.Value.Models.Truncated.ShouldBeTrue();
        result.Value.Models.Counted.ShouldBe(200);
    }
}
