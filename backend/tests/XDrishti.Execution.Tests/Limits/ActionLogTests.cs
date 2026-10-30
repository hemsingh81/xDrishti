using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Execution.Application.Limits;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Tests.Limits;

/// <summary>
/// Spec P12-09 criteria 11 and 12 at the use-case level, on fakes: every automatic entry writes one <c>auto_entry</c> row with the settings and limits ids and the plain text, in the same reservation as its ledger row; every refusal writes
/// <c>auto_refused</c> with its codes; the reducing actions write their kind; nothing reaches a hand-over port without its row; and an account that is not <c>auto_live</c> with both switches ON reads the two switches and nothing else.
/// </summary>
public sealed class ActionLogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Scene : IModeSource, IAutoLiveSettingsSource, ILimitsStore, ILedger, IActionLog, IOwnerPresence, IFeedHealth, IEntryHandOver, IReduceHandOver
    {
        public ExecMode? Mode { get; set; } = ExecMode.AutoLive;

        public AutoLiveSettings? Settings { get; set; } = LimitsScene.On;

        public LimitsRow? Row { get; set; } = new(11, LimitsScene.Account, LimitsScene.Sheet, LimitsScene.Now.AddDays(-3));

        public LedgerDay Day { get; set; } = new(0, 0m);

        public bool? StrategyUsed { get; set; } = true;

        public string? DatabaseRefusal { get; set; }

        public bool LogFails { get; set; }

        public List<string> Reads { get; } = [];

        public List<(long Id, ActionLogEntry Row)> Actions { get; } = [];

        public List<LedgerReservation> Ledger { get; } = [];

        public List<string> HandedOver { get; } = [];

        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Reads.Add("mode");
            return Task.FromResult(Mode is { } m ? new ModeAnswer(m, true) : null);
        }

        Task<AutoLiveSettings?> IAutoLiveSettingsSource.ReadAsync(CancellationToken cancellationToken)
        {
            Reads.Add("settings");
            return Task.FromResult(Settings);
        }

        public Task<LimitsReading> ReadAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Reads.Add("limits");
            return Task.FromResult(new LimitsReading(LimitsScene.Now, Row));
        }

        public Task<LedgerDay?> DayAsync(Guid accountId, DateOnly session, CancellationToken cancellationToken)
        {
            Reads.Add("day");
            return Task.FromResult<LedgerDay?>(Day);
        }

        public Task<bool?> StrategyUsedAsync(Guid accountId, string strategyHash, CancellationToken cancellationToken)
        {
            Reads.Add("strategy");
            return Task.FromResult(StrategyUsed);
        }

        public Task<LedgerResult> ReserveAsync(LedgerReservation entry, ActionLogEntry action, CancellationToken cancellationToken)
        {
            if (DatabaseRefusal is { } code)
            {
                return Task.FromResult(new LedgerResult(null, null, code));
            }

            var id = Write(action);
            Ledger.Add(entry);
            Day = new LedgerDay(Day.Orders + 1, Day.Notional + entry.Notional);
            return Task.FromResult(new LedgerResult(Ledger.Count, id, null));
        }

        public Task<long?> AppendAsync(ActionLogEntry entry, CancellationToken cancellationToken) => Task.FromResult<long?>(LogFails ? null : Write(entry));

        public Task<OwnerSeen?> NewestAsync(CancellationToken cancellationToken)
        {
            Reads.Add("owner");
            return Task.FromResult<OwnerSeen?>(new OwnerSeen(LimitsScene.Now.AddMinutes(-1), false));
        }

        public Task<DateTimeOffset?> LastPriceAtAsync(string symbol, CancellationToken cancellationToken)
        {
            Reads.Add("feed");
            return Task.FromResult<DateTimeOffset?>(LimitsScene.Now.AddSeconds(-2));
        }

        public Task HandOverAsync(Guid intentPublicId, long ledgerId, CancellationToken cancellationToken)
        {
            Actions.Any(a => a.Row.IntentPublicId == intentPublicId && a.Row.Kind is ActionKinds.AutoEntry or ActionKinds.TapEntry).ShouldBeTrue("no entry is handed over before its row");
            HandedOver.Add("entry:" + ledgerId);
            return Task.CompletedTask;
        }

        public Task HandOverAsync(Guid intentPublicId, string kind, long actionLogId, CancellationToken cancellationToken)
        {
            Actions.Any(a => a.Id == actionLogId && a.Row.Kind == kind).ShouldBeTrue("no reducing action is handed over before its row");
            HandedOver.Add(kind + ":" + actionLogId);
            return Task.CompletedTask;
        }

        public ReserveEntry Reserve() => new(this, this, this, this, this, this, this, NullLogger<ReserveEntry>.Instance);

        public AutoActions Actor() => new(Reserve(), this, this, this, NullLogger<AutoActions>.Instance);

        private long Write(ActionLogEntry entry)
        {
            var id = Actions.Count + 100;
            Actions.Add((id, entry));
            return id;
        }
    }

    [Fact]
    public void The_entry_text_is_the_plain_sentence_of_the_spec()
    {
        var text = ActionLogText.Entry(LimitsScene.Entry(), "T", false, 49_482m, 150_000m, 1, 6);

        text.ShouldBe("Placed BUY 120 TATAPOWER at LIMIT 412.35 with stop 405.10 and target 425.80 because ticket T approved by you on 23 Oct was triggered at 09:46; day total Rs 49,482 of Rs 150,000, order 1 of 6");
    }

    [Fact]
    public void Money_is_whole_without_paise_and_two_decimals_with_them()
    {
        ActionLogText.Money(150_000m).ShouldBe("150,000");
        ActionLogText.Money(1_554.01m).ShouldBe("1,554.01");
        ActionLogText.Refused(LimitsScene.Entry(), "4711", [AutoLiveCodes.NotionalDay, AutoLiveCodes.FeedStale]).ShouldBe("Did not place BUY 120 TATAPOWER for ticket 4711: cap.notional_day, feed.stale");
    }

    [Fact]
    public async Task An_automatic_entry_writes_one_auto_entry_row_with_the_settings_and_limits_ids_together_with_its_ledger_row()
    {
        var scene = new Scene();

        var outcome = await scene.Actor().EntryAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct);

        outcome.Status.ShouldBe(ReserveStatus.Reserved);
        var (id, row) = scene.Actions.ShouldHaveSingleItem();
        outcome.ActionLogId.ShouldBe(id);
        (row.Kind, row.By, row.SettingsId, row.LimitsId, row.IntentPublicId).ShouldBe((ActionKinds.AutoEntry, ActionKinds.ByAuto, 7L, 11L, LimitsScene.Entry().IntentPublicId));
        row.Codes.ShouldBeEmpty();
        row.Text.ShouldBe("Placed BUY 120 TATAPOWER at LIMIT 412.35 with stop 405.10 and target 425.80 because ticket 4711 approved by you on 23 Oct was triggered at 09:46; day total Rs 49,482 of Rs 150,000, order 1 of 6");
        var ledger = scene.Ledger.ShouldHaveSingleItem();
        (ledger.Mode, ledger.Notional, ledger.RiskAtStop, ledger.LimitsId, ledger.SettingsId, ledger.Side, ledger.Session).ShouldBe(("auto_live", 49_482.00m, 870.00m, 11L, 7L, "buy", new DateOnly(2026, 10, 23)));
        scene.HandedOver.ShouldBe(["entry:1"]);
    }

    [Fact]
    public async Task A_refusal_writes_auto_refused_with_every_code_and_hands_nothing_over()
    {
        var scene = new Scene { Day = new LedgerDay(6, 1_000m) };

        var outcome = await scene.Actor().EntryAsync(LimitsScene.Entry(), LimitsScene.Facts() with { Ready = false }, Ct);

        outcome.Status.ShouldBe(ReserveStatus.Refused);
        outcome.Codes.ShouldBe([AutoLiveCodes.OrdersDay, AutoLiveCodes.NotReady]);
        var (_, row) = scene.Actions.ShouldHaveSingleItem();
        (row.Kind, row.By).ShouldBe((ActionKinds.AutoRefused, ActionKinds.ByAuto));
        row.Codes.ShouldBe([AutoLiveCodes.OrdersDay, AutoLiveCodes.NotReady]);
        scene.Ledger.ShouldBeEmpty();
        scene.HandedOver.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_database_refusal_leaves_no_entry_row_and_writes_a_refusal_with_its_code()
    {
        var scene = new Scene { DatabaseRefusal = AutoLiveCodes.NotionalDay };

        var outcome = await scene.Actor().EntryAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct);

        outcome.Codes.ShouldBe([AutoLiveCodes.NotionalDay]);
        scene.Actions.ShouldHaveSingleItem().Row.Kind.ShouldBe(ActionKinds.AutoRefused);
        scene.HandedOver.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_first_live_use_writes_its_refusal_and_is_routed_to_a_ticket_never_handed_over()
    {
        var scene = new Scene { StrategyUsed = false };

        var outcome = await scene.Actor().EntryAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct);

        outcome.RouteToTicket.ShouldBeTrue();
        scene.Actions.ShouldHaveSingleItem().Row.Codes.ShouldBe([AutoLiveCodes.StrategyFirstUse]);
        scene.HandedOver.ShouldBeEmpty();
    }

    public static TheoryData<ExecMode?, bool, bool, bool> SilentCases() => new()
    {
        { ExecMode.AutoLive, true, false, true },
        { ExecMode.AutoLive, false, false, true },
        { ExecMode.AutoLive, true, true, false },
        { ExecMode.SemiAuto, false, false, true },
        { ExecMode.Assisted, true, true, true },
        { null, true, true, true },
    };

    [Theory]
    [MemberData(nameof(SilentCases))]
    public async Task Without_auto_live_and_both_switches_on_only_the_mode_and_the_switches_are_read_and_nothing_is_written(ExecMode? mode, bool executionEnabled, bool autoLiveEnabled, bool settingsRow)
    {
        var scene = new Scene { Mode = mode, Settings = AutoLiveSettings.From(settingsRow ? 7 : null, executionEnabled, autoLiveEnabled, null) };

        var outcome = await scene.Actor().EntryAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct);

        outcome.ShouldBe(ReserveOutcome.NotAuto);
        scene.Reads.ShouldBe(["mode", "settings"]);
        scene.Actions.ShouldBeEmpty();
        scene.Ledger.ShouldBeEmpty();
        scene.HandedOver.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unreadable_settings_are_not_auto()
    {
        var scene = new Scene { Settings = null };

        (await scene.Reserve().ReserveAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct)).ShouldBe(ReserveOutcome.NotAuto);
        scene.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_semi_auto_entry_is_held_to_the_same_caps_and_written_as_a_tap_entry_without_the_auto_rules()
    {
        var scene = new Scene { Mode = ExecMode.SemiAuto, Settings = AutoLiveSettings.From(7, true, false, null), StrategyUsed = false };

        var reserved = await scene.Reserve().ReserveAsync(LimitsScene.Entry(), LimitsScene.Facts() with { Ready = false }, Ct);
        scene.Day = new LedgerDay(3, 148_446m);
        var refused = await scene.Reserve().ReserveAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct);

        reserved.Status.ShouldBe(ReserveStatus.Reserved);
        refused.Codes.ShouldBe([AutoLiveCodes.NotionalDay]);
        scene.Actions.Select(a => (a.Row.Kind, a.Row.By)).ShouldBe([(ActionKinds.TapEntry, ActionKinds.ByTap), (ActionKinds.TapRefused, ActionKinds.ByTap)]);
        scene.Ledger.ShouldHaveSingleItem().Mode.ShouldBe("semi_auto");
        scene.Reads.ShouldNotContain("owner");
    }

    [Fact]
    public async Task An_empty_sheet_refuses_every_entry_and_says_so_in_a_row()
    {
        var scene = new Scene { Row = null };

        var outcome = await scene.Actor().EntryAsync(LimitsScene.Entry(), LimitsScene.Facts(), Ct);

        outcome.Codes.ShouldContain(AutoLiveCodes.LimitsIncomplete);
        var row = scene.Actions.ShouldHaveSingleItem().Row;
        row.Codes.ShouldContain(AutoLiveCodes.LimitsIncomplete);
        row.LimitsId.ShouldBeNull();
    }

    [Theory]
    [InlineData(ActionKinds.AutoStopTighten)]
    [InlineData(ActionKinds.AutoExit)]
    [InlineData(ActionKinds.AutoCancel)]
    public async Task A_reducing_action_writes_its_kind_before_it_is_handed_over(string kind)
    {
        var scene = new Scene();

        var id = await scene.Actor().ReduceAsync(new AutoReduce(LimitsScene.Account, Guid.NewGuid(), kind, "Tightened the stop of 120 TATAPOWER to 409.00", 7, 11), Ct);

        var (rowId, row) = scene.Actions.ShouldHaveSingleItem();
        id.ShouldBe(rowId);
        (row.Kind, row.By).ShouldBe((kind, ActionKinds.ByAuto));
        scene.HandedOver.ShouldBe([kind + ":" + rowId]);
    }

    [Fact]
    public async Task No_action_reaches_the_hand_over_port_without_its_row()
    {
        var scene = new Scene { LogFails = true };
        var actor = scene.Actor();

        (await actor.ReduceAsync(new AutoReduce(LimitsScene.Account, Guid.NewGuid(), ActionKinds.AutoExit, "Exited", 7, 11), Ct)).ShouldBeNull();
        var refused = await actor.EntryAsync(LimitsScene.Entry(), LimitsScene.Facts() with { Halted = true }, Ct);
        await Should.ThrowAsync<ArgumentException>(async () => await actor.ReduceAsync(new AutoReduce(LimitsScene.Account, Guid.NewGuid(), ActionKinds.AutoEntry, "x", 7, 11), Ct));

        scene.HandedOver.ShouldBeEmpty();
        refused.ActionLogId.ShouldBeNull();
        refused.Status.ShouldBe(ReserveStatus.Refused);
    }

    [Fact]
    public async Task Over_a_random_mix_of_actions_every_hand_over_has_exactly_one_row_written_before_it()
    {
        var scene = new Scene();
        var actor = scene.Actor();
        var random = new Random(31);

        for (var i = 0; i < 60; i++)
        {
            if (random.Next(3) == 0)
            {
                await actor.EntryAsync(LimitsScene.Entry(quantity: random.Next(1, 150)) with { IntentPublicId = Guid.NewGuid() }, LimitsScene.Facts() with { OpenPositions = 0 }, Ct);
            }
            else
            {
                await actor.ReduceAsync(new AutoReduce(LimitsScene.Account, Guid.NewGuid(), random.Next(2) == 0 ? ActionKinds.AutoExit : ActionKinds.AutoCancel, "Reduced", 7, 11), Ct);
            }
        }

        scene.HandedOver.Count.ShouldBe(scene.Actions.Count(a => a.Row.Kind is not (ActionKinds.AutoRefused or ActionKinds.TapRefused)));
        scene.Ledger.Count.ShouldBeLessThanOrEqualTo(6);
    }
}
