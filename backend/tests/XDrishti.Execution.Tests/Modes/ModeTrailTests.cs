using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Domain.Execution;
using XDrishti.Execution.Application.Modes;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Modes;

/// <summary>P12-07 on fakes: the source reads only the rungs that exist (an unknown name reads paper, an unreadable record gives no answer), no mode of this slice opens the account gate, the guard only lowers, and nothing is composed.</summary>
public sealed class ModeTrailTests
{
    private static readonly Guid Account = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Trail(ModeRecord? record) : IModeTrail
    {
        public List<(LadderMode From, LadderMode To, string Code)> Written { get; } = [];

        public Task<ModeRecord?> ReadAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(record);

        public Task<bool> DemoteAsync(Guid accountId, LadderMode from, LadderMode to, string code, CancellationToken cancellationToken)
        {
            Written.Add((from, to, code));
            return Task.FromResult(true);
        }
    }

    [Theory]
    [InlineData("paper", ExecMode.Paper)]
    [InlineData("manual_live", ExecMode.ManualLive)]
    [InlineData("assisted", ExecMode.Assisted)]
    [InlineData("semi_auto", ExecMode.Paper)] // not a rung of this build: the safer side
    [InlineData("auto_live", ExecMode.Paper)]
    [InlineData("garbage", ExecMode.Paper)]
    public async Task The_source_maps_only_the_rungs_that_exist_and_reads_anything_else_as_paper(string stored, ExecMode expected)
    {
        var answer = await new ModeTrailSource(new Trail(new ModeRecord(stored, true))).AnswerAsync(Account, Ct);

        answer!.EffectiveMode.ShouldBe(expected);
        answer.ExecutionEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unreadable_record_gives_no_answer_and_so_a_closed_gate()
    {
        var answer = await new ModeTrailSource(new Trail(null)).AnswerAsync(Account, Ct);

        answer.ShouldBeNull();
        AccountGate.MayAct(answer, ExecEnvironment.Sandbox).Open.ShouldBeFalse();
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("manual_live")]
    [InlineData("assisted")]
    [InlineData("semi_auto")]
    public async Task No_mode_this_build_can_store_opens_the_account_gate_for_an_entry_even_with_the_switch_on_in_the_sandbox(string stored)
    {
        var answer = await new ModeTrailSource(new Trail(new ModeRecord(stored, true))).AnswerAsync(Account, Ct);

        AccountGate.MayAct(answer, ExecEnvironment.Sandbox).Open.ShouldBeFalse();
        AccountGate.MayAct(answer, ExecEnvironment.Live).Open.ShouldBeFalse();
    }

    [Theory]
    [InlineData("assisted", false, false, "role_off")]
    [InlineData("manual_live", true, true, "archived")]
    [InlineData("assisted", null, false, "role_unreadable")]
    public async Task The_guard_writes_one_lower_row_when_the_policy_says_so(string stored, bool? role, bool? archived, string code)
    {
        var trail = new Trail(new ModeRecord(stored, true));

        var decision = await new ModeGuard(trail).RunAsync(Account, new DemotionFacts(role, archived), Ct);

        decision!.Code.ShouldBe(code);
        trail.Written.Single().ShouldBe((ExecutionModes.TryParse(stored)!.Value, LadderMode.Paper, code));
    }

    [Theory]
    [InlineData("assisted", true, false)]
    [InlineData("paper", false, true)] // already at the bottom
    [InlineData("semi_auto", false, false)] // not a rung: the guard leaves what it cannot name
    public async Task The_guard_writes_nothing_when_the_facts_are_good_the_account_is_at_the_bottom_or_the_mode_is_not_a_rung(string stored, bool role, bool archived)
    {
        var trail = new Trail(new ModeRecord(stored, true));

        (await new ModeGuard(trail).RunAsync(Account, new DemotionFacts(role, archived), Ct)).ShouldBeNull();
        (await new ModeGuard(new Trail(null)).RunAsync(Account, new DemotionFacts(false, true), Ct)).ShouldBeNull();
        trail.Written.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("sandbox", "true")]
    [InlineData("live", "true")]
    public void No_environment_composes_the_trail_source_the_guard_or_the_trail_and_the_mode_source_stays_closed(string? environment, string? sendEnabled)
    {
        var values = new Dictionary<string, string?>();
        if (environment is not null)
        {
            values["Execution:Environment"] = environment;
        }

        if (sendEnabled is not null)
        {
            values["Execution:SendEnabled"] = sendEnabled;
        }

        using var provider = new ServiceCollection().AddLogging().AddExecution(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

        provider.GetService<ModeTrailSource>().ShouldBeNull();
        provider.GetService<ModeGuard>().ShouldBeNull();
        provider.GetService<IModeTrail>().ShouldBeNull();
        provider.GetService<IModeSource>()!.GetType().Name.ShouldBe("ClosedModeSource");
        provider.GetService<IBrokerOrders>().ShouldBeNull();
    }

    [Fact]
    public void The_host_source_names_none_of_the_mode_pieces_and_the_mode_record_code_names_no_broker_port()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Execution"));
        var host = File.ReadAllText(Path.Combine(root, "Host", "ExecServices.cs"));

        host.ShouldContain("services.AddSingleton<IModeSource, ClosedModeSource>()");
        host.ShouldNotContain("ModeTrailSource");
        host.ShouldNotContain("ModeGuard");
        host.ShouldNotContain("PgModeTrail");
        foreach (var file in new[] { Path.Combine(root, "Application", "Modes", "ModeTrail.cs"), Path.Combine(root, "Persistence", "PgModeTrail.cs") })
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("IBrokerOrders");
            text.ShouldNotContain("LegSender");
            text.ShouldNotContain("HttpClient");
        }
    }
}
