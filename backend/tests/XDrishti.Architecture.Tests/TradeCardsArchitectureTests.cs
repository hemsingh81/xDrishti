using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The rules of the trade cards (P3-04c, criterion 11; ADR 0041): the second read is an internal sealed handler over ports only that names no engine type and never merges; the page code in the export library writes no script, link, image,
/// import or URL and has an <c>href</c> only in the index. Each rule has a sentinel, a case the same code must reject, so that a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed class TradeCardsArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly string[] EngineModules = ["XDrishti.Simulation", "XDrishti.Indicators", "XDrishti.Strategies", "XDrishti.SimExport"];

    private static readonly string[] BannedMarkup =
    [
        "<script", "<link", "<img", "<iframe", "<object", "<embed", "<form", "@import", "url(", "xmlns", "http://", "https://", " src=", "srcset", "javascript:", "data:", " onclick", " onload", " onerror", "xlink:href",
    ];

    private static Type Handler() => Application.GetType("XDrishti.Application.Research.BuildTradeCardsHandler") ?? throw new InvalidOperationException("BuildTradeCardsHandler is missing.");

    /// <summary>The problems with a handler type: not internal, not sealed, a constructor parameter that is not a port.</summary>
    private static List<string> ShapeProblems(Type handler)
    {
        var problems = new List<string>();
        if (handler.IsPublic)
        {
            problems.Add("public");
        }

        if (!handler.IsSealed)
        {
            problems.Add("not sealed");
        }

        problems.AddRange(handler.GetConstructors().Single().GetParameters().Where(p => !p.ParameterType.IsInterface && p.ParameterType != typeof(TimeProvider)).Select(p => "parameter " + p.Name + " is not a port"));
        return problems;
    }

    [Fact]
    public void The_handler_is_internal_and_sealed_and_takes_ports_only()
    {
        ShapeProblems(Handler()).ShouldBeEmpty();
        var ports = Handler().GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();
        ports.ShouldBe(["IBarStore", "IBarQueries", "IAdjustmentFactors"]);
    }

    [Fact]
    public void The_shape_rule_does_catch_a_public_unsealed_class_with_a_concrete_parameter()
    {
        ShapeProblems(typeof(ConcreteHandlerSentinel)).ShouldBe(["public", "not sealed", "parameter text is not a port"], ignoreOrder: true);
    }

    [Fact]
    public void The_handler_names_no_engine_type_and_no_renderer_and_never_begins_a_merge()
    {
        var used = AssemblyScan.MembersUsedBy(Application, Handler().FullName!);

        used.ShouldNotContain(m => EngineModules.Any(module => m.Type.StartsWith(module, StringComparison.Ordinal)), "no engine or renderer type");
        used.ShouldNotContain(m => m.Member == "BeginMergeAsync", "a reader of the bars never merges");
        used.ShouldNotContain(m => m.Member == "Replay" || m.Member == "Prepare", "the trade is not recomputed here");
        used.ShouldContain(m => m.Member == "ReadAsync", "the scan does see the read");
        AssemblyScan.MembersUsedBy(Application, "XDrishti.Application.Research.RunSimulationHandler").ShouldContain(m => m.Member == "Replay", "the scan does see an engine call where there is one");
    }

    [Fact]
    public void The_handler_reads_no_clock_and_no_file()
    {
        var used = AssemblyScan.MembersUsedBy(Application, Handler().FullName!);

        used.Any(m => m.Type is "System.IO.File" or "System.IO.Directory" or "System.Environment" or "System.Random" or "System.Diagnostics.Stopwatch").ShouldBeFalse();
        used.Any(m => m.Type is "System.DateTime" or "System.DateTimeOffset" && m.Member is "get_Now" or "get_UtcNow" or "get_Today").ShouldBeFalse();
    }

    private static List<string> MarkupProblems(string source)
    {
        var code = string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        return [.. BannedMarkup.Where(banned => code.Contains(banned, StringComparison.OrdinalIgnoreCase))];
    }

    [Fact]
    public void The_page_code_writes_no_script_link_image_import_or_url_and_only_the_index_has_an_href()
    {
        var folder = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.SimExport", "Cards", "Html.cs"))!;
        var files = Directory.GetFiles(folder, "*.cs");

        files.Length.ShouldBeGreaterThan(8, "the scan does see the page code");
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            MarkupProblems(source).ShouldBeEmpty(Path.GetFileName(file));
            if (Path.GetFileName(file) != "IndexHtml.cs")
            {
                source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ShouldNotContain(l => l.Contains("href", StringComparison.OrdinalIgnoreCase), Path.GetFileName(file) + " writes an href");
            }
        }
    }

    [Fact]
    public void The_markup_rule_does_catch_a_script_an_import_a_url_and_a_handler_attribute()
    {
        MarkupProblems("page.Append(\"<script>x</script>\");").ShouldBe(["<script"]);
        MarkupProblems("css = \"@import 'a'; p{background:url(x)}\";").ShouldBe(["@import", "url("]);
        MarkupProblems("a = \"<a href=\\\"http://x\\\" onclick=\\\"y\\\">\";").ShouldBe(["http://", " onclick"]);
        MarkupProblems("// a comment about <script is not code").ShouldBeEmpty();
    }

    [Fact]
    public void The_page_code_reads_no_clock_and_no_machine_fact()
    {
        var folder = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.SimExport", "Cards", "Html.cs"))!;

        foreach (var file in Directory.GetFiles(folder, "*.cs"))
        {
            var source = File.ReadAllText(file);

            foreach (var banned in new[] { "DateTime.Now", "UtcNow", "DateTimeOffset.Now", "TimeProvider", "Environment.", "Path.", "File.", "CultureInfo.CurrentCulture", "Guid.NewGuid", "new Random" })
            {
                source.ShouldNotContain(banned, Case.Sensitive, Path.GetFileName(file));
            }
        }
    }
}

/// <summary>A public unsealed class with a concrete parameter, on purpose, to prove the shape rule of the handler sees such a case (it is never used by anything else).</summary>
public class ConcreteHandlerSentinel(string text)
{
    public string Text => text;
}
