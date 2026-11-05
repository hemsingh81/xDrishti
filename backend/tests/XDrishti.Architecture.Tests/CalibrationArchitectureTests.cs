using NetArchTest.Rules;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-10 part 10a (ADR 0214): the calibration calculators are pure Domain code, and nothing that chooses what to approve (Planning, Selection, Autonomy) can read them, so a model's calibrated probability stays advisory.
/// The enforcement of the whole influence predicate (<c>IMlInfluenceGate</c>) is part 10c.
/// </summary>
public sealed class CalibrationArchitectureTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    // by full name, since the dependency rule matches whole type names or namespaces and not prefixes
    private static readonly string[] CalibrationTypes =
    [
        typeof(PlattCalibrator).FullName!, typeof(ScoreCalibration).FullName!, typeof(CalibrationChoice).FullName!, typeof(CalibrationKind).FullName!, typeof(CalibrationAssessment).FullName!, typeof(EceInterval).FullName!, typeof(CalibrationOptions).FullName!,
    ];

    [Fact]
    public void The_calibration_calculators_are_pure_no_clock_no_io_no_system_random()
    {
        var result = Types.InAssembly(Domain).That().HaveNameMatching("^(PlattCalibrator|ScoreCalibration|CalibrationChoice|CalibrationAssessment|EceInterval|CalibrationOptions)$").Should()
            .NotHaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.Diagnostics.Debug", "System.Diagnostics.Activity", "System.TimeProvider", "System.Environment", "System.Random", "System.Security.Cryptography", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching("^(PlattCalibrator|ScoreCalibration|CalibrationChoice|CalibrationAssessment|EceInterval|CalibrationOptions)$").GetTypes().Count().ShouldBe(6);
    }

    [Fact]
    public void The_confidence_settings_do_not_depend_on_the_learning_namespace()
    {
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Confidence").Should().NotHaveDependencyOn("XDrishti.Domain.Learning").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Planning_selection_and_autonomy_name_no_calibration_type()
    {
        foreach (var ns in new[] { "XDrishti.Domain.Selection", "XDrishti.Domain.Planning", "XDrishti.Domain.Autonomy", "XDrishti.Application.Planning", "XDrishti.Application.Selection", "XDrishti.Application.Autonomy" })
        {
            var assembly = ns.StartsWith("XDrishti.Domain", StringComparison.Ordinal) ? Domain : Application;
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny(CalibrationTypes).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        // control: the rule sees a dependency where there is one
        Types.InAssembly(typeof(XDrishti.Learning.MlNet.MlNetModel).Assembly).That().HaveName("MlNetModel").Should().HaveDependencyOnAny(CalibrationTypes).GetResult().IsSuccessful.ShouldBeTrue();
    }
}
