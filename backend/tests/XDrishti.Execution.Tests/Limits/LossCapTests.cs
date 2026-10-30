using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Limits;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Tests.Limits;

/// <summary>Spec P12-09 criteria 3 and 4: the worst-case entry rule (a profit gives no room, the boundary is allowed, unavailable refuses) and the mark-to-market day halt through a fake <see cref="IHaltRequests"/>.</summary>
public sealed class LossCapTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Session = new(2026, 10, 23);

    [Theory]
    [InlineData(-600, 870, 30, null)]
    [InlineData(-600, 870, 100, AutoLiveCodes.LossDay)]
    [InlineData(400, 870, 30, null)]
    [InlineData(400, 870, 631, AutoLiveCodes.LossDay)]
    [InlineData(400, 0, 1500, null)]
    [InlineData(0, 0, 1500, null)]
    [InlineData(0, 0, 1500.01, AutoLiveCodes.LossDay)]
    [InlineData(0, 870, 630, null)]
    [InlineData(-600, 0, 900, null)]
    [InlineData(-600, 0, 901, AutoLiveCodes.LossDay)]
    public void An_entry_is_refused_when_the_days_worst_case_passes_the_cap(double realised, double openRisk, double newRisk, string? code)
    {
        LossCap.Check((decimal)realised, (decimal)openRisk, (decimal)newRisk, 1_500m).ShouldBe(code);
    }

    [Fact]
    public void Realised_minus_600_open_risk_870_and_new_risk_30_is_exactly_1500_and_a_profit_counts_as_zero()
    {
        LossCap.WorstCase(-600m, 870m, 30m).ShouldBe(1_500m);
        LossCap.WorstCase(-600m, 870m, 100m).ShouldBe(1_570m);
        LossCap.WorstCase(400m, 870m, 30m).ShouldBe(900m);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_unavailable_realised_or_open_risk_figure_refuses_cap_loss_unavailable(bool realisedMissing, bool openRiskMissing)
    {
        LossCap.Check(realisedMissing ? null : 0m, openRiskMissing ? null : 0m, 1m, 1_500m).ShouldBe(AutoLiveCodes.LossUnavailable);
    }

    [Theory]
    [InlineData(-1000, -499, LossHaltVerdict.Clear)]
    [InlineData(-1000, -500, LossHaltVerdict.Halt)]
    [InlineData(-200, -1500, LossHaltVerdict.Halt)]
    [InlineData(400, -1899, LossHaltVerdict.Clear)]
    public void The_day_halts_when_realised_plus_unrealised_is_at_or_below_minus_the_cap(double realised, double unrealised, LossHaltVerdict verdict)
    {
        LossCap.Halt((decimal)realised, (decimal)unrealised, 1_500m).ShouldBe(verdict);
    }

    private sealed class Caps(LossCapSetting? setting) : ILossCapSource
    {
        public Task<LossCapSetting?> ForAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(setting);
    }

    private sealed class Log : IActionLog
    {
        public List<ActionLogEntry> Rows { get; } = [];

        public Task<long?> AppendAsync(ActionLogEntry entry, CancellationToken cancellationToken)
        {
            Rows.Add(entry);
            return Task.FromResult<long?>(Rows.Count);
        }
    }

    private static (LossCapEvaluator Evaluator, RecordingHaltRequests Halts, Log Log) Evaluator(LossCapSetting? setting)
    {
        var halts = new RecordingHaltRequests();
        var log = new Log();
        return (new LossCapEvaluator(new Caps(setting), halts, log, NullLogger<LossCapEvaluator>.Instance), halts, log);
    }

    [Fact]
    public async Task At_the_cap_the_evaluator_asks_p12_05_for_a_loss_cap_halt_at_level_entries_once_and_writes_one_row()
    {
        var (evaluator, halts, log) = Evaluator(new LossCapSetting(1_500m, false, 9));

        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_000m, -499m, Ct)).ShouldBe(LossHaltVerdict.Clear);
        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_000m, -500m, Ct)).ShouldBe(LossHaltVerdict.Halt);
        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_100m, -500m, Ct)).ShouldBe(LossHaltVerdict.Halt);

        halts.Requests.ShouldBe([(LimitsScene.Account, HaltLevel.Entries, HaltTriggers.LossCap)]);
        log.Rows.ShouldHaveSingleItem().Kind.ShouldBe(ActionKinds.LossCapHalt);
        log.Rows[0].By.ShouldBe(ActionKinds.BySystem);
        log.Rows[0].LimitsId.ShouldBe(9);
    }

    [Fact]
    public async Task A_failed_halt_request_is_not_latched_and_is_asked_again_on_the_next_call()
    {
        var halts = new FlakyHalts();
        var evaluator = new LossCapEvaluator(new Caps(new LossCapSetting(1_500m, false, 9)), halts, new Log(), NullLogger<LossCapEvaluator>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(async () => await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_500m, 0m, Ct));
        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_500m, 0m, Ct)).ShouldBe(LossHaltVerdict.Halt);
        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_600m, 0m, Ct)).ShouldBe(LossHaltVerdict.Halt);

        halts.Calls.ShouldBe(2);
    }

    private sealed class FlakyHalts : IHaltRequests
    {
        public int Calls { get; private set; }

        public Task RequestAsync(Guid? accountId, HaltLevel level, string triggerCode, string detail, CancellationToken cancellationToken)
        {
            Calls++;
            return Calls == 1 ? Task.FromException(new InvalidOperationException("halt store down")) : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task With_exit_all_on_loss_halt_on_the_level_asked_for_is_exit_all()
    {
        var (evaluator, halts, _) = Evaluator(new LossCapSetting(1_500m, true, 9));

        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_500m, 0m, Ct)).ShouldBe(LossHaltVerdict.Halt);

        halts.Requests.ShouldBe([(LimitsScene.Account, HaltLevel.ExitAll, HaltTriggers.LossCap)]);
    }

    [Fact]
    public async Task An_unavailable_mark_does_not_halt_and_notifies_once()
    {
        var (evaluator, halts, log) = Evaluator(new LossCapSetting(1_500m, false, 9));

        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -1_000m, null, Ct)).ShouldBe(LossHaltVerdict.Unavailable);
        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, null, -2_000m, Ct)).ShouldBe(LossHaltVerdict.Unavailable);

        halts.Requests.ShouldBeEmpty();
        var row = log.Rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe(ActionKinds.LossCapNotice);
        row.Codes.ShouldBe([AutoLiveCodes.LossUnavailable]);
    }

    [Fact]
    public async Task Without_a_complete_sheet_there_is_no_number_and_no_cap_halt()
    {
        var (evaluator, halts, log) = Evaluator(null);

        (await evaluator.EvaluateAsync(LimitsScene.Account, Session, -100_000m, -100_000m, Ct)).ShouldBeNull();

        halts.Requests.ShouldBeEmpty();
        log.Rows.ShouldBeEmpty();
    }

    private sealed class Store(LimitsRow? row) : ILimitsStore
    {
        public Task<LimitsReading> ReadAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(new LimitsReading(LimitsScene.Now, row));
    }

    [Fact]
    public async Task The_loss_cap_source_gives_the_cap_of_the_sheet_in_force_and_none_for_an_incomplete_one()
    {
        var complete = await new LimitsLossCapSource(new Store(new LimitsRow(5, LimitsScene.Account, LimitsScene.Sheet with { ExitAllOnLossHalt = true }, LimitsScene.Now))).ForAsync(LimitsScene.Account, Ct);
        var incomplete = await new LimitsLossCapSource(new Store(new LimitsRow(6, LimitsScene.Account, LimitsScene.Sheet with { AllowList = [] }, LimitsScene.Now))).ForAsync(LimitsScene.Account, Ct);
        var none = await new LimitsLossCapSource(new Store(null)).ForAsync(LimitsScene.Account, Ct);

        complete.ShouldBe(new LossCapSetting(1_500m, true, 5));
        incomplete.ShouldBeNull();
        none.ShouldBeNull();
    }
}
