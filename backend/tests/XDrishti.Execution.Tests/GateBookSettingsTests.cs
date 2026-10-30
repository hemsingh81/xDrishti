using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

public sealed class GateBookSettingsTests
{
    [Theory]
    [InlineData(ExecMode.SemiAuto, true, ExecEnvironment.Sandbox, true, "open")]
    [InlineData(ExecMode.AutoLive, true, ExecEnvironment.Live, true, "open")]
    [InlineData(ExecMode.SemiAuto, false, ExecEnvironment.Live, false, "execution_disabled")]
    [InlineData(ExecMode.Paper, true, ExecEnvironment.Live, false, "mode_not_executing")]
    [InlineData(ExecMode.ManualLive, true, ExecEnvironment.Live, false, "mode_not_executing")]
    [InlineData(ExecMode.Assisted, true, ExecEnvironment.Live, false, "mode_not_executing")]
    [InlineData(ExecMode.SemiAuto, true, ExecEnvironment.None, false, "environment_none")]
    [InlineData(ExecMode.AutoLive, true, ExecEnvironment.None, false, "environment_none")]
    public void The_gate_table(ExecMode mode, bool enabled, ExecEnvironment env, bool open, string reason)
    {
        var d = AccountGate.MayAct(new ModeAnswer(mode, enabled), env);

        (d.Open, d.Reason).ShouldBe((open, reason));
    }

    [Fact]
    public async Task No_answer_and_the_default_mode_source_keep_every_gate_closed()
    {
        AccountGate.MayAct(null, ExecEnvironment.Live).Open.ShouldBeFalse();
        var answer = await new ClosedModeSource().AnswerAsync(Guid.NewGuid(), CancellationToken.None);

        answer.ShouldBeNull();
        AccountGate.MayAct(answer, ExecEnvironment.Live).Reason.ShouldBe("no_mode_answer");
    }

    [Fact]
    public async Task The_default_halt_source_is_unreadable_and_unreadable_means_halted()
    {
        var reading = await new ClosedHaltSource().ReadAsync(CancellationToken.None);

        reading.ShouldBe(HaltReading.Unreadable);
        HaltPolicy.EntriesBlocked(reading).ShouldBeTrue();
        HaltPolicy.EntriesBlocked(HaltReading.Halted).ShouldBeTrue();
        HaltPolicy.EntriesBlocked(HaltReading.Clear).ShouldBeFalse();
    }

    [Fact]
    public void Three_actions_are_refused_in_every_mode_and_the_rest_are_not()
    {
        foreach (var mode in Enum.GetValues<ExecMode>())
        {
            ActionPolicy.Check(ExecAction.StopLoosen, mode).ShouldBe(ActionVerdict.Refused);
            ActionPolicy.Check(ExecAction.AddToPosition, mode).ShouldBe(ActionVerdict.Refused);
            ActionPolicy.Check(ExecAction.ReEntry, mode).ShouldBe(ActionVerdict.Refused);
            ActionPolicy.Check(ExecAction.StopTighten, mode).ShouldBe(ActionVerdict.Allowed);
        }
    }

    [Fact]
    public void The_position_book_folds_fills_and_never_clamps_an_oversold_position()
    {
        var a = Guid.NewGuid();
        var book = new PositionBook();
        book.Apply(new BookFill(a, "TCS", "INTRADAY", ExecSide.Buy, 10, 100m));
        book.Apply(new BookFill(a, "TCS", "INTRADAY", ExecSide.Sell, 4, 102m));

        var p = book.Find(a, "TCS", "INTRADAY")!;
        (p.NetQty, p.AvgPrice, p.Realised, p.Oversold).ShouldBe((6, 100m, 8.00m, false));

        book.Apply(new BookFill(a, "TCS", "INTRADAY", ExecSide.Sell, 9, 101m));
        var over = book.Find(a, "TCS", "INTRADAY")!;
        (over.NetQty, over.Oversold).ShouldBe((-3, true));
        over.Realised.ShouldBe(8m + 6m);
        book.Find(Guid.NewGuid(), "TCS", "INTRADAY").ShouldBeNull();
    }

    private sealed class Snapshot(IReadOnlyDictionary<string, string>? values) : IExecSettingsSnapshots
    {
        public Task<IReadOnlyDictionary<string, string>?> NewestAsync(CancellationToken cancellationToken) => Task.FromResult(values);
    }

    [Fact]
    public async Task No_snapshot_means_all_defaults_and_canary_is_pending()
    {
        var view = await new ExecSettingsReader(new Snapshot(null)).ReadAsync(CancellationToken.None);

        view.Values.Count.ShouldBe(ExecSettingRegistry.All.Count);
        view.Values["exec.canary_pending"].ShouldBe("true");
        view.Values["exec.max_orders_per_second"].ShouldBe("2");
        view.Replaced.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_snapshot_overrides_a_valid_value_ignores_an_unknown_key_and_replaces_an_out_of_range_one()
    {
        var view = await new ExecSettingsReader(new Snapshot(new Dictionary<string, string>
        {
            ["exec.max_orders_per_second"] = "4",
            ["exec.max_order_qty"] = "10001",
            ["exec.square_off_time"] = "15:30",
            ["exec.canary_pending"] = "maybe",
            ["exec.unknown"] = "1",
        })).ReadAsync(CancellationToken.None);

        view.Values["exec.max_orders_per_second"].ShouldBe("4");
        view.Values["exec.max_order_qty"].ShouldBe("2000");
        view.Values["exec.square_off_time"].ShouldBe("15:15");
        view.Values["exec.canary_pending"].ShouldBe("true");
        view.Values.ShouldNotContainKey("exec.unknown");
        view.Replaced.ShouldBe(["exec.max_order_qty", "exec.square_off_time", "exec.canary_pending"], ignoreOrder: true);
    }

    [Fact]
    public void Every_default_is_inside_its_own_range()
    {
        ExecSettingRegistry.All.ShouldAllBe(s => ExecSettingRegistry.IsValid(s, s.Default));
        ExecSettingRegistry.All.Count.ShouldBe(53); // four readiness keys from P12-11, three confirm keys from P12-08b
    }
}
