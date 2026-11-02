using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-01 criteria 7 and 8 (ADR 0179): the folder-drop filings source, its port and the Domain types reach no network, write nothing into the inbox, and no fetch setting exists anywhere in the shipped source. Nothing but the filings code and the DI root references
/// a filings type until the P15-02 ingest exists.
/// </summary>
public sealed partial class FilingsArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] Network =
    [
        "System.Net.Http", "System.Net.Sockets", "System.Net.WebClient", "System.Net.WebRequest", "System.Net.WebSockets", "System.Net.Security", "XDrishti.Infrastructure.Brokers", "XDrishti.Application.Abstractions.Brokers",
    ];

    private static readonly string[] FilingsNamespaces = ["XDrishti.Domain.Filings", "XDrishti.Application.Abstractions.Filings", "XDrishti.Infrastructure.Filings"];

    private static string[] SourceFiles(string project, string folder) =>
        [.. Directory.GetFiles(Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!, "src", project, folder), "*.cs", SearchOption.AllDirectories)];

    [Fact]
    public void The_domain_types_the_port_and_the_adapter_reference_no_network_or_broker_type()
    {
        var domain = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Filings").ShouldNot().HaveDependencyOnAny(Network).GetResult();
        var port = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Abstractions.Filings").ShouldNot().HaveDependencyOnAny(Network).GetResult();
        var adapter = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Filings").ShouldNot().HaveDependencyOnAny(Network).GetResult();

        domain.IsSuccessful.ShouldBeTrue(string.Join(", ", domain.FailingTypeNames ?? []));
        port.IsSuccessful.ShouldBeTrue(string.Join(", ", port.FailingTypeNames ?? []));
        adapter.IsSuccessful.ShouldBeTrue(string.Join(", ", adapter.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_filings_source_text_names_no_network_type()
    {
        var banned = new[] { "System.Net.Http", "System.Net.Sockets", "HttpClient", "IHttpClientFactory", "WebClient", "HttpMessageHandler", "Dhan", "BrokerEndpoints" };
        var files = new[] { ("XDrishti.Domain", "Filings"), ("XDrishti.Application", Path.Combine("Abstractions", "Filings")), ("XDrishti.Infrastructure", "Filings") };

        foreach (var (project, folder) in files)
        {
            var found = SourceFiles(project, folder);
            found.ShouldNotBeEmpty();
            foreach (var file in found)
            {
                var text = File.ReadAllText(file);
                foreach (var word in banned)
                {
                    text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file) + " names " + word);
                }
            }
        }
    }

    [Fact]
    public void The_adapter_takes_no_http_dependency_and_exposes_only_the_two_port_methods()
    {
        var adapter = Infrastructure.GetType("XDrishti.Infrastructure.Filings.FolderDropFilingsSource").ShouldNotBeNull();

        adapter.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).Order().ShouldBe(["ILogger`1", "IOptions`1", "TimeProvider"]);
        typeof(IFilingsSource).GetMethods().Select(m => m.Name).Order().ShouldBe(["ListAsync", "OpenAsync"]);
        typeof(IFilingsSource).GetProperties().ShouldBeEmpty();
    }

    [Fact]
    public void The_adapter_never_writes_moves_or_deletes_in_the_inbox()
    {
        var banned = new[] { "File.Write", "File.Append", "File.Create", "File.Delete", "File.Move", "File.Copy", "File.Replace", "File.SetUnixFileMode", "File.SetLastWrite", "File.SetAttributes", "Directory.Create", "Directory.Delete", "Directory.Move", "FileMode.Create", "FileMode.CreateNew", "FileMode.Append", "FileMode.Truncate", "FileMode.OpenOrCreate", "FileAccess.Write", "FileAccess.ReadWrite", ".Delete(", ".MoveTo(", ".CopyTo(", ".Create(" };

        foreach (var file in SourceFiles("XDrishti.Infrastructure", "Filings"))
        {
            var text = File.ReadAllText(file);
            foreach (var word in banned)
            {
                text.ShouldNotContain(word, Case.Sensitive, Path.GetFileName(file) + " contains " + word);
            }
        }
    }

    [Fact]
    public void No_fetch_setting_exists_anywhere_in_the_shipped_source()
    {
        var root = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!, "src");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".yaml", StringComparison.Ordinal))).ToList();
        files.ShouldNotBeEmpty();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            FetchSetting().IsMatch(text).ShouldBeFalse(file + " names a filings fetch setting");
        }
    }

    [GeneratedRegex(@"filings[.:_]+fetch", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FetchSetting();

    [Fact]
    public void Nothing_but_the_filings_code_and_the_di_roots_references_a_filings_type()
    {
        foreach (var name in new[] { "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain", "XDrishti.Api", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Mcp", "XDrishti.Learning.MlNet", "XDrishti.Execution", "XDrishti.Hosting", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader" })
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            var result = Types.InAssembly(assembly).That()
                .DoNotResideInNamespaceStartingWith("XDrishti.Domain.Filings").And().DoNotResideInNamespaceStartingWith("XDrishti.Application.Abstractions.Filings").And().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.Filings")
                .And().DoNotHaveNameMatching("^(DependencyInjection)$")
                .ShouldNot().HaveDependencyOnAny(FilingsNamespaces).GetResult();

            result.IsSuccessful.ShouldBeTrue(name + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }
}
