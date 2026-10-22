using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Tests.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Lab;

/// <summary>The Lab use cases of P5-01 against fake ports: the create rules (invalid, too large, conflict, built-in, idempotent), validate, clone and the reads.</summary>
public sealed class LabHandlerTests
{
    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StrategyDescription Description(string id = "LAB_TEST") => new(id, "intraday", Hash, "{}", [Timeframe.M15], [Side.Buy, Side.Sell], []);

    private static StrategyIssue Issue(string severity, string code = "x.y") => new("buy.trigger[1]", code, "message", severity, 3, 5, 3, 9, 0, 4, null);

    private static IStrategyChecker CheckerReturning(StrategyCheck check)
    {
        var checker = Substitute.For<IStrategyChecker>();
        checker.Check(Arg.Any<string>()).Returns(check);
        return checker;
    }

    // create

    [Fact]
    public async Task Create_refuses_a_text_with_an_error_with_its_problems_and_writes_nothing()
    {
        var store = Substitute.For<IStrategyStore>();
        var handler = new CreateStrategyHandler(CheckerReturning(new StrategyCheck(null, null, [Issue("error", "strategy.lint.unknown_name")])), store, NullLogger<CreateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new CreateStrategyCommand("text"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.InvalidStrategyCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        ((IReadOnlyList<StrategyIssue>)result.Error.Payload![LabErrors.ProblemsKey]).Single().Code.ShouldBe("strategy.lint.unknown_name");
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, Ct);
    }

    [Fact]
    public async Task Create_refuses_a_text_over_the_limit_before_reading_it()
    {
        var checker = Substitute.For<IStrategyChecker>();
        var handler = new CreateStrategyHandler(checker, Substitute.For<IStrategyStore>(), NullLogger<CreateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new CreateStrategyCommand(new string('x', LabErrors.MaxTextBytes + 1)), Ct);

        result.Error.Code.ShouldBe(LabErrors.InvalidStrategyCode);
        ((IReadOnlyList<StrategyIssue>)result.Error.Payload![LabErrors.ProblemsKey]).Single().Code.ShouldBe(LabErrors.TextTooLargeCode);
        checker.DidNotReceiveWithAnyArgs().Check(default!);
    }

    [Fact]
    public async Task Create_appends_the_canonical_text_with_the_version_of_the_text_as_expected_and_never_as_a_seed()
    {
        var store = Substitute.For<IStrategyStore>();
        store.AppendAsync(Arg.Any<NewVersion>(), Ct).Returns(Result.Success(new AppendResult("LAB_TEST", 2, Hash, true)));
        store.GetVersionAsync("LAB_TEST", 2, Ct).Returns(new StoredVersion("LAB_TEST", 2, 1, Hash, "t", "{}", new string('b', 64), "n", DateTimeOffset.UnixEpoch));
        var handler = new CreateStrategyHandler(CheckerReturning(new StrategyCheck(Description(), 2, [Issue("warning")])), store, NullLogger<CreateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new CreateStrategyCommand("the text", "a note", new string('c', 64)), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new CreatedStrategy("LAB_TEST", 2, Hash, new string('b', 64), true));
        var sent = (NewVersion)store.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IStrategyStore.AppendAsync)).GetArguments()[0]!;
        sent.Seed.ShouldBeFalse("the public path cannot write a built-in");
        sent.SourceText.ShouldBe("the text");
        sent.CanonicalJson.ShouldBe("{}");
        sent.Hash.ShouldBe(Hash);
        sent.ExpectedVersion.ShouldBe(2);
        sent.Note.ShouldBe("a note");
        sent.ParentHash.ShouldBe(new string('c', 64));
        sent.Style.ShouldBe("intraday");
    }

    [Fact]
    public async Task Create_reports_the_repeat_of_the_same_content_as_not_created()
    {
        var store = Substitute.For<IStrategyStore>();
        store.AppendAsync(Arg.Any<NewVersion>(), Ct).Returns(Result.Success(new AppendResult("LAB_TEST", 1, Hash, false)));
        var handler = new CreateStrategyHandler(CheckerReturning(new StrategyCheck(Description(), null, [])), store, NullLogger<CreateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new CreateStrategyCommand("text"), Ct);

        result.Value.Created.ShouldBeFalse();
        result.Value.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Create_answers_a_built_in_with_a_conflict_not_a_forbidden()
    {
        var store = Substitute.For<IStrategyStore>();
        store.AppendAsync(Arg.Any<NewVersion>(), Ct).Returns(Result.Failure<AppendResult>(StrategyStoreErrors.BuiltInReadOnly("ORB")));
        var handler = new CreateStrategyHandler(CheckerReturning(new StrategyCheck(Description("ORB"), null, [])), store, NullLogger<CreateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new CreateStrategyCommand("text"), Ct);

        result.Error.Code.ShouldBe(StrategyStoreErrors.BuiltInReadOnlyCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Create_passes_a_version_conflict_on_as_it_is()
    {
        var store = Substitute.For<IStrategyStore>();
        store.AppendAsync(Arg.Any<NewVersion>(), Ct).Returns(Result.Failure<AppendResult>(StrategyStoreErrors.VersionExists("LAB_TEST", 5, 2)));
        var handler = new CreateStrategyHandler(CheckerReturning(new StrategyCheck(Description(), 5, [])), store, NullLogger<CreateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new CreateStrategyCommand("text"), Ct);

        result.Error.Code.ShouldBe(StrategyStoreErrors.VersionExistsCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    // validate

    [Fact]
    public async Task Validate_answers_a_valid_text_with_its_hash_sides_and_timeframes()
    {
        var handler = new ValidateStrategyHandler(CheckerReturning(new StrategyCheck(Description(), 1, [Issue("warning")])), NullLogger<ValidateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new ValidateStrategyQuery("text"), Ct);

        result.Valid.ShouldBeTrue();
        result.Hash.ShouldBe(Hash);
        result.Sides.ShouldBe(["BUY", "SELL"]);
        result.Timeframes.ShouldBe(["15m"]);
        result.Problems.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Validate_answers_an_invalid_text_with_the_problems_and_no_hash()
    {
        var handler = new ValidateStrategyHandler(CheckerReturning(new StrategyCheck(null, null, [Issue("error")])), NullLogger<ValidateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new ValidateStrategyQuery("text"), Ct);

        result.Valid.ShouldBeFalse();
        result.Hash.ShouldBeNull();
        result.Problems.Single().Severity.ShouldBe("error");
    }

    [Fact]
    public async Task Validate_answers_a_text_over_the_limit_as_a_problem_without_reading_it()
    {
        var checker = Substitute.For<IStrategyChecker>();
        var handler = new ValidateStrategyHandler(checker, NullLogger<ValidateStrategyHandler>.Instance);

        var result = await handler.HandleAsync(new ValidateStrategyQuery(new string('é', LabErrors.MaxTextBytes)), Ct);

        result.Valid.ShouldBeFalse();
        result.Problems.Single().Code.ShouldBe(LabErrors.TextTooLargeCode);
        checker.DidNotReceiveWithAnyArgs().Check(default!);
    }

    // reads and clone

    private static FakeStrategyStore StoreWith()
    {
        var store = new FakeStrategyStore();
        store.Put("MINE", 1, "text one");
        store.Put("MINE", 2, "text two");
        return store;
    }

    [Fact]
    public async Task Get_lists_the_versions_of_a_family_and_a_missing_one_is_not_found()
    {
        var handler = new GetStrategyHandler(StoreWith());

        var found = await handler.HandleAsync(new GetStrategyQuery("MINE"), Ct);
        var missing = await handler.HandleAsync(new GetStrategyQuery("NOPE"), Ct);

        found.Value.Versions.Select(v => v.Version).ShouldBe([1, 2]);
        found.Value.LatestVersion.ShouldBe(2);
        missing.Error.Code.ShouldBe(LabErrors.NotFoundCode);
        missing.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task GetVersion_returns_the_text_as_stored_with_the_flag_of_today_and_a_missing_version_is_not_found()
    {
        var handler = new GetStrategyVersionHandler(StoreWith(), CheckerReturning(new StrategyCheck(null, null, [Issue("error")])));

        var found = await handler.HandleAsync(new GetStrategyVersionQuery("MINE", 1), Ct);
        var missing = await handler.HandleAsync(new GetStrategyVersionQuery("MINE", 9), Ct);

        found.Value.Text.ShouldBe("text one");
        found.Value.ProblemsFree.ShouldBeFalse();
        missing.Error.Code.ShouldBe(LabErrors.NotFoundCode);
    }

    [Fact]
    public async Task Clone_keeps_the_id_of_an_ordinary_family_and_names_the_parent_hash()
    {
        var checker = Substitute.For<IStrategyChecker>();
        checker.Retarget("text two", "MINE").Returns("cloned text");
        var handler = new CloneStrategyHandler(StoreWith(), checker);

        var result = await handler.HandleAsync(new CloneStrategyQuery("MINE", 2), Ct);

        result.Value.ShouldBe(new ClonedStrategy("MINE", "cloned text", ("MINE2").PadRight(64, 'f')[..64], "MINE", 2));
    }

    [Fact]
    public async Task Clone_gives_a_new_id_when_asked_and_refuses_an_id_that_cannot_be_stored()
    {
        var checker = Substitute.For<IStrategyChecker>();
        checker.Retarget("text one", "OTHER_1").Returns("cloned");
        var handler = new CloneStrategyHandler(StoreWith(), checker);

        var named = await handler.HandleAsync(new CloneStrategyQuery("MINE", 1, "OTHER_1"), Ct);
        var bad = await handler.HandleAsync(new CloneStrategyQuery("MINE", 1, "1 bad id"), Ct);
        var missing = await handler.HandleAsync(new CloneStrategyQuery("MINE", 7), Ct);

        named.Value.Id.ShouldBe("OTHER_1");
        bad.Error.Kind.ShouldBe(ErrorKind.Validation);
        missing.Error.Code.ShouldBe(LabErrors.NotFoundCode);
    }
}
