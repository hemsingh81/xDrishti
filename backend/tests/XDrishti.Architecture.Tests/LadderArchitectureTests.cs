using NetArchTest.Rules;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-10 part 10b (ADR 0215): the ladder, events-per-variable and feature stability calculators are pure Domain code, and nothing that chooses what to approve (Planning, Selection, Autonomy) can read them.
/// The enforcement of the whole influence predicate (<c>IMlInfluenceGate</c>) is part 10c.
/// </summary>
public sealed class LadderArchitectureTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private const string PureNames = "^(EventsPerVariable|ComplexityLadder|LadderAssessment|LadderRung|Psi|PsiLevel|FeatureStability|StabilityVerdict|FeatureStabilityRow|FeatureShift|CandidateStability|ComplexityReport|ComplexityEvidence|LadderOptions)$";

    // by full name, since the dependency rule matches whole type names or namespaces and not prefixes
    private static readonly string[] LadderTypes =
    [
        typeof(EventsPerVariable).FullName!, typeof(ComplexityLadder).FullName!, typeof(LadderAssessment).FullName!, typeof(LadderRung).FullName!, typeof(Psi).FullName!, typeof(PsiLevel).FullName!, typeof(FeatureStability).FullName!,
        typeof(StabilityVerdict).FullName!, typeof(FeatureStabilityRow).FullName!, typeof(FeatureShift).FullName!, typeof(CandidateStability).FullName!, typeof(ComplexityReport).FullName!, "XDrishti.Domain.Learning.ComplexityEvidence", typeof(LadderOptions).FullName!,
    ];

    [Fact]
    public void The_ladder_calculators_are_pure_no_clock_no_io_no_random()
    {
        var result = Types.InAssembly(Domain).That().HaveNameMatching(PureNames).Should()
            .NotHaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.Diagnostics.Debug", "System.Diagnostics.Activity", "System.TimeProvider", "System.Environment", "System.Random", "System.Security.Cryptography", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching(PureNames).GetTypes().Count().ShouldBe(LadderTypes.Length);
    }

    [Fact]
    public void Planning_selection_and_autonomy_name_no_ladder_type()
    {
        foreach (var ns in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Autonomy", "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Autonomy" })
        {
            var assembly = ns.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Application;
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny(LadderTypes).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        // control: the rule sees a dependency where there is one
        Types.InAssembly(Domain).That().HaveName("WalkForwardValidator").Should().HaveDependencyOnAny(LadderTypes).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_in_the_validation_verdict_reads_the_ladder_evidence()
    {
        var result = Types.InAssembly(Domain).That().HaveNameMatching("^(EligibilityRule|ChampionRule|CandidateVerdict|ValidationOutcome)$").Should().NotHaveDependencyOnAny(LadderTypes).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching("^(EligibilityRule|ChampionRule|CandidateVerdict|ValidationOutcome)$").GetTypes().Count().ShouldBe(4);
    }
}
