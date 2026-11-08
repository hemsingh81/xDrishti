using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-08 review fix 4 (ADR 0232): the newest digest of a verified chain is copied outside the database, once, and only while the switch is ON.</summary>
public sealed class ExportShadowDigestHandlerTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private readonly IShadowSettings _switch = Substitute.For<IShadowSettings>();

    private readonly IShadowDigestSink _sink = Substitute.For<IShadowDigestSink>();

    private readonly IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView> _verify = Substitute.For<IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ExportShadowDigestHandlerTests()
    {
        _switch.IsEnabledAsync(Ct).Returns(true);
        _sink.AppendAsync(Arg.Any<ShadowDigestLine>(), Ct).Returns(true);
        _sink.CheckWritableAsync(Ct).Returns((string?)null);
        Chain(new ShadowChainCheckView(1, 0, true, null, Monday, Digest, 1));
    }

    private static readonly string Digest = new('d', 64);

    private void Chain(ShadowChainCheckView view) => _verify.HandleAsync(Arg.Any<VerifyShadowChainQuery>(), Ct).Returns(view);

    private ExportShadowDigestHandler Handler() => new(_switch, _verify, _sink, _time);

    [Fact]
    public async Task An_intact_chain_appends_its_newest_digest_with_the_iso_week_and_counts()
    {
        var result = await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct);

        result.Value.ShouldSatisfyAllConditions(v => v.Exported.ShouldBeTrue(), v => v.Written.ShouldBeTrue());
        await _sink.Received(1).AppendAsync(new ShadowDigestLine("2026-W45", Monday, Digest, 1, 1, _time.GetUtcNow()), Ct);
    }

    [Fact]
    public async Task The_same_head_already_in_the_log_is_reported_as_not_written()
    {
        _sink.AppendAsync(Arg.Any<ShadowDigestLine>(), Ct).Returns(false);

        var result = await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct);

        result.Value.ShouldSatisfyAllConditions(v => v.Exported.ShouldBeTrue(), v => v.Written.ShouldBeFalse());
    }

    [Fact]
    public async Task A_broken_chain_is_not_copied_and_the_run_fails()
    {
        Chain(new ShadowChainCheckView(1, 0, false, Monday, Monday, Digest, 1));

        var result = await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct);

        result.Error.Code.ShouldBe(ExportShadowDigestHandler.BrokenCode);
        await _sink.DidNotReceive().AppendAsync(Arg.Any<ShadowDigestLine>(), Ct);
    }

    [Fact]
    public async Task With_the_switch_off_or_an_empty_chain_nothing_is_exported()
    {
        Chain(new ShadowChainCheckView(0, 0, true, null, null, null));
        (await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct)).Value.Exported.ShouldBeFalse();
        Chain(new ShadowChainCheckView(1, 0, true, null, Monday, Digest, 1));
        _switch.IsEnabledAsync(Ct).Returns(false);

        (await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct)).Value.Exported.ShouldBeFalse();
        await _sink.DidNotReceive().AppendAsync(Arg.Any<ShadowDigestLine>(), Ct);
    }

    [Fact]
    public async Task A_log_that_disagrees_with_the_chain_fails_the_run_and_writes_nothing()
    {
        Chain(new ShadowChainCheckView(1, 0, true, null, Monday, Digest, 1, null, "The digest of the session 2026-11-02 in the log differs from the stored one."));

        var result = await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct);

        result.Error.Code.ShouldBe(ExportShadowDigestHandler.DivergedCode);
        await _sink.DidNotReceive().AppendAsync(Arg.Any<ShadowDigestLine>(), Ct);
    }

    [Fact]
    public async Task An_unwritable_folder_fails_the_run_visibly()
    {
        _sink.CheckWritableAsync(Ct).Returns("cannot be written");

        var result = await Handler().HandleAsync(new ExportShadowDigestCommand(), Ct);

        result.Error.Code.ShouldBe(ExportShadowDigestHandler.UnwritableCode);
        await _sink.DidNotReceive().AppendAsync(Arg.Any<ShadowDigestLine>(), Ct);
    }
}
