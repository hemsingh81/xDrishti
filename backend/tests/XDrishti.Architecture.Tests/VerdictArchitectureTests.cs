using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>The verdict evaluator and its types (P5-03, criterion 8) are pure: no clock, no I/O, no framework, no randomness.</summary>
public sealed class VerdictArchitectureTests
{
    private static readonly string[] Forbidden =
    [
        "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.TimeProvider", "System.Random", "System.Environment", "Microsoft.Extensions", "Microsoft.EntityFrameworkCore", "Npgsql", "Serilog",
    ];

    [Fact]
    public void The_evaluator_and_its_types_depend_on_no_io_clock_or_framework()
    {
        var result = Types.InAssembly(typeof(XDrishti.Domain.Lab.VerdictEvaluator).Assembly).That().ResideInNamespace("XDrishti.Domain.Lab")
            .And().HaveNameMatching("^(VerdictEvaluator|Verdict|VerdictOutcome|GateResult|OosEvidence|QualificationGates|MultipleTesting)$")
            .ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_evaluator_is_found_by_the_scan()
    {
        Types.InAssembly(typeof(XDrishti.Domain.Lab.VerdictEvaluator).Assembly).That().HaveName("VerdictEvaluator").GetTypes().ShouldHaveSingleItem();
    }
}
