using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P11-05 (ADR 0147): the runbook is in the repository with its questions complete, and the compliance code is a record, not a client. Criterion 1 (every question and check has an owner column, a why-line and a place for the answer; every external fact has a source and the
/// date read or is marked UNVERIFIED) and the structural guards: no HTTP client, broker port or order word anywhere in the compliance code; the one writer is the one endpoint after the gate and the fresh password; the route has no order segment.
/// </summary>
public sealed class ComplianceArchitectureTests
{
    private static readonly string[] CodeFiles =
    [
        "XDrishti.Domain/LiveTrading/ComplianceKinds.cs", "XDrishti.Domain/LiveTrading/ComplianceRecord.cs", "XDrishti.Domain/LiveTrading/ComplianceScreens.cs",
        "XDrishti.Application/LiveTrading/GetComplianceHandler.cs", "XDrishti.Application/LiveTrading/RecordComplianceHandler.cs", "XDrishti.Application/LiveTrading/ComplianceRules.cs",
        "XDrishti.Infrastructure/LiveTrading/ComplianceStore.cs", "XDrishti.Api/Endpoints/LiveTradingEndpoints.cs", "XDrishti.Api/Cli/LiveComplianceCommand.cs",
    ];

    private static string Runbook() => File.ReadAllText(RepositoryFiles.RepositoryFile(["docs", "research", "p11-static-ip-and-sebi-runbook.md"]));

    private static string Source(string relative) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. relative.Split('/')]));

    private static List<string[]> Rows(string text, string heading, string idPattern)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, heading);
        var end = text.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];
        return [.. section.Split('\n').Where(l => Regex.IsMatch(l, @"^\| " + idPattern + @" \|", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Select(l => l.Trim().Trim('|').Split(" | ").Select(c => c.Trim()).ToArray())];
    }

    [Fact]
    public void The_runbook_file_exists_and_every_question_and_check_has_the_five_columns_with_the_first_four_filled()
    {
        var text = Runbook();
        var dhan = Rows(text, "### 3.", @"D\d");
        var isp = Rows(text, "### 4.", @"I\d");
        var checks = Rows(text, "### 5.", @"C\d");

        dhan.Select(r => r[0]).ShouldBe(["D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8"]);
        isp.Select(r => r[0]).ShouldBe(["I1", "I2", "I3", "I4", "I5", "I6", "I7"]);
        checks.Select(r => r[0]).ShouldBe(["C1", "C2", "C3", "C4", "C5"]);
        foreach (var row in dhan.Concat(isp).Concat(checks))
        {
            row.Length.ShouldBe(5, row[0] + ": id, question, who answers, why it matters, answer");
            row.Take(4).ShouldAllBe(c => c.Length > 0, row[0] + " has an empty cell");
        }

        text.ShouldContain("| # | Question (send as written) | Who answers | Why it matters | Answer · date · source |");
    }

    [Fact]
    public void Every_external_fact_has_a_source_and_the_date_it_was_read_or_is_marked_UNVERIFIED()
    {
        var text = Runbook();
        var start = text.IndexOf("### 2.", StringComparison.Ordinal);
        var end = text.IndexOf("### 3.", StringComparison.Ordinal);
        var facts = text[start..end].Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Fact", StringComparison.Ordinal) && !l.StartsWith("|---", StringComparison.Ordinal)).ToList();

        facts.Count.ShouldBeGreaterThanOrEqualTo(10);
        foreach (var fact in facts)
        {
            var cells = fact.Trim().Trim('|').Split(" | ").Select(c => c.Trim()).ToArray();
            cells.Length.ShouldBe(3, fact);
            Regex.IsMatch(cells[1], @"\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeTrue("the date it was read: " + fact[..40]);
            (cells[1].Contains("https://", StringComparison.Ordinal) || cells[2].Contains("UNVERIFIED", StringComparison.Ordinal)).ShouldBeTrue("a source URL or UNVERIFIED: " + fact[..40]);
        }

        text.ShouldContain("UNVERIFIED");
    }

    [Fact]
    public void The_runbook_holds_no_address_client_id_or_token()
    {
        var text = Runbook();

        Regex.IsMatch(text, @"(?<![\d.])(?!100\.64\.0\.0)(\d{1,3}\.){3}\d{1,3}(?![\d.])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeFalse("an IPv4 address");
        Regex.IsMatch(text, @"(?<![A-Za-z0-9])\d{7,12}(?![A-Za-z0-9])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeFalse("a client-id-like number");
    }

    [Fact]
    public void The_compliance_code_makes_no_call_outside_the_database_and_names_no_order_action()
    {
        foreach (var file in CodeFiles)
        {
            var text = Source(file);
            foreach (var forbidden in new[] { "HttpClient", "IHttpClientFactory", "IBrokerConnector", "IBrokerClient", "System.Net.Http", "Process.Start", "IDhan", "DhanClient" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }

            Regex.IsMatch(text, @"(?i)\b\w*(placeorder|submitorder|cancelorder|modifyorder|neworder)\w*\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_one_writer_asks_the_gate_then_the_password_then_the_handler_and_the_route_has_no_order_segment()
    {
        var endpoints = Source("XDrishti.Api/Endpoints/LiveTradingEndpoints.cs");

        Regex.Count(endpoints, @"\.Map(Post|Put|Delete|Patch)\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(1);
        Regex.Count(endpoints, @"\.MapPost\(""/compliance""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(1);
        endpoints.ShouldContain("RequireRateLimiting(");
        var gate = endpoints.IndexOf("gate.Enabled", StringComparison.Ordinal);
        var password = endpoints.IndexOf("OwnerAct.AuthorizeAsync(", StringComparison.Ordinal);
        var handler = endpoints.IndexOf("handler.HandleAsync(\n            new RecordComplianceCommand", StringComparison.Ordinal);
        gate.ShouldBeGreaterThan(0);
        password.ShouldBeGreaterThan(gate);
        handler.ShouldBeGreaterThan(password);
        endpoints.ShouldNotContain("SessionOwner(");
        endpoints.ShouldNotContain("Actor =");
        Regex.IsMatch(endpoints, @"MapGroup\(""[^""]*(order|execute|square)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void The_store_only_reads_and_inserts_and_the_migration_is_append_only_with_a_down()
    {
        var store = Source("XDrishti.Infrastructure/LiveTrading/ComplianceStore.cs");
        var migration = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile(["backend", "src", "XDrishti.Infrastructure", "Persistence", "Schemas.cs"]))!, "Migrations"), "*_AddComplianceRecords.cs").Single();
        var text = File.ReadAllText(migration);

        store.ShouldNotContain("UPDATE ");
        store.ShouldNotContain("DELETE ");
        store.ShouldNotContain("TRUNCATE");
        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Trading, \"compliance_records\")");
        text.ShouldContain("DROP TABLE IF EXISTS trading.compliance_records");
    }
}
