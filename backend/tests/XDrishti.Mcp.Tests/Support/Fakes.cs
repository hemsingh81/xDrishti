using System.Collections.Concurrent;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp.Tests.Support;

/// <summary>The switches of the assistant as a test sets them; counts the reads.</summary>
internal sealed class FakeContext(bool enabled = true, bool portfolio = false) : IAiContext
{
    public bool Enabled { get; set; } = enabled;

    public bool Portfolio { get; set; } = portfolio;

    public bool Down { get; set; }

    public int Reads { get; private set; }

    public Task<AiContext> GetAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Down ? throw new AiDatabaseUnavailableException() : Task.FromResult(new AiContext(Enabled, Portfolio));
    }
}

/// <summary>A catalog over the real manifest with a fixed size per view.</summary>
internal sealed class FakeCatalog : IAiCatalog
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<CatalogView>> ListAsync(IReadOnlyList<string> prefixes, CancellationToken cancellationToken)
    {
        Calls++;
        IReadOnlyList<CatalogView> views =
        [
            .. DataManifest.Views.Where(v => prefixes.Count == 0 || prefixes.Any(p => v.Name.StartsWith(p, StringComparison.Ordinal)))
                .Select(v => new CatalogView(v.Name, v.Description, v.Grain, [v.Class.ToString()], 10, v.Class == ViewClass.C2 ? "portfolio context is off" : null)),
        ];
        return Task.FromResult(views);
    }

    public Task<CatalogTable?> DescribeAsync(string name, CancellationToken cancellationToken)
    {
        Calls++;
        var view = DataManifest.Find(name);
        return Task.FromResult(view is null ? null : new CatalogTable(view.Name, view.Description, view.Grain, [view.Class.ToString()], [.. view.Columns.Select(c => new CatalogColumn(c.Name, "text", c.Kind, c.Class, c.Unit, c.Description))], ["limit"]));
    }
}

/// <summary>Answers a query with a canned outcome and remembers what it was asked.</summary>
internal sealed class FakeSql(SqlRunOutcome? outcome = null) : IAiSqlRunner
{
    public ConcurrentQueue<(string Statement, int MaxRows, TimeSpan Timeout)> Asked { get; } = new();

    public Task<SqlRunOutcome> RunAsync(string statement, int maxRows, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Asked.Enqueue((statement, maxRows, timeout));
        return Task.FromResult(outcome ?? new SqlRunOutcome(SqlRunStatus.Ok, [new SqlColumn("n", "integer")], [[1L]], false, 3, null));
    }
}

internal sealed class FakeLab : IAiLabValidator
{
    public LabValidation Validate(string yaml) => new(yaml.Contains("valid", StringComparison.Ordinal), "abc", []);
}
