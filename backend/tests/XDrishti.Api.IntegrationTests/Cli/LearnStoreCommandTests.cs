using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>Spec P8-13 criterion 9: the store branch of <c>xd learn windows --store</c> and <c>xd learn policy --store</c> prints the stored or already-stored line and maps a refusal to exit 1. Handlers are fakes; no database.</summary>
public sealed class LearnStoreCommandTests
{
    private static readonly string[] Args = ["--run", $"{new string('a', 64)}:intraday:X", "--store"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fake<TCommand>(Func<Result<StoredReport>> answer) : ICommandHandler<TCommand, StoredReport>
        where TCommand : ICommand
    {
        public int Calls { get; private set; }

        public ValueTask<Result<StoredReport>> HandleAsync(TCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(answer());
        }
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(Func<IReadOnlyList<string>, IServiceProvider, SimulateConsole, CancellationToken, Task<int>> verb, IServiceProvider provider)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await verb(Args, provider, new SimulateConsole(output, error), Ct);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Windows_prints_stored_then_already_stored_and_a_refusal_exits_1()
    {
        var created = true;
        var fake = new Fake<StoreEntryWindowsCommand>(() => Result.Success(new StoredReport(7, created, "text\n")));
        using var provider = new ServiceCollection().AddSingleton<ICommandHandler<StoreEntryWindowsCommand, StoredReport>>(fake).BuildServiceProvider();

        var first = await RunAsync(LearnWindowsCommand.RunAsync, provider);
        created = false;
        var again = await RunAsync(LearnWindowsCommand.RunAsync, provider);
        var refused = new Fake<StoreEntryWindowsCommand>(() => Result.Failure<StoredReport>(LearningErrors.Invalid([new FieldError("sources", "bad")])));
        using var refusing = new ServiceCollection().AddSingleton<ICommandHandler<StoreEntryWindowsCommand, StoredReport>>(refused).BuildServiceProvider();
        var no = await RunAsync(LearnWindowsCommand.RunAsync, refusing);

        (first.Code, first.Out).ShouldBe((XdCommand.Done, "text\nstored as #7" + Environment.NewLine));
        (again.Code, again.Out).ShouldBe((XdCommand.Done, "text\nalready stored as #7 (same inputs)" + Environment.NewLine));
        no.Code.ShouldBe(XdCommand.Refused);
        no.Error.ShouldContain("learning.invalid");
    }

    [Fact]
    public async Task Policy_prints_stored_then_already_stored_and_a_refusal_exits_1()
    {
        var created = true;
        var fake = new Fake<StoreMetaPolicyCommand>(() => Result.Success(new StoredReport(3, created, "text\n")));
        using var provider = new ServiceCollection().AddSingleton<ICommandHandler<StoreMetaPolicyCommand, StoredReport>>(fake).BuildServiceProvider();

        var first = await RunAsync(LearnPolicyCommand.RunAsync, provider);
        created = false;
        var again = await RunAsync(LearnPolicyCommand.RunAsync, provider);
        var refused = new Fake<StoreMetaPolicyCommand>(() => Result.Failure<StoredReport>(LearningErrors.Invalid([new FieldError("sources", "bad")])));
        using var refusing = new ServiceCollection().AddSingleton<ICommandHandler<StoreMetaPolicyCommand, StoredReport>>(refused).BuildServiceProvider();
        var no = await RunAsync(LearnPolicyCommand.RunAsync, refusing);

        first.Out.ShouldEndWith("stored as #3" + Environment.NewLine);
        again.Out.ShouldEndWith("already stored as #3 (same inputs)" + Environment.NewLine);
        (first.Code, again.Code, no.Code).ShouldBe((XdCommand.Done, XdCommand.Done, XdCommand.Refused));
    }
}
