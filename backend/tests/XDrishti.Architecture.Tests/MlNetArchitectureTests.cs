using System.Reflection;
using System.Xml.Linq;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The ML.NET adapter (P6-06, ADR 0068) is the only project that references <c>Microsoft.ML*</c>; it references only Domain among the projects, and nothing below or beside it (Domain, Application, Infrastructure, the hosts, the other libraries)
/// reaches the packages. The model is behind the Domain port <c>IScorerFactory</c>, so the harness and the registry never see ML.NET types.
/// </summary>
public sealed class MlNetArchitectureTests
{
    private static readonly Assembly MlNet = typeof(XDrishti.Learning.MlNet.MlNetModel).Assembly;

    private static IEnumerable<string> SourceProjects() =>
        Directory.GetFiles(Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(file => !RepositoryFiles.IsBuildOutput(file));

    private static List<string> MlPackages(string project) =>
        [.. XDocument.Load(project).Descendants("PackageReference").Select(e => (string?)e.Attribute("Include") ?? string.Empty).Where(IsMlNetPackage)];

    private static bool IsMlNetPackage(string name) => name == "Microsoft.ML" || name.StartsWith("Microsoft.ML.", StringComparison.Ordinal);

    [Fact]
    public void The_adapter_references_only_domain_among_the_projects_and_only_the_three_ml_net_packages()
    {
        var project = XDocument.Load(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Learning.MlNet", "XDrishti.Learning.MlNet.csproj"));

        RepositoryFiles.ProjectReferences(project).ShouldBe(["XDrishti.Domain"]);
        project.Descendants("PackageReference").Select(e => (string?)e.Attribute("Include")).Order().ShouldBe(["Microsoft.ML", "Microsoft.ML.FastTree", "Microsoft.ML.LightGbm"]);
        MlNet.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).ShouldBe(["XDrishti.Domain"]);
    }

    [Fact]
    public void No_other_source_project_references_an_ml_net_package()
    {
        var others = SourceProjects().Where(p => Path.GetFileNameWithoutExtension(p) != "XDrishti.Learning.MlNet").ToList();

        others.ShouldNotBeEmpty();
        others.Where(p => MlPackages(p).Count > 0).ShouldBeEmpty();
        foreach (var assembly in new[] { typeof(XDrishti.Domain.Common.Result).Assembly, typeof(XDrishti.Application.DependencyInjection).Assembly, typeof(XDrishti.Simulation.SimulationEngine).Assembly })
        {
            assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(n => IsMlNetPackage(n) || n == "XDrishti.Learning.MlNet").ShouldBeEmpty(assembly.GetName().Name);
        }

        // Infrastructure implements the model ports over the adapter (P6-07, ADR 0070): it may reach the adapter, never the packages.
        typeof(XDrishti.Infrastructure.DependencyInjection).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(IsMlNetPackage).ShouldBeEmpty("Infrastructure reaches ML.NET only through the adapter");
    }

    [Fact]
    public void Only_infrastructure_among_the_libraries_references_the_adapter_project()
    {
        var users = SourceProjects()
            .Where(p => RepositoryFiles.ProjectReferences(XDocument.Load(p)).Contains("XDrishti.Learning.MlNet"))
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToList();

        users.ShouldBe(["XDrishti.Infrastructure"]);
        typeof(XDrishti.Application.DependencyInjection).Assembly.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Learning.MlNet");
    }

    [Fact]
    public void The_package_rule_does_see_the_packages_in_the_adapter()
    {
        MlPackages(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Learning.MlNet", "XDrishti.Learning.MlNet.csproj")).Count.ShouldBe(3);
        MlNet.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ShouldContain(n => IsMlNetPackage(n));
    }

    [Fact]
    public void The_adapter_is_in_the_solution_and_the_dockerfile_and_exposes_no_ml_net_type()
    {
        var solution = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"));
        var dockerfile = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "Dockerfile"));

        solution.ShouldContain("src/XDrishti.Learning.MlNet/XDrishti.Learning.MlNet.csproj");
        solution.ShouldContain("tests/XDrishti.Learning.MlNet.Tests/XDrishti.Learning.MlNet.Tests.csproj");
        dockerfile.ShouldContain("COPY src/XDrishti.Learning.MlNet/XDrishti.Learning.MlNet.csproj src/XDrishti.Learning.MlNet/");
        var exposed = MlNet.GetExportedTypes().SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(m => m switch
            {
                MethodBase method => method.GetParameters().Select(p => p.ParameterType).Append(method is MethodInfo info ? info.ReturnType : typeof(void)),
                PropertyInfo property => [property.PropertyType],
                FieldInfo field => [field.FieldType],
                _ => [],
            })
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Microsoft.ML", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();
        exposed.ShouldBeEmpty("callers see Domain types and the adapter's own types only");
    }
}
