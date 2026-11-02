using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>P15-07 verbs: <c>xd eval announcements sample|import|show</c> parse their arguments, call the use cases, print lines (never text), and map a refusal to exit code 1.</summary>
public sealed class AnnouncementsVerbTests : IDisposable
{
    private readonly Fake<SampleAnnouncementsCommand> _sample = new();

    private readonly Fake<ImportAnnouncementLabelsCommand> _import = new();

    private readonly ShowFake _show = new();

    private readonly ServiceProvider _services;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xd-ann-verb-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AnnouncementsVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _services = new ServiceCollection()
            .AddSingleton<ICommandHandler<SampleAnnouncementsCommand, AnnouncementEvalReport>>(_sample)
            .AddSingleton<ICommandHandler<ImportAnnouncementLabelsCommand, AnnouncementEvalReport>>(_import)
            .AddSingleton<IQueryHandler<ShowAnnouncementsQuery, AnnouncementEvalReport>>(_show)
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_dir, true);
    }

    private async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await EvalCommand.RunAsync(["announcements", .. args], _services, new SimulateConsole(output, error), Ct);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Sample_passes_the_range_and_the_default_seed_1337_and_prints_the_lines()
    {
        var (exit, output, _) = await RunAsync("sample", "--from", "2024-01-01", "--to", "2026-06-30");

        exit.ShouldBe(0);
        output.ShouldContain("done");
        var command = _sample.Received.ShouldHaveSingleItem();
        (command.From, command.To, command.Seed).ShouldBe((new DateOnly(2024, 1, 1), new DateOnly(2026, 6, 30), 1337));
    }

    [Fact]
    public async Task Sample_takes_a_seed_and_import_reads_the_named_file()
    {
        var file = Path.Combine(_dir, "labels.csv");
        await File.WriteAllTextAsync(file, "id\n", Ct);

        var sampled = await RunAsync("sample", "--from", "2024-01-01", "--to", "2024-12-31", "--seed", "7");
        var imported = await RunAsync("import", file);

        (sampled.Exit, imported.Exit).ShouldBe((0, 0));
        _sample.Received.ShouldHaveSingleItem().Seed.ShouldBe(7);
        _import.Received.ShouldHaveSingleItem().LabelFileText.ShouldBe("id\n");
    }

    [Fact]
    public async Task Show_prints_the_report()
    {
        var (exit, output, _) = await RunAsync("show");

        exit.ShouldBe(0);
        output.ShouldContain("waiting for labels");
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("sample")]
    [InlineData("sample --from 2024-01-01")]
    [InlineData("sample --from yesterday --to 2024-01-01")]
    [InlineData("sample --from 2024-01-01 --to 2024-12-31 --seed minus")]
    [InlineData("sample --from 2024-01-01 --to 2024-12-31 --extra")]
    [InlineData("import")]
    [InlineData("show now")]
    public async Task A_wrong_command_line_prints_the_usage_and_exits_2_without_calling_anything(string line)
    {
        var (exit, _, error) = await RunAsync(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        exit.ShouldBe(2);
        error.ShouldContain("usage:");
        _sample.Received.ShouldBeEmpty();
        _import.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refusal_is_printed_with_its_code_and_exits_1_and_a_missing_file_exits_2()
    {
        _import.Answer = Result.Failure<AnnouncementEvalReport>(Error.Validation("eval.too_few_labels", "a041 is unsure and no reserve row is left"));
        var file = Path.Combine(_dir, "labels.csv");
        await File.WriteAllTextAsync(file, "id\n", Ct);

        var refused = await RunAsync("import", file);
        var missing = await RunAsync("import", Path.Combine(_dir, "nope.csv"));

        refused.Exit.ShouldBe(1);
        refused.Err.ShouldContain("eval.too_few_labels");
        missing.Exit.ShouldBe(2);
    }

    private sealed class Fake<TCommand> : ICommandHandler<TCommand, AnnouncementEvalReport>
        where TCommand : ICommand
    {
        public List<TCommand> Received { get; } = [];

        public Result<AnnouncementEvalReport> Answer { get; set; } = Result.Success(new AnnouncementEvalReport(true, ["done"]));

        public ValueTask<Result<AnnouncementEvalReport>> HandleAsync(TCommand command, CancellationToken cancellationToken)
        {
            Received.Add(command);
            return ValueTask.FromResult(Answer);
        }
    }

    private sealed class ShowFake : IQueryHandler<ShowAnnouncementsQuery, AnnouncementEvalReport>
    {
        public ValueTask<AnnouncementEvalReport> HandleAsync(ShowAnnouncementsQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(new AnnouncementEvalReport(false, ["waiting for labels"]));
    }
}
