using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-03 criteria 3 and 9 (ADR 0184): the parser reads untrusted XML only through the one hardened factory, no other XML or network path exists in the shipped source, the result-facts port has no decision caller, and the migration grants the two new tables insert-only to the
/// runtime role and to nobody else.
/// </summary>
public sealed partial class ResultsParserArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static List<string> Shipped() =>
        [.. Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    [Fact]
    public void An_xml_reader_is_created_in_one_file_only_and_no_other_xml_loading_path_exists_in_the_shipped_source()
    {
        foreach (var file in Shipped())
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            if (name != "SafeXml.cs")
            {
                text.ShouldNotContain("XmlReader.Create", Case.Sensitive, name);
            }

            foreach (var banned in new[] { "XmlDocument", "XmlTextReader", "XDocument.Load", "XDocument.Parse", "XElement.Load", "XPathDocument", "XmlUrlResolver", "XmlSecureResolver", "DtdProcessing.Parse", "DtdProcessing.Ignore", "XslCompiledTransform", "XmlSerializer" })
            {
                text.ShouldNotContain(banned, Case.Sensitive, name);
            }
        }
    }

    [Fact]
    public void The_hardened_factory_prohibits_the_dtd_has_no_resolver_and_bounds_the_document()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "FilingArchive", "SafeXml.cs"));

        text.ShouldContain("DtdProcessing = DtdProcessing.Prohibit");
        text.ShouldContain("XmlResolver = null");
        text.ShouldContain("MaxCharactersFromEntities = 0");
        text.ShouldContain("MaxCharactersInDocument = maxCharacters");
    }

    [Fact]
    public void Only_the_file_reader_names_the_factory_and_only_the_parse_step_names_the_reader_port()
    {
        var factory = Types.InAssembly(Infrastructure).That().HaveDependencyOn("XDrishti.Infrastructure.FilingArchive.SafeXml").GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order().ToList();
        var application = Types.InAssembly(Application).That().HaveDependencyOn(typeof(IResultsXbrlReader).FullName!).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().ToList();

        factory.ShouldBe(["XbrlFileReader"]);
        application.ShouldBe(["ParseResultsHandler"]);
    }

    [Fact]
    public void The_parse_code_in_every_layer_reaches_no_network_socket_or_process_type()
    {
        string[] banned = ["System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "System.Diagnostics.Process"];

        var domain = Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().ResideInNamespace("XDrishti.Domain.Filings").ShouldNot().HaveDependencyOnAny(banned).GetResult();
        var infrastructure = Types.InAssembly(Infrastructure).That().HaveNameStartingWith("XbrlFileReader").Or().HaveNameStartingWith("SafeXml").Or().HaveNameStartingWith("ResultFact").ShouldNot().HaveDependencyOnAny(banned).GetResult();

        domain.IsSuccessful.ShouldBeTrue(string.Join(", ", domain.FailingTypeNames ?? []));
        infrastructure.IsSuccessful.ShouldBeTrue(string.Join(", ", infrastructure.FailingTypeNames ?? []));
        foreach (var file in new[] { Src("XDrishti.Domain", "Filings", "ResultsXbrlParser.cs"), Src("XDrishti.Infrastructure", "FilingArchive", "XbrlFileReader.cs"), Src("XDrishti.Infrastructure", "FilingArchive", "SafeXml.cs") })
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "HttpClient", "WebClient", "WebRequest", "Socket", "File.Open", "File.Read", "Process.Start" })
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_result_facts_port_has_no_decision_caller_its_implementation_is_the_only_user()
    {
        var port = typeof(IResultFactsAsOf).FullName!;

        var application = Types.InAssembly(Application).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order().ToList();
        var infrastructure = Types.InAssembly(Infrastructure).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().ToList();
        var api = Types.InAssembly(Assembly.Load(new AssemblyName("XDrishti.Api"))).That().HaveDependencyOn(port).GetTypes().Select(t => t.Name).Distinct().ToList();

        application.ShouldBe(["DependencyInjection", "ExtensionFeatureExtractor", "ResultFactsAsOf"]);
        infrastructure.ShouldBeEmpty();
        api.ShouldBeEmpty();
        foreach (var name in new[] { "XDrishti.Execution", "XDrishti.Strategies", "XDrishti.Simulation", "XDrishti.Worker", "XDrishti.Feed" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).That().HaveDependencyOn(port).GetTypes().ShouldBeEmpty(name);
        }
    }

    [Fact]
    public void The_migration_replaces_the_empty_table_checks_emptiness_and_grants_each_table_insert_only_to_the_runtime_role_alone()
    {
        var text = File.ReadAllText(Src("XDrishti.Infrastructure", "Persistence", "Migrations", "20261102123646_ReshapeResultFacts.cs"));

        text.ShouldContain("IF EXISTS (SELECT 1 FROM filings.result_facts)");
        text.ShouldContain("RAISE EXCEPTION");
        text.ShouldContain("GrantTableToAppRole(Schemas.Filings, table)");
        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Filings, table)");
        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldNotContain("xd_exec");
        text.ShouldNotContain("xd_mcp");
        text.ShouldNotContain("GRANT UPDATE");
        text.ShouldNotContain("GRANT ALL");
        text.ShouldContain("PRIMARY KEY (filing_id, parser_version, field_code, span, basis)");
    }

    [Fact]
    public void The_pdf_and_every_non_xml_name_is_answered_without_opening_the_file()
    {
        var text = File.ReadAllText(Src("XDrishti.Application", "Filings", "ParseResultsHandler.cs"));
        var order = text.IndexOf("IsXbrlName(filing.FileName)", StringComparison.Ordinal);
        var open = text.IndexOf("files.OpenAsync", StringComparison.Ordinal);

        order.ShouldBeGreaterThan(0);
        open.ShouldBeGreaterThan(order);
        NumberAfterGuess().IsMatch(text).ShouldBeFalse();
    }

    [GeneratedRegex(@"\bdecimal\.Parse\(", RegexOptions.CultureInvariant)]
    private static partial Regex NumberAfterGuess();
}
