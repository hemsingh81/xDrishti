using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Lab;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.Infrastructure;

/// <summary>
/// Puts the SignalR hub into the generated contract (<c>scripts/gen-api.sh</c>): OpenAPI cannot describe a hub, so the message types are added as component schemas (they get TypeScript types like every other schema) and the
/// hub itself - route, server methods with their argument and result, client events with their payload - as the document extension <c>x-xdrishti-hubs</c>, built from <see cref="LiveHubContract"/> and the contract types. The frontend's generator
/// reads that extension to write <c>hubContract.generated.ts</c>. The C# contract is the single source: nothing is written by hand on either side.
/// </summary>
internal static class HubContractDocument
{
    public const string ExtensionName = "x-xdrishti-hubs";

    /// <summary>The message types of the hub, by schema name.</summary>
    private static readonly (string Name, Type Type)[] Messages =
    [
        (nameof(LiveScope), typeof(LiveScope)),
        (nameof(SubscribeAck), typeof(SubscribeAck)),
        (nameof(InstrumentPrice), typeof(InstrumentPrice)),
        (nameof(PricesChanged), typeof(PricesChanged)),
        (nameof(LabRunProgress), typeof(LabRunProgress)),
        (nameof(LabRunFinished), typeof(LabRunFinished)),
        (nameof(ExperimentJobProgress), typeof(ExperimentJobProgress)),
        (nameof(ExperimentJobFinished), typeof(ExperimentJobFinished)),
    ];

    public static async Task AddAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var (name, type) in Messages)
        {
            var schema = await context.GetOrCreateSchemaAsync(type, null, cancellationToken);
            document.AddComponent(name, schema);
        }

        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions[ExtensionName] = new JsonNodeExtension(new JsonObject
        {
            [LiveHubContract.Route] = new JsonObject
            {
                ["transport"] = "WebSockets",
                ["types"] = new JsonArray([.. Messages.Select(m => (JsonNode)m.Name), nameof(LiveFeedStatus)]),
                ["methods"] = new JsonObject
                {
                    [LiveHubContract.Subscribe] = new JsonObject { ["arguments"] = new JsonArray(Argument("scope", nameof(LiveScope))), ["returns"] = nameof(SubscribeAck) },
                    [LiveHubContract.Unsubscribe] = new JsonObject { ["arguments"] = new JsonArray(), ["returns"] = null },
                },
                ["events"] = new JsonObject
                {
                    [LiveHubContract.PricesChangedEvent] = new JsonObject { ["payload"] = nameof(PricesChanged) },
                    [LiveHubContract.FeedStatusEvent] = new JsonObject { ["payload"] = nameof(LiveFeedStatus) },
                    [LiveHubContract.LabRunProgressEvent] = new JsonObject { ["payload"] = nameof(LabRunProgress) },
                    [LiveHubContract.LabRunFinishedEvent] = new JsonObject { ["payload"] = nameof(LabRunFinished) },
                    [LiveHubContract.ExperimentProgressEvent] = new JsonObject { ["payload"] = nameof(ExperimentJobProgress) },
                    [LiveHubContract.ExperimentFinishedEvent] = new JsonObject { ["payload"] = nameof(ExperimentJobFinished) },
                },
                ["limits"] = new JsonObject
                {
                    ["maxInstrumentsPerMessage"] = LiveHubContract.MaxInstrumentsPerMessage,
                    ["maxMessageBytes"] = LiveHubContract.MaxMessageBytes,
                    ["maxInstrumentsPerConnection"] = LiveHubContract.MaxInstrumentsPerConnection,
                    ["maxHoldingIds"] = LiveHubContract.MaxHoldingIds,
                },
            },
        });
    }

    private static JsonObject Argument(string name, string type) => new() { ["name"] = name, ["type"] = type };
}
