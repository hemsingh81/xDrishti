using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The owner-act forms (spec P8-19, ADR 0130) add endpoints and a gate, no logic: the five learning handlers are unchanged (their source hashes are pinned here as a reminder), exactly one endpoint module calls them from the web, the autonomy command is built only by the
/// autonomy endpoint, every write goes through the one flow (capability, then password, then the runner), no body names the actor, a state of a lesson or a number of the evidence, and the hypothesis move is always supervised. Each scan has a sentinel.
/// </summary>
public sealed partial class OwnerMovesArchitectureTests
{
    private static readonly Dictionary<string, string> PinnedHandlers = new()
    {
        ["LinkLessonHandler"] = "91da8bc2a3a35559",
        ["CloseLessonHandler"] = "4ce82b8eefdf94ef",
        ["DismissLessonHandler"] = "d0308363c316e068",
        ["MoveHypothesisHandler"] = "a596adb2d53896b8",
        ["RevertChangelogHandler"] = "711ad93879b13024",
    };

    private static readonly string[] MoveCommands = ["LinkLessonCommand", "CloseLessonCommand", "DismissLessonCommand", "MoveHypothesisCommand", "RevertChangelogCommand"];

    private static readonly string[] FlowOrder = ["RefusedWhileOffAsync(", "OwnerAct.AuthorizeAsync(", "command(owner.Value)", "runner.RunAsync("];

    private static readonly string[] BodyTypes = ["OwnerMoveRequest", "LinkLessonRequest", "MoveHypothesisRequest", "ChangeAutonomyRequest"];

    private static readonly Assembly Api = typeof(Program).Assembly;

    [GeneratedRegex(@"\.Map(Post|Put|Delete|Patch)\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Writes();

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r", string.Empty, StringComparison.Ordinal))))[..16];

    private static List<string> EndpointFilesNaming(string pattern)
    {
        var dir = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "OwnerMovesEndpoints.cs"))!;
        return [.. Directory.GetFiles(dir, "*.cs").Where(f => Regex.IsMatch(File.ReadAllText(f), pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Select(Path.GetFileName).Cast<string>().Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void The_five_learning_handlers_are_the_ones_the_slice_found()
    {
        foreach (var (name, hash) in PinnedHandlers)
        {
            Hash(Source("XDrishti.Application", "Learning", name + ".cs")).ShouldBe(hash, name + " changed: this slice adds no logic to a move; update the pin only with an ADR");
        }

        Hash("a").ShouldNotBe(Hash("b"), "the pin sees a changed text");
        Hash("a\r\n").ShouldBe(Hash("a\n"));
    }

    [Fact]
    public void Only_the_owner_moves_module_names_the_five_commands_and_only_the_autonomy_module_names_the_autonomy_change()
    {
        EndpointFilesNaming(@"\b(" + string.Join('|', MoveCommands) + @")\b").ShouldBe(["OwnerMovesEndpoints.cs"]);
        EndpointFilesNaming(@"\bChangeAutonomySettingsCommand\b").ShouldBe(["AutonomyEndpoints.cs"]);
        EndpointFilesNaming(@"\bIOwnerActRunner\b").ShouldBe(["AutonomyEndpoints.cs", "OwnerActFlow.cs", "OwnerMovesEndpoints.cs"]);
        Regex.IsMatch("x LinkLessonCommand y", @"\b(" + string.Join('|', MoveCommands) + @")\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeTrue("the scan sees a use");
    }

    [Fact]
    public void Every_write_of_the_two_modules_goes_through_the_one_flow_after_a_capability_and_a_password()
    {
        var moves = Source("XDrishti.Api", "Endpoints", "OwnerMovesEndpoints.cs");
        var autonomy = Source("XDrishti.Api", "Endpoints", "AutonomyEndpoints.cs");

        Writes().Count(moves).ShouldBe(5);
        Regex.Count(moves, @"OwnerActFlow\.RunAsync\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(5);
        Regex.Count(moves, @"RequireRateLimiting\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(5);
        Writes().Count(autonomy).ShouldBe(1);
        autonomy.ShouldContain("OwnerActFlow.RunAsync(");
        autonomy.ShouldContain("Capabilities.AutonomyForm");
        moves.ShouldContain("Cap = Domain.Capabilities.Capabilities.LearningMovesUi");
        moves.ShouldNotContain("AutonomyForm");
        moves.ShouldNotContain("ExperimentsUi");
        Writes().IsMatch("group.MapPost(\"/x\", Handler)").ShouldBeTrue("the scan sees a write");

        var flow = Source("XDrishti.Api", "Endpoints", "OwnerActFlow.cs");
        var order = FlowOrder.Select(t => flow.IndexOf(t, StringComparison.Ordinal)).ToList();
        order.ShouldAllBe(i => i > 0);
        order.ShouldBe([.. order.Order()], "capability, password, command, runner, in that order");
        flow.ShouldNotContain("SessionOwner(", Case.Sensitive, "no act of the flow is without a fresh password");
        Source("XDrishti.Api", "Endpoints", "OwnerAct.cs").ShouldContain("Elevate: false");
    }

    [Fact]
    public void No_body_names_the_actor_a_lesson_state_an_autonomy_level_or_a_number_of_the_evidence_and_the_hypothesis_move_is_supervised()
    {
        var names = BodyTypes
            .SelectMany(n => Api.GetType("XDrishti.Api.Endpoints." + n)!.GetProperties().Select(p => n + "." + p.Name)).ToList();

        names.Count.ShouldBeGreaterThan(10);
        names.ShouldNotContain(n => Regex.IsMatch(n, @"\.(Actor|Owner|Autonomy|Level|Result|BeforeMeanR|AfterMeanR|BeforeTotalR|AfterTotalR|State|ExperimentId|Outcome)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        Api.GetType("XDrishti.Api.Endpoints.OwnerMoveRequest")!.GetProperties().Select(p => p.Name).Order().ShouldBe(["Password", "Reason"]);
        var moves = Source("XDrishti.Api", "Endpoints", "OwnerMovesEndpoints.cs");
        moves.ShouldContain("AutonomyLevel.Supervised");
        moves.ShouldNotContain("AutonomyLevel.Autonomous");
        moves.ShouldNotContain("AutonomyLevel.Auto");
    }

    [Fact]
    public void The_runner_calls_the_handler_it_is_given_and_nothing_that_applies_a_change()
    {
        var runner = Source("XDrishti.Application", "OwnerActs", "OwnerActRunner.cs");

        runner.ShouldContain("gate.RequireAsync(");
        runner.ShouldContain("handler.HandleAsync(");
        Regex.IsMatch(runner, @"\b(Apply|Activate|Promote|Publish|Place|Order)\w*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeFalse();
        runner.ShouldNotContain("ICapabilityLogWriter");
    }
}
