#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Tests.Lab;

/// <summary>AU-11 criteria 2 and 3 (ADR 0243): a training window always ends before the hold-out block start less the embargo, by the Lab's own policy, off or on of the owner's Enforce switch; with no block it does not start.</summary>
public sealed class TrainingWindowResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly MemoryHoldOutStore _store = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

    private TrainingWindowResolver Resolver(HoldOutOptions options, DateTimeOffset? first, DateTimeOffset? last)
    {
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(2, 1000, first, last, 0, 0));
        return new TrainingWindowResolver(new LabHoldOutGuard(_store, _bars, options, _time), options);
    }

    private static DateTimeOffset At(int y, int m, int d) => new(y, m, d, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_omitted_end_is_the_day_before_the_embargo_before_the_block_whatever_the_enforce_switch_says(bool enforce)
    {
        var resolver = Resolver(new HoldOutOptions(Enforce: enforce), At(2024, 10, 1), At(2026, 10, 30));

        var window = await resolver.ResolveAsync(null, null, Ct);

        window.IsSuccess.ShouldBeTrue();
        var block = _store.Blocks.Single();
        window.Value.HoldOutStart.ShouldBe(block.Start);
        window.Value.To.ShouldBe(block.Start.AddDays(-1 - 30));
        window.Value.From.ShouldBe(new DateOnly(2024, 10, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_end_that_reaches_the_embargo_or_the_block_is_refused_even_when_enforce_is_off(bool enforce)
    {
        var resolver = Resolver(new HoldOutOptions(Enforce: enforce), At(2024, 10, 1), At(2026, 10, 30));
        await resolver.ResolveAsync(null, null, Ct); // fixes the block
        var start = _store.Blocks.Single().Start;

        foreach (var end in new[] { start.AddDays(-30), start, start.AddDays(30) })
        {
            var result = await resolver.ResolveAsync(null, end, Ct);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(LabErrors.HoldOutTouchedCode);
        }

        (await resolver.ResolveAsync(null, start.AddDays(-31), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_start_inside_the_hold_out_is_refused_and_a_start_after_the_end_is_invalid()
    {
        var resolver = Resolver(new HoldOutOptions(), At(2024, 10, 1), At(2026, 10, 30));
        await resolver.ResolveAsync(null, null, Ct);
        var start = _store.Blocks.Single().Start;

        (await resolver.ResolveAsync(start, null, Ct)).Error.Code.ShouldBe(LabErrors.HoldOutTouchedCode);
        (await resolver.ResolveAsync(new DateOnly(2025, 6, 1), new DateOnly(2025, 5, 1), Ct)).Error.Code.ShouldBe(LabErrors.InvalidInputCode);
    }

    [Fact]
    public async Task With_no_stored_history_the_training_run_does_not_start()
    {
        var result = await Resolver(new HoldOutOptions(), null, null).ResolveAsync(null, null, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _store.Blocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_a_history_too_short_to_keep_a_block_the_training_run_does_not_start()
    {
        var result = await Resolver(new HoldOutOptions(), At(2026, 9, 1), At(2026, 10, 30)).ResolveAsync(null, null, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
    }

    [Theory]
    [InlineData(27, true)]
    [InlineData(28, false)]
    [InlineData(180, false)]
    [InlineData(181, true)]
    public void The_embargo_setting_is_validated_seven_to_ninety_days(int days, bool problem) =>
        new HoldOutOptions(TrainingEmbargoDays: days).Problems().Any(p => p.Contains("TrainingEmbargoDays", StringComparison.Ordinal)).ShouldBe(problem);
}
