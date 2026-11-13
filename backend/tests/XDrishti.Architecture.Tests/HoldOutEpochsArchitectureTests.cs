namespace XDrishti.Architecture.Tests;

/// <summary>
/// HO-01 (ADR 0283), fences: the block and release tables are named by one class, <c>HoldOutStore</c> (the EF mapping, its record and the migrations only describe the tables), and the epoch code references no execution, broker or order type. Source pins are text pins:
/// they stop the obvious, and a new reference needs this test updated in review.
/// </summary>
public sealed class HoldOutEpochsArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f));

    private static readonly string[] Describing = ["HoldOutStore.cs", "LabRegistrationConfigurations.cs", "LabRegistrationRecords.cs"];

    [Theory]
    [InlineData("holdout_blocks")]
    [InlineData("holdout_releases")]
    public void No_code_outside_the_hold_out_store_names_the_block_or_release_table(string table)
    {
        var offenders = Sources()
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Migrations" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Describing.Contains(Path.GetFileName(f)))
            .Where(f => File.ReadAllText(f).Contains(table, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
        File.ReadAllText(Sources().Single(f => Path.GetFileName(f) == "HoldOutStore.cs")).ShouldContain("lab." + table);
    }

    [Fact]
    public void The_epoch_code_references_no_execution_broker_or_order_type()
    {
        var files = Sources().Where(f =>
        {
            var name = Path.GetFileName(f);
            return name is "HoldOutChain.cs" or "HoldOutBlockState.cs" or "HoldOutAutoRelease.cs" or "LookAvailability.cs" or "SeenHoldOut.cs" or "HoldOutEpochs.cs" or "HoldOutReleaser.cs" or "ReleaseHoldOutHandler.cs" or "IHoldOutLookSlot.cs" or "BlockViews.cs" or "HoldOutStore.cs";
        }).ToList();

        files.Count.ShouldBe(11);
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
