using System.Text.Json;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// Reads the marker of the database behind the eval tool endpoint, through that endpoint and one reviewed query (spec P9-09 AC 13): <c>select fixture_hash from aiv.eval_marker</c>. Null for no marker, a tool error or an endpoint that cannot be reached; the runner then refuses with
/// <c>eval.wrong_database</c> before any model call. The query is fixed text; nothing of the model reaches it.
/// </summary>
internal sealed class EvalFixtureProbe(IEvalToolCatalog tools) : IEvalFixtureProbe
{
    public async ValueTask<string?> ReadFixtureHashAsync(CancellationToken cancellationToken)
    {
        var call = await tools.CallAsync("run_sql", """{"sql":"select fixture_hash from aiv.eval_marker limit 2"}""", cancellationToken);
        if (!call.Reachable || call.IsError)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(call.Text);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True || !root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1)
            {
                return null;
            }

            return rows[0].TryGetProperty("fixture_hash", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
