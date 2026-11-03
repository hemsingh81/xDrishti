#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Market.Eod;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>CF-04 step 0: the probe fetches only while the switch is on and prints header lines only.</summary>
public sealed class EodProbeHandlerTests
{
    private static readonly DateOnly[] Dates = [new(2024, 7, 5)];

    private readonly IEodSettings _settings = Substitute.For<IEodSettings>();

    private readonly IEodSource _source = Substitute.For<IEodSource>();

    private readonly IEodRawFiles _files = Substitute.For<IEodRawFiles>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EodProbeHandler Handler(bool enabled)
    {
        _settings.Enabled.Returns(enabled);
        _settings.Delay.Returns(TimeSpan.Zero);
        _files.SaveAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Ct).Returns(ValueTask.FromResult("data/history-probe/file"));
        return new EodProbeHandler(_settings, _source, _files, new FakeTimeProvider());
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_fetched_or_saved()
    {
        var result = await Handler(false).HandleAsync(new EodProbeCommand(Dates), Ct);

        result.Error.Code.ShouldBe("history.disabled");
        await _source.DidNotReceiveWithAnyArgs().FetchAsync(default, default, Ct);
        await _files.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task The_raw_files_are_saved_and_only_the_header_lines_and_the_layout_are_reported()
    {
        _source.FetchAsync(EodFileKind.Bhavcopy, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.Ok(EodFixtures.Legacy(EodFixtures.LegacyRow("SECRETROW", "EQ"))));
        _source.FetchAsync(EodFileKind.Delivery, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.Ok(EodFixtures.Delivery("SECRETROW,EQ,10,5,50.00")));

        var result = await Handler(true).HandleAsync(new EodProbeCommand(Dates), Ct);

        var text = string.Join('\n', result.Value.Lines);
        text.ShouldContain("layout: known (legacy)");
        text.ShouldContain("layout: known (mto)");
        text.ShouldContain(EodFixtures.LegacyHeader);
        text.ShouldNotContain("SECRETROW");
        await _files.Received(2).SaveAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Ct);
    }

    [Fact]
    public async Task A_refused_endpoint_is_reported_as_the_owner_act()
    {
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.NotAllowed("not on the allow-list"));

        var result = await Handler(true).HandleAsync(new EodProbeCommand(Dates), Ct);

        result.Error.Code.ShouldBe("history.not_allowed");
    }
}
