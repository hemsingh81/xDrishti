#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>BK-11 (Q5): the universe settings are the owner's, with a fresh password for every change; a refused change stores nothing; each changed key is one row with its hash.</summary>
public sealed class UniverseSettingsHandlersTests
{
    private const string Owner = "owner:hem";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IUniverseSettingsStore _store = Substitute.For<IUniverseSettingsStore>();

    private IReadOnlyList<UniverseSettingChange> _appended = [];

    private static ICommandHandler<StepUpCommand, StepUpResult> Password(bool ok)
    {
        var password = Substitute.For<ICommandHandler<StepUpCommand, StepUpResult>>();
        password.HandleAsync(Arg.Any<StepUpCommand>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(ok ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials)));
        return password;
    }

    private ChangeUniverseSettingsHandler Handler(bool passwordOk = true)
    {
        _store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new UniverseSettingsState(UniverseSettings.Defaults, []));
        _store.AppendAsync(Arg.Do<IReadOnlyList<UniverseSettingChange>>(c => _appended = c), Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        return new ChangeUniverseSettingsHandler(_store, Password(passwordOk), Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, NullLogger<ChangeUniverseSettingsHandler>.Instance);
    }

    private static ChangeUniverseSettingsCommand Command(string actor, int? child = null, int? link = null, string? baskets = null) =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), child, link, baskets, "pw");

    [Theory]
    [InlineData("system:nightly")]
    [InlineData("auto:x")]
    [InlineData("")]
    public async Task A_non_owner_is_refused_before_the_password_and_the_store(string actor)
    {
        var result = await Handler().HandleAsync(Command(actor, child: 40), Ct);

        result.Error.Code.ShouldBe(ChangeUniverseSettingsHandler.OwnerOnlyCode);
        await _store.DidNotReceive().AppendAsync(Arg.Any<IReadOnlyList<UniverseSettingChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_password_stores_nothing()
    {
        var result = await Handler(passwordOk: false).HandleAsync(Command(Owner, child: 40), Ct);

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceive().AppendAsync(Arg.Any<IReadOnlyList<UniverseSettingChange>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(null, 9, null)]
    [InlineData(null, null, " , ")]
    public async Task A_value_out_of_range_or_an_empty_list_is_refused_and_stores_nothing(int? child, int? link, string? baskets)
    {
        var result = await Handler().HandleAsync(Command(Owner, child, link, baskets), Ct);

        result.Error.Code.ShouldBe(ChangeUniverseSettingsHandler.InvalidCode);
        await _store.DidNotReceive().AppendAsync(Arg.Any<IReadOnlyList<UniverseSettingChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_changed_key_is_one_row_with_old_and_new_value_and_a_value_that_stays_is_not_a_row()
    {
        var result = await Handler().HandleAsync(Command(Owner, child: 40, link: 100, baskets: "Nifty 50, Nifty 100,nifty 50"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _appended.Select(c => (c.Key, c.OldValue, c.NewValue)).ShouldBe(
        [
            (UniverseSettingsRules.MinTradesChildKey, "30", "40"),
            (UniverseSettingsRules.RegisteredBasketsKey, UniverseSettings.Defaults.RegisteredText, "Nifty 50,Nifty 100"),
        ]);
        _appended.ShouldAllBe(c => c.Actor == Owner && c.Hash.Length == 64);
    }
}
