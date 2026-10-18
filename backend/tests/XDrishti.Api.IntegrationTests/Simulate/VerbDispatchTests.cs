using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-04b2 criterion 1: only the first argument selects the verb; the older steps are selected as before; a symbol list never selects another step.</summary>
public sealed class VerbDispatchTests
{
    public static TheoryData<string[], string> Cases => new()
    {
        { ["simulate", "--strategy", "ORB"], "Simulate" },
        { ["simulate"], "Simulate" },
        { ["migrate"], "Migrate" },
        { ["owner-reset"], "OwnerReset" },
        { ["--healthcheck"], "HealthCheck" },
        { [], "Serve" },
        { ["simulate", "--healthcheck"], "Simulate" },
        { ["simulate", "--symbols", "migrate"], "Simulate" },
        { ["simulate", "--basket", "owner-reset"], "Simulate" },
        { ["x", "simulate"], "Serve" },
        { ["--urls", "http://localhost:1"], "Serve" },
        { ["migrate", "--healthcheck"], "HealthCheck" },
        { ["Simulate"], "Serve" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Only_the_first_argument_selects_the_simulate_verb_and_the_other_steps_are_selected_as_before(string[] args, string expected)
    {
        VerbDispatch.Of(args).ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_simulate_verb_with_a_stray_healthcheck_flag_is_a_usage_error_not_the_probe()
    {
        var parsed = SimulateOptions.Parse(["--healthcheck"]);

        parsed.IsValid.ShouldBeFalse();
        parsed.Errors.ShouldContain(e => e.Contains("--healthcheck", StringComparison.Ordinal));
    }
}
