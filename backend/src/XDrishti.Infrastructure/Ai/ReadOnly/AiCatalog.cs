using Npgsql;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// The catalog of readable views (spec P9-02 AC 9): the manifest joined to the live database. A view the context does not allow is still listed (so the model can tell the owner why there is nothing), with 0 rows and a note. Sizes are the planner's estimates of the base table, never a
/// count; one catalog query, no scan.
/// </summary>
internal sealed class AiCatalog(AiDataSources sources, IAiContext context) : IAiCatalog
{
    private const string PortfolioOff = "portfolio context is off";

    public async Task<IReadOnlyList<CatalogView>> ListAsync(IReadOnlyList<string> prefixes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        var views = DataManifest.Views.Where(v => prefixes.Count == 0 || prefixes.Any(p => v.Name.StartsWith(p, StringComparison.Ordinal))).ToList();
        var state = await context.GetAsync(cancellationToken);
        var estimates = await EstimatesAsync(views.Where(v => v.Class == ViewClass.C1 || state.PortfolioAllowed).Select(v => v.RowSource).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
        return
        [
            .. views.Select(v =>
            {
                var off = v.Class == ViewClass.C2 && !state.PortfolioAllowed;
                return new CatalogView(v.Name, v.Description, v.Grain, [v.Class.ToString()], off ? 0 : estimates.GetValueOrDefault(v.RowSource), off ? PortfolioOff : null);
            }),
        ];
    }

    public async Task<CatalogTable?> DescribeAsync(string name, CancellationToken cancellationToken)
    {
        var view = DataManifest.Find(name);
        if (view is null)
        {
            return null;
        }

        var live = await LiveTypesAsync(view.Name, cancellationToken);
        var state = view.Class == ViewClass.C2 ? await context.GetAsync(cancellationToken) : new AiContext(true, true);
        var limits = new List<string> { $"At most {ToolLimits.MaxRows} rows are returned ({ToolLimits.DefaultRows} unless you ask for more), cells are cut at {ToolLimits.MaxCellChars} characters.", "Filter by date or symbol: a query that scans a large view can hit the 5 second limit." };
        if (view.Class == ViewClass.C2)
        {
            limits.Add(state.PortfolioAllowed ? "Holds the owner's portfolio data (portfolio context is on)." : $"Returns no rows: {PortfolioOff}.");
        }

        return new CatalogTable(
            view.Name,
            view.Description,
            view.Grain,
            [view.Class.ToString()],
            [.. view.Columns.Select(c => new CatalogColumn(c.Name, live.GetValueOrDefault(c.Name, "unknown"), c.Kind, c.Class, c.Unit, c.Description))],
            limits);
    }

    private async Task<Dictionary<string, long>> EstimatesAsync(IReadOnlyList<string> tables, CancellationToken cancellationToken)
    {
        const string Sql = "SELECT n.nspname || '.' || c.relname, GREATEST(c.reltuples, 0)::bigint FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname || '.' || c.relname = ANY(@names)";
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            await using var command = sources.Read.CreateCommand(Sql);
            command.Parameters.AddWithValue("names", tables.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }

        return result;
    }

    private async Task<Dictionary<string, string>> LiveTypesAsync(string view, CancellationToken cancellationToken)
    {
        const string Sql = "SELECT a.attname, format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'aiv' AND c.relname = @view AND a.attnum > 0 AND NOT a.attisdropped";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await using var command = sources.Read.CreateCommand(Sql);
            command.Parameters.AddWithValue("view", view);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }

        return result;
    }
}
