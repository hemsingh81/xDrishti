using System.Diagnostics;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-10 AC 10: <c>scripts/hermes-poc.sh record</c> refuses an ADOPT_OPTIONAL verdict while any gating check is not PASS and follows the decision rule (any FAIL gives REJECT, any other gap gives DEFER). Only the record subcommand runs: no container, no network.</summary>
public sealed class HermesRecordScriptTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hermes-poc-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static string Script()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "scripts", "hermes-poc.sh");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("scripts/hermes-poc.sh");
    }

    private static string Digest() => System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Script())!, "..", "deploy", "hermes", "Dockerfile")), "@sha256:([0-9a-f]{64})").Groups[1].Value;

    private (int Exit, string Record) Record(IReadOnlyDictionary<int, string> results, params string[] args)
    {
        var day = Path.Combine(_dir, "2026-11-01");
        Directory.CreateDirectory(day);
        File.WriteAllLines(Path.Combine(day, "results.tsv"), results.Select(r => $"H-{r.Key}\t{r.Value}\t\tnote\t{Digest()}"));
        var output = Path.Combine(_dir, "record.md");
        File.Delete(output);
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Script());
        start.ArgumentList.Add("record");
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }

        start.Environment["HERMES_POC_DIR"] = _dir;
        start.Environment["HERMES_RECORD_OUT"] = output;
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, File.Exists(output) ? File.ReadAllText(output) : string.Empty);
    }

    private static Dictionary<int, string> AllPass() => Enumerable.Range(1, 12).ToDictionary(i => i, _ => "PASS");

    [Fact]
    public void Adopt_is_accepted_only_when_every_check_passed()
    {
        var (exit, record) = Record(AllPass(), "--verdict", "ADOPT_OPTIONAL", "--owner", "Owner");

        exit.ShouldBe(0);
        record.ShouldContain("ADOPT_OPTIONAL (rule suggests: ADOPT_OPTIONAL)");
        record.ShouldContain("| H-12 |");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Adopt_is_refused_when_one_check_is_inconclusive_failed_or_missing(int check)
    {
        foreach (var state in new[] { "INCONCLUSIVE", "FAIL", "MISSING" })
        {
            var results = AllPass();
            if (state == "MISSING")
            {
                results.Remove(check);
            }
            else
            {
                results[check] = state;
            }

            var (exit, record) = Record(results, "--verdict", "ADOPT_OPTIONAL", "--owner", "Owner");

            exit.ShouldBe(3, $"H-{check} {state}");
            record.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Without_a_verdict_the_record_names_the_pending_state_and_the_rule_result_and_marks_gaps_not_measured()
    {
        var results = AllPass();
        results[10] = "INCONCLUSIVE";
        results.Remove(12);

        var (exit, record) = Record(results);

        exit.ShouldBe(0);
        record.ShouldContain("PENDING (rule suggests: DEFER)");
        record.ShouldContain("not measured");
    }

    [Fact]
    public void A_fail_gives_reject_and_defer_is_refused_over_a_fail()
    {
        var results = AllPass();
        results[4] = "FAIL";

        Record(results).Record.ShouldContain("rule suggests: REJECT");
        Record(results, "--verdict", "DEFER", "--owner", "Owner").Exit.ShouldBe(3);
        Record(results, "--verdict", "REJECT", "--owner", "Owner").Exit.ShouldBe(0);
    }

    [Fact]
    public void A_signed_verdict_needs_a_named_owner()
    {
        Record(AllPass(), "--verdict", "REJECT").Exit.ShouldBe(2);
    }

    [Fact]
    public void Rows_made_against_another_digest_are_ignored_and_a_manual_pass_of_the_automated_checks_is_refused()
    {
        var day = Path.Combine(_dir, "2026-11-01");
        Directory.CreateDirectory(day);
        File.WriteAllLines(Path.Combine(day, "results.tsv"), Enumerable.Range(1, 12).Select(i => $"H-{i}\tPASS\t\tnote\t{new string('f', 64)}"));
        var output = Path.Combine(_dir, "stale.md");
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { Script(), "record", "--verdict", "ADOPT_OPTIONAL", "--owner", "Owner" })
        {
            start.ArgumentList.Add(a);
        }

        start.Environment["HERMES_POC_DIR"] = _dir;
        start.Environment["HERMES_RECORD_OUT"] = output;
        using (var process = Process.Start(start)!)
        {
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.ShouldBe(3);
        }

        foreach (var id in new[] { "H-3", "H-4" })
        {
            var mark = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { Script(), "mark", id, "PASS" })
            {
                mark.ArgumentList.Add(a);
            }

            mark.Environment["HERMES_POC_DIR"] = _dir;
            using var process = Process.Start(mark)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.ShouldBe(3, id);
        }
    }
}
