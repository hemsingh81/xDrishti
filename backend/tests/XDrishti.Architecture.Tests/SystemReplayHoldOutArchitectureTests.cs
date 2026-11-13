namespace XDrishti.Architecture.Tests;

/// <summary>
/// SR-03 (ADR 0284), fences. The block reader exists only for the runner and only from a claim; the three tables are named by one store (so no evidence reader can read the result); the code names no execution, broker or order type. Source pins are text pins: they stop
/// the obvious, and a new reference needs this test updated in review.
/// </summary>
public sealed class SystemReplayHoldOutArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f));

    private static IEnumerable<string> Naming(string text) => Sources().Where(f => File.ReadAllText(f).Contains(text, StringComparison.Ordinal)).Select(Path.GetFileName).Order(StringComparer.Ordinal).Select(n => n!);

    [Fact]
    public void Only_the_runner_builds_the_block_reader_and_only_from_a_claim_and_only_the_clamp_and_the_facts_hold_it()
    {
        Naming("HoldOutBlockReader").ShouldBe(["ClampedBars.cs", "HoldOutBlockReader.cs", "HoldOutClaim.cs", "HoldOutSystemRunner.cs", "ReplayFacts.cs"]);
        Naming("new HoldOutBlockReader(").ShouldBe(["HoldOutSystemRunner.cs"]);
        Naming("HoldOutClaim").ShouldBe(["HoldOutBlockReader.cs", "HoldOutClaim.cs", "HoldOutSystemRunner.cs"]);
        Naming("UseBlock(").ShouldBe(["HoldOutSystemRunner.cs", "ReplayFacts.cs"]);
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "HoldOut", "HoldOutBlockReader.cs")).ShouldContain("internal HoldOutBlockReader(HoldOutClaim claim", Case.Sensitive);
    }

    [Fact]
    public void The_claim_is_made_only_from_a_stored_system_look_and_the_runner_makes_it_that_way()
    {
        var claim = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "HoldOut", "HoldOutClaim.cs"));
        claim.ShouldContain("private HoldOutClaim(");
        claim.ShouldContain("HoldOutHorizon.SystemKind");
        claim.ShouldContain("look.ReusedWindow");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "HoldOut", "HoldOutSystemRunner.cs")).ShouldContain("HoldOutClaim.From(look, registration.Id)");
    }

    [Theory]
    [InlineData("replay_holdout_registrations")]
    [InlineData("replay_holdout_results")]
    [InlineData("replay_holdout_settings")]
    public void No_code_outside_the_registration_store_names_the_tables_so_no_evidence_reader_reads_the_result(string table)
    {
        var offenders = Sources()
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Migrations" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && Path.GetFileName(f) != "HoldOutRegistrationStore.cs")
            .Where(f => File.ReadAllText(f).Contains(table, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
        File.ReadAllText(Sources().Single(f => Path.GetFileName(f) == "HoldOutRegistrationStore.cs")).ShouldContain("research." + table);
    }

    [Fact]
    public void The_system_replay_code_references_no_execution_broker_or_order_type()
    {
        var folders = new[] { Path.Combine("XDrishti.Application", "Replay", "HoldOut"), Path.Combine("XDrishti.Domain", "Replay", "HoldOut") };
        var files = folders.SelectMany(d => Directory.GetFiles(Path.Combine(Src, d), "*.cs")).Append(Path.Combine(Src, "XDrishti.Infrastructure", "HoldOutReplay", "HoldOutRegistrationStore.cs")).Append(Path.Combine(Src, "XDrishti.Api", "Endpoints", "HoldOutRegistrationEndpoints.cs")).ToList();

        files.Count.ShouldBeGreaterThan(18);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "XDrishti.Execution", "XDrishti.Application.Execution", "Abstractions.Brokers", "XDrishti.Infrastructure.Brokers", "PlaceOrder", "IBrokerOrder", "xd_exec" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }
}
