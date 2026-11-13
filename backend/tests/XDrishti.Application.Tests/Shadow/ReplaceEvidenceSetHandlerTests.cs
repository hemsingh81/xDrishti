using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>AM-01b (GAP-09): only the owner replaces the set, only before the epoch's first shadow write, and only with stored versions.</summary>
public sealed class ReplaceEvidenceSetHandlerTests
{
    private static readonly string A = new('a', 64);

    private readonly IEvidenceSetStore _store = Substitute.For<IEvidenceSetStore>();

    private readonly IPaperStore _paper = Substitute.For<IPaperStore>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unitOfWork = Substitute.For<IAccountUnitOfWork>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ReplaceEvidenceSetHandlerTests()
    {
        _store.ReplaceAsync(Arg.Any<NewEvidenceSet>(), Ct).Returns(true);
        _strategies.GetByHashAsync(A, Ct).Returns(new StoredVersion("s", 1, 1, A, "t", "{}", null, null, DateTimeOffset.UnixEpoch));
    }

    private ReplaceEvidenceSetHandler Handler() => new(_store, _paper, _strategies, _events, _unitOfWork, new FakeTimeProvider(), NullLogger<ReplaceEvidenceSetHandler>.Instance);

    private async Task<Result<EvidenceSetReplaced>> RunAsync(string actor = "owner:me@x.test", string reason = "the versions I would trade", string? version = null) =>
        await Handler().HandleAsync(new ReplaceEvidenceSetCommand(actor, [version ?? A], reason), Ct);

    [Fact]
    public async Task The_owner_stores_an_owner_set_under_the_epoch_and_it_is_audited()
    {
        var result = await RunAsync();

        result.IsSuccess.ShouldBeTrue();
        await _store.Received(1).ReplaceAsync(Arg.Is<NewEvidenceSet>(s => s.Mode == "owner" && s.EdgeKey == 0 && s.Actor == "owner:me@x.test" && s.Versions.Single() == A), Ct);
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.EvidenceSetReplaced));
    }

    [Theory]
    [InlineData("system:default")]
    [InlineData("owner:")]
    [InlineData("auto")]
    public async Task A_non_owner_actor_is_refused_and_nothing_is_stored(string actor)
    {
        var result = await RunAsync(actor);

        result.Error.Code.ShouldBe(ReplaceEvidenceSetHandler.OwnerOnlyCode);
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<NewEvidenceSet>(), Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_refused(string reason) => (await RunAsync(reason: reason)).Error.Code.ShouldBe(ReplaceEvidenceSetHandler.ReasonInvalidCode);

    [Fact]
    public async Task A_malformed_version_is_refused() => (await RunAsync(version: "nothex")).Error.Code.ShouldBe(ReplaceEvidenceSetHandler.VersionsInvalidCode);

    [Fact]
    public async Task A_version_that_is_not_stored_is_refused() => (await RunAsync(version: new string('c', 64))).Error.Code.ShouldBe(ReplaceEvidenceSetHandler.UnknownVersionCode);

    [Fact]
    public async Task A_frozen_epoch_refuses_the_replace_before_anything_is_stored()
    {
        _store.CurrentAsync(0, Ct).Returns(new StoredEvidenceSet(1, 0, [A], EvidenceSetRules.SetHash([A]), "auto", "system:default", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, true));

        var result = await RunAsync();

        result.Error.Code.ShouldBe(ReplaceEvidenceSetHandler.FrozenCode);
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<NewEvidenceSet>(), Ct);
    }

    [Fact]
    public async Task The_database_refusal_of_a_race_is_the_same_frozen_answer()
    {
        _store.ReplaceAsync(Arg.Any<NewEvidenceSet>(), Ct).Returns(false);

        (await RunAsync()).Error.Code.ShouldBe(ReplaceEvidenceSetHandler.FrozenCode);
    }
}
