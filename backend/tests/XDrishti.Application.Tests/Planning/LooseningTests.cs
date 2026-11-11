#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>BK-10b over fakes: which loosenings a plan shows, the insert-only dismissal, the automatic option (OFF by default, recorded as auto with the setting), and that the plan itself is never changed by the banner.</summary>
public sealed class LooseningTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 6, 0, 0, TimeSpan.Zero);

    private static readonly Guid BasketId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ILooseningStore _store = Substitute.For<ILooseningStore>();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unit = Substitute.For<IAccountUnitOfWork>();

    private readonly FakeTimeProvider _time = new(T0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LooseningNotice Sizing(long id, DateTimeOffset at) => new(LooseningNoticeId.Sizing, id, "sizing.vol_target.floor", null, "0.5", "0.7", "owner:me@x", at);

    private static LooseningNotice Budget(long id, DateTimeOffset at) => new(LooseningNoticeId.Budget, id, "basket budget Momentum", BasketId, "20%", "off", "owner:me@x", at);

    private static LooseningNotice Basket(long id, DateTimeOffset at) => new(LooseningNoticeId.Basket, id, "gate.breadth_source", null, "tracked", "basket:abc", "owner:me@x", at);

    private static PlanView View(DateTimeOffset createdAt) =>
        PlanView.Of(new PlanRow(1, new NewPlan("k", new DateOnly(2026, 11, 11), createdAt, 1, new string('b', 64), "cell statistics only", true, "test", createdAt, []), PlanState.Published, 0, 0, 0), [], []);

    private LooseningReader Reader() => new(_store, NullLogger<LooseningReader>.Instance);

    [Fact]
    public async Task Every_loosening_shows_oldest_first_with_its_sentence_and_a_later_one_waits_for_the_next_plan()
    {
        var late = Sizing(9, T0.AddHours(5));
        _store.ListUndismissedAsync(Ct).Returns([Basket(4, T0.AddHours(2)), Sizing(7, T0.AddHours(1)), Budget(3, T0.AddHours(3)), late]);
        var (notices, readable) = await Reader().ReadAsync(Ct);

        var plan = LooseningReader.With(View(T0.AddHours(4)), notices, readable);
        var next = LooseningReader.With(View(T0.AddHours(6)), notices, readable);

        plan.Loosenings.Select(l => l.NoticeId).ShouldBe(["sizing:7", "basket:4", "budget:3"]);
        plan.Loosenings[0].Sentence.ShouldBe("Loosened since 2026-11-10: sizing.vol_target.floor 0.5 → 0.7 (by owner:me@x).");
        plan.Loosenings[2].BasketId.ShouldBe(BasketId);
        plan.LooseningsUnavailable.ShouldBeFalse();
        next.Loosenings.Select(l => l.NoticeId).ShouldBe(["sizing:7", "basket:4", "budget:3", "sizing:9"]);
    }

    [Fact]
    public async Task The_banner_never_changes_the_plan_it_is_read_with()
    {
        _store.ListUndismissedAsync(Ct).Returns([Sizing(7, T0.AddHours(1))]);
        var (notices, readable) = await Reader().ReadAsync(Ct);
        var bare = View(T0.AddHours(4));

        var shown = LooseningReader.With(bare, notices, readable);

        shown.Loosenings.Count.ShouldBe(1);
        (shown with { Loosenings = [] }).ShouldBe(bare);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_says_so_and_the_plan_is_still_returned()
    {
        _store.ListUndismissedAsync(Ct).Returns<IReadOnlyList<LooseningNotice>>(_ => throw new InvalidOperationException("down"));
        var (notices, readable) = await Reader().ReadAsync(Ct);

        var plan = LooseningReader.With(View(T0), notices, readable);

        plan.LooseningsUnavailable.ShouldBeTrue();
        plan.Loosenings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("sizing:7", true)]
    [InlineData("budget:12", true)]
    [InlineData("basket:1", true)]
    [InlineData("sizing:0", false)]
    [InlineData("sizing:-3", false)]
    [InlineData("sizing:1:2", false)]
    [InlineData("sizing:x", false)]
    [InlineData("crossing:7", false)]
    [InlineData("7", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_notice_id_is_one_of_three_forms_with_a_positive_whole_id(string? text, bool valid) => LooseningNoticeId.Parse(text).HasValue.ShouldBe(valid);

    private DismissLooseningHandler Dismiss() => new(_store, _events, _unit, _time, NullLogger<DismissLooseningHandler>.Instance);

    [Fact]
    public async Task Dismissing_adds_one_owner_row_and_one_event_and_a_repeat_adds_nothing()
    {
        _store.FindAsync("sizing", 7, Ct).Returns(new LooseningLookup(Sizing(7, T0), "loosen"));
        _store.AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct).Returns(true, false);

        var first = await Dismiss().HandleAsync(new DismissLooseningCommand("sizing:7", "owner:me@x"), Ct);
        var again = await Dismiss().HandleAsync(new DismissLooseningCommand("sizing:7", "owner:me@x"), Ct);

        first.IsSuccess.ShouldBeTrue();
        again.IsSuccess.ShouldBeTrue();
        await _store.Received(2).AddDismissalAsync(Arg.Is<NewLooseningDismissal>(d => d.Actor == "owner:me@x" && d.SettingId == null && d.SettingHash == null), Ct);
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.LooseningDismissed && e.Actor == "owner:me@x" && e.Detail!.Contains("sizing:7", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("nonsense", "plans.invalid")]
    [InlineData("budget:99", "plans.not_found")]
    [InlineData("basket:5", "plans.invalid")]
    public async Task A_malformed_unknown_or_non_loosening_notice_is_refused_and_nothing_is_written(string noticeId, string code)
    {
        _store.FindAsync("basket", 5, Ct).Returns(new LooseningLookup(Basket(5, T0), "tighten"));

        var result = await Dismiss().HandleAsync(new DismissLooseningCommand(noticeId, "owner:me@x"), Ct);

        result.Error.Code.ShouldBe(code);
        await _store.DidNotReceive().AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct);
    }

    [Fact]
    public async Task Only_an_owner_actor_dismisses()
    {
        var result = await Dismiss().HandleAsync(new DismissLooseningCommand("sizing:7", "auto"), Ct);

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceive().AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct);
    }

    private LooseningAutoDismissal Auto() => new(_store, _events, _unit, _time, NullLogger<LooseningAutoDismissal>.Instance);

    [Fact]
    public async Task With_the_setting_ON_only_what_an_earlier_plan_showed_is_dismissed_as_auto_with_the_setting_row()
    {
        var previousPlan = T0.AddHours(2);
        _store.CurrentSettingAsync(Ct).Returns(new LooseningSwitch(5, true, "hash5"));
        _store.PreviousPlanCreatedAtAsync(20, Ct).Returns(previousPlan);
        _store.ListUndismissedAsync(Ct).Returns([Sizing(7, T0.AddHours(1)), Budget(3, T0.AddHours(3))]);
        _store.AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct).Returns(true);

        var count = await Auto().RunAsync(20, Ct);

        count.ShouldBe(1);
        await _store.Received(1).AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct);
        await _store.Received(1).AddDismissalAsync(new NewLooseningDismissal("sizing", 7, "auto", 5, "hash5"), Ct);
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Actor == "auto" && e.Event == AccountEventType.LooseningDismissed));
    }

    [Fact]
    public async Task With_the_setting_OFF_or_never_stored_nothing_is_dismissed_and_nothing_else_is_read()
    {
        _store.CurrentSettingAsync(Ct).Returns(new LooseningSwitch(null, false, null), new LooseningSwitch(6, false, "h"));

        (await Auto().RunAsync(20, Ct)).ShouldBe(0);
        (await Auto().RunAsync(20, Ct)).ShouldBe(0);

        await _store.DidNotReceive().ListUndismissedAsync(Ct);
        await _store.DidNotReceive().AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct);
    }

    [Fact]
    public async Task The_first_plan_ever_dismisses_nothing_because_no_plan_has_shown_anything()
    {
        _store.CurrentSettingAsync(Ct).Returns(new LooseningSwitch(5, true, "h"));
        _store.PreviousPlanCreatedAtAsync(1, Ct).Returns((DateTimeOffset?)null);
        _store.ListUndismissedAsync(Ct).Returns([Sizing(7, T0)]);

        (await Auto().RunAsync(1, Ct)).ShouldBe(0);

        await _store.DidNotReceive().AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct);
    }

    [Fact]
    public async Task A_failing_automatic_dismissal_is_logged_and_leaves_the_banner_shown()
    {
        _store.CurrentSettingAsync(Ct).Returns(new LooseningSwitch(5, true, "h"));
        _store.PreviousPlanCreatedAtAsync(20, Ct).Returns(T0);
        _store.ListUndismissedAsync(Ct).Returns([Sizing(7, T0)]);
        _store.AddDismissalAsync(Arg.Any<NewLooseningDismissal>(), Ct).Returns<bool>(_ => throw new InvalidOperationException("refused"));

        (await Auto().RunAsync(20, Ct)).ShouldBe(0);
    }

    [Fact]
    public void The_pure_rule_hides_nothing_without_the_setting_and_never_a_loosening_no_plan_showed()
    {
        var undismissed = new[] { Sizing(7, T0), Sizing(9, T0.AddHours(5)) };

        LooseningNotices.AutoDismissible(undismissed, false, T0.AddHours(9)).ShouldBeEmpty();
        LooseningNotices.AutoDismissible(undismissed, true, null).ShouldBeEmpty();
        LooseningNotices.AutoDismissible(undismissed, true, T0.AddHours(1)).Select(l => l.Id).ShouldBe([7]);
    }

    [Fact]
    public async Task The_setting_change_is_an_owner_act_that_needs_the_version_read_and_appends_a_row_with_a_hash()
    {
        var handler = new ChangeLooseningSettingsHandler(_store, _events, _unit, _time, NullLogger<ChangeLooseningSettingsHandler>.Instance);
        _store.AppendSettingAsync(true, "owner:me@x", Arg.Any<string>(), 0, Ct).Returns(true);
        _store.AppendSettingAsync(true, "owner:me@x", Arg.Any<string>(), 3, Ct).Returns(false);
        _store.CurrentSettingAsync(Ct).Returns(new LooseningSwitch(1, true, "h"));
        _store.SettingHistoryAsync(Arg.Any<int>(), Ct).Returns([new LooseningSettingRow(1, true, "owner:me@x", T0, "h")]);

        var ok = await handler.HandleAsync(new ChangeLooseningSettingsCommand("owner:me@x", true, 0), Ct);
        var stale = await handler.HandleAsync(new ChangeLooseningSettingsCommand("owner:me@x", true, 3), Ct);
        var notOwner = await handler.HandleAsync(new ChangeLooseningSettingsCommand("auto", true, 0), Ct);

        ok.Value.AutoDismiss.ShouldBeTrue();
        ok.Value.Version.ShouldBe(1);
        stale.Error.Code.ShouldBe(PlanErrors.StaleCode);
        notOwner.IsFailure.ShouldBeTrue();
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.LooseningSettingsChanged));
    }

    [Fact]
    public async Task With_no_setting_row_the_setting_reads_OFF()
    {
        _store.CurrentSettingAsync(Ct).Returns(new LooseningSwitch(null, false, null));
        _store.SettingHistoryAsync(Arg.Any<int>(), Ct).Returns([]);

        var view = await new GetLooseningSettingsHandler(_store).HandleAsync(new GetLooseningSettingsQuery(), Ct);

        view.AutoDismiss.ShouldBeFalse();
        view.Version.ShouldBe(0);
    }
}
