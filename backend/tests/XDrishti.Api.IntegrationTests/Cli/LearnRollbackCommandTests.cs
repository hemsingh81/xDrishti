using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The verb <c>xd learn rollback</c> (spec CF-10, part 10d-2a) over a fake use case: every proposal state, the no-fallback list, the setting, the failed read, the bad line and the routing through <see cref="XdCommand"/>.</summary>
public sealed class LearnRollbackCommandTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    private sealed class Fake(Result<RollbackProposalsView> answer, Result<IReadOnlyList<ModelView>>? champion = null) : IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>, IQueryHandler<ListModelsQuery, Result<IReadOnlyList<ModelView>>>
    {
        public List<GetRollbackProposalsQuery> Seen { get; } = [];

        public List<ListModelsQuery> ModelsSeen { get; } = [];

        public ValueTask<Result<RollbackProposalsView>> HandleAsync(GetRollbackProposalsQuery query, CancellationToken cancellationToken)
        {
            Seen.Add(query);
            return ValueTask.FromResult(answer);
        }

        public ValueTask<Result<IReadOnlyList<ModelView>>> HandleAsync(ListModelsQuery query, CancellationToken cancellationToken)
        {
            ModelsSeen.Add(query);
            return ValueTask.FromResult(champion ?? Result.Success<IReadOnlyList<ModelView>>([]));
        }
    }

    private static Result<IReadOnlyList<ModelView>> Champion(long id) =>
        Result.Success<IReadOnlyList<ModelView>>([new ModelView(id, new string('a', 64), DateTimeOffset.UnixEpoch, "owner", "ts", "v1", "trainer", null, 1, 1, 1, Day, Day, null, "Eligible", 1, 1, 1, true, 0, 0, 0, [], "champion", true)]);

    private static RollbackProposalView Proposal(long id, long model, string state, long? previous, bool qualifies, string? actor = null, DateTimeOffset? decided = null, long? autoRow = null) =>
        new(id, model, "calibration", 0.12, 0.1, "Calibration gap 12 pp over a window of 200 at three consecutive checks.", ["ORB|a|swing"], previous, qualifies, new string('f', 64), Day, new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.Zero), state, actor, decided, autoRow);

    private static Result<RollbackProposalsView> View(bool autoRollback, params RollbackProposalView[] items) =>
        Result.Success(new RollbackProposalsView(items, new MlSettingsView(false, null, false, null, autoRollback, autoRollback ? 9 : null), ["A proposal changes nothing by itself."]));

    private static async Task<(int Code, string Out, string Error)> RunAsync(Fake fake, params string[] args)
    {
        var (code, output, error) = await RunRawAsync(fake, args);

        return (code, string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)), error);
    }

    private static async Task<(int Code, string Out, string Error)> RunRawAsync(Fake fake, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>>(fake)
            .AddSingleton<IQueryHandler<ListModelsQuery, Result<IReadOnlyList<ModelView>>>>(fake)
            .BuildServiceProvider();

        var code = await XdCommand.RunAsync(["learn", "rollback", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Every_state_is_listed_with_its_numbers_actor_and_time_and_the_models_with_no_fallback()
    {
        var fake = new Fake(View(
            true,
            Proposal(6, 40, "proposed", null, false),
            Proposal(4, 40, "proposed", null, false),
            Proposal(3, 30, "applied", 20, false, "auto", new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.Zero), 9),
            Proposal(2, 20, "applied", 10, true, "owner:me", new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)),
            Proposal(1, 10, "dismissed", null, false, "owner:me", new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero))));

        var (code, output, error) = await RunAsync(fake);

        code.ShouldBe(0);
        error.ShouldBeEmpty();
        output.ShouldContain("automatic rollback (ml.auto_rollback): ON");
        output.ShouldContain("rollback proposals: 5 (newest first)");
        output.ShouldContain("#4 model #40, trigger calibration: value 0.1200 against 0.1000; PROPOSED");
        output.ShouldContain("proposed 2026-11-05 (2026-11-05 10:30Z); actor none yet; decided not yet");
        output.ShouldContain("#3 model #30, trigger calibration: value 0.1200 against 0.1000; APPLIED");
        output.ShouldContain("actor auto; decided 2026-11-05 11:00Z; automatic under setting row 9");
        output.ShouldContain("actor owner:me; decided 2026-11-04 09:00Z");
        output.ShouldContain("#1 model #10, trigger calibration: value 0.1200 against 0.1000; DISMISSED");
        output.ShouldContain("Calibration gap 12 pp over a window of 200 at three consecutive checks.");
        output.ShouldContain("cells in alarm: ORB|a|swing");
        output.ShouldContain("previous champion: none (no champion fallback)");
        output.ShouldContain("previous champion: #10, still qualified when proposed (information only; never re-promoted automatically)");
        output.ShouldContain("previous champion: #20, did not qualify when proposed");
        output.ShouldContain("champion now: none (no model is the champion; the owner names one)");

        // the list says what it is (as of when the proposals were written), keeps open and applied apart, and names each model once by its newest proposal
        output.ShouldContain("proposals that leave (applied) or would leave (open) no qualifying previous champion, as of when they were proposed: applied (leave none): model #30 (newest proposal #3): the earlier champion #20 did not qualify when it was proposed open (would leave none if applied): model #40 (newest proposal #6): it replaced no earlier champion applying a rollback");
        output.ShouldNotContain("model #40 (newest proposal #4)");
        output.ShouldNotContain("model #20 (newest");
        output.ShouldNotContain("model #10 (newest");
        output.ShouldNotContain("models with no champion fallback");
        var asked = fake.Seen.ShouldHaveSingleItem();
        (asked.State, asked.ModelId).ShouldBe((null, null));
        var champion = fake.ModelsSeen.ShouldHaveSingleItem();
        (champion.Role, champion.Limit).ShouldBe(("champion", 1));
    }

    [Fact]
    public async Task A_champion_named_since_an_applied_rollback_is_shown_beside_the_frozen_list()
    {
        var applied = Proposal(3, 30, "applied", null, false, "owner:me", new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.Zero));

        var (code, output, _) = await RunAsync(new Fake(View(false, applied), Champion(77)));

        code.ShouldBe(0);
        output.ShouldContain("champion now: model #77");
        output.ShouldContain("applied (leave none): model #30 (newest proposal #3): it replaced no earlier champion");
        output.ShouldContain("open (would leave none if applied): none among the proposals shown");
    }

    [Fact]
    public async Task A_failed_champion_read_is_shown_as_unavailable_not_as_none()
    {
        var failing = new Fake(View(false), Result.Failure<IReadOnlyList<ModelView>>(Error.Validation("learning.invalid", "The limit is 1 to 200.")));

        var (code, output, _) = await RunAsync(failing);

        code.ShouldBe(0);
        output.ShouldContain("champion now: unavailable: learning.invalid: The limit is 1 to 200. Unavailable is not a finding and not a pass.");
        output.ShouldNotContain("champion now: none");
    }

    [Fact]
    public async Task No_proposals_and_the_setting_off_read_as_such()
    {
        var (code, output, _) = await RunAsync(new Fake(View(false)));

        code.ShouldBe(0);
        output.ShouldContain("automatic rollback (ml.auto_rollback): OFF (a proposal waits for the owner)");
        output.ShouldContain("rollback proposals: none");
        output.ShouldContain("applied (leave none): none among the proposals shown");
        output.ShouldContain("open (would leave none if applied): none among the proposals shown");
    }

    [Fact]
    public async Task Missing_numbers_read_unavailable_and_never_as_zero()
    {
        var item = Proposal(4, 40, "proposed", 1, true) with { Value = null, Limit = null };

        var (_, output, _) = await RunAsync(new Fake(View(false, item)));

        output.ShouldContain("numbers unavailable (not a finding)");
        output.ShouldNotContain("value 0.0000");
    }

    [Fact]
    public async Task A_failed_read_exits_1_as_unavailable_and_never_as_none()
    {
        var fake = new Fake(Result.Failure<RollbackProposalsView>(Error.Validation("learning.invalid", "The limit is 1 to 200.")));

        var (code, output, error) = await RunAsync(fake);

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        string.Join(' ', error.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)).ShouldContain("unavailable: learning.invalid: The limit is 1 to 200. Unavailable is not a finding and not a pass.");
        error.ShouldNotContain("none");
        fake.ModelsSeen.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_extra_argument_exits_2_without_calling_the_use_case()
    {
        var fake = new Fake(View(false));

        var (code, _, error) = await RunAsync(fake, "--all");

        code.ShouldBe(2);
        error.ShouldContain("unknown argument '--all'");
        error.ShouldContain("usage:");
        fake.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_use_case_that_throws_exits_3()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await XdCommand.RunAsync(["learn", "rollback"], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        code.ShouldBe(3);
        error.ToString().ShouldContain("failed:");
    }

    [Fact]
    public async Task Long_sentences_are_wrapped_and_stored_text_cannot_write_control_characters()
    {
        var item = Proposal(4, 40, "proposed", null, false) with { Text = string.Join(' ', Enumerable.Repeat("word", 80)) + " bad\u001b[31m text\nsecond line", Actor = "owner:\u0007x", Cells = ["A\u001bB"] };

        var (_, output, _) = await RunRawAsync(new Fake(View(false, item)));

        output.Split('\n').ShouldAllBe(l => l.TrimEnd('\r').Length <= LearnReportText.Width);
        output.ShouldNotContain("\u001b");
        output.ShouldNotContain("\u0007");
        string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)).ShouldContain("bad [31m text second line");
    }
}
