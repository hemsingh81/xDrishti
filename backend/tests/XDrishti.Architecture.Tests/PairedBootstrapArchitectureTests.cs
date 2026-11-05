using NetArchTest.Rules;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-10 part 10c-1 (ADR 0216): the paired bootstrap, the fold sign test and the uniqueness weights are pure Domain code, and nothing that chooses what to approve (Planning, Selection, Autonomy) or judges a model (the verdict types) can read them.
/// The enforcement of the whole influence predicate (<c>IMlInfluenceGate</c>) is part 10c-2.
/// </summary>
public sealed class PairedBootstrapArchitectureTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private const string PureNames = "^(PairedDayBootstrap|PairedBootstrapResult|PairedReading|RungComparison|FoldSignTest|SignTestResult|UniquenessWeights|UniquenessResult|PairedBootstrapOptions)$";

    // by full name, since the dependency rule matches whole type names or namespaces and not prefixes
    private static readonly string[] PairedTypes =
    [
        typeof(PairedDayBootstrap).FullName!, typeof(PairedBootstrapResult).FullName!, typeof(PairedReading).FullName!, typeof(RungComparison).FullName!, typeof(FoldSignTest).FullName!, typeof(SignTestResult).FullName!,
        typeof(UniquenessWeights).FullName!, typeof(UniquenessResult).FullName!, typeof(PairedBootstrapOptions).FullName!,
    ];

    [Fact]
    public void The_paired_calculators_are_pure_no_clock_no_io_no_system_random()
    {
        var result = Types.InAssembly(Domain).That().HaveNameMatching(PureNames).Should()
            .NotHaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.Diagnostics.Debug", "System.Diagnostics.Activity", "System.TimeProvider", "System.Environment", "System.Random", "System.Security.Cryptography", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching(PureNames).GetTypes().Count().ShouldBe(PairedTypes.Length);
    }

    [Fact]
    public void Planning_selection_and_autonomy_name_no_paired_type()
    {
        foreach (var ns in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Autonomy", "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Autonomy" })
        {
            var assembly = ns.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Application;
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny(PairedTypes).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        // control: the rule sees a dependency where there is one
        Types.InAssembly(Domain).That().HaveName("ComplexityEvidence").Should().HaveDependencyOnAny(PairedTypes).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_in_the_validation_verdict_reads_the_paired_evidence()
    {
        var result = Types.InAssembly(Domain).That().HaveNameMatching("^(EligibilityRule|ChampionRule|CandidateVerdict|ValidationOutcome)$").Should().NotHaveDependencyOnAny(PairedTypes).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching("^(EligibilityRule|ChampionRule|CandidateVerdict|ValidationOutcome)$").GetTypes().Count().ShouldBe(4);
    }

    [Fact]
    public void The_trainer_port_and_the_scoring_ports_do_not_read_the_paired_evidence()
    {
        // evidence only: the model scorers and the trainer produce probabilities and never read how likely they are to beat a baseline
        const string Scorers = "^(IRowScorer|IScorerFactory|IExplainableScorer|BaseRateScorer|CellRateScorer|LogisticScorer)$";
        var result = Types.InAssembly(Domain).That().HaveNameMatching(Scorers).Should().NotHaveDependencyOnAny(PairedTypes).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching(Scorers).GetTypes().Count().ShouldBe(6);
    }
}
