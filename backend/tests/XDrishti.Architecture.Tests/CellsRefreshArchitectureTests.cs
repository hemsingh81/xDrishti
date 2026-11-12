using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec LC-01 (ADR 0277, criterion 10, D9): the scheduled cell-statistics refresh does not ingest shadow grades or paper results (CF-08 D6), reads stored runs only through the research-fenced <c>IRunStore</c> (never the training reader), and names no execution, approval, arming or live-trading type.
/// </summary>
public sealed class CellsRefreshArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private const string Namespace = "XDrishti.Application.Learning.CellsRefresh";

    private static readonly string[] Forbidden =
    [
        "XDrishti.Domain.Shadow", "XDrishti.Application.Shadow", "XDrishti.Application.Abstractions.Shadow",
        "XDrishti.Domain.Paper", "XDrishti.Application.Paper", "XDrishti.Application.Abstractions.Paper",
    ];

    private static readonly string[] NoOrderPath =
    [
        "XDrishti.Execution", "XDrishti.Application.Abstractions.Autonomy", "XDrishti.Application.Autonomy",
        "XDrishti.Application.Abstractions.LiveTrading", "XDrishti.Application.LiveTrading", "XDrishti.Domain.LiveTrading", "XDrishti.Domain.Autonomy",
    ];

    [Fact]
    public void The_refresh_names_no_shadow_or_paper_type()
    {
        Types.InAssembly(Application).That().ResideInNamespace(Namespace).GetTypes().ShouldNotBeEmpty();

        var result = Types.InAssembly(Application).That().ResideInNamespace(Namespace).Should().NotHaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_refresh_reads_runs_through_the_run_store_and_never_the_training_reader()
    {
        Types.InAssembly(Application).That().HaveName("RefreshCellStatsHandler").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Research.IRunStore").GetResult().IsSuccessful.ShouldBeTrue("control: the handler reads through IRunStore");

        var result = Types.InAssembly(Application).That().ResideInNamespace(Namespace).Should().NotHaveDependencyOn(typeof(ITrainingRunReader).FullName!).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_refresh_has_no_execution_approval_arming_or_live_trading_path()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace(Namespace).Should().NotHaveDependencyOnAny(NoOrderPath).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
