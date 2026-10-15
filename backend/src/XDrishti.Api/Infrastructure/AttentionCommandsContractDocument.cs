using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using XDrishti.Application.Portfolio.Attention;

namespace XDrishti.Api.Infrastructure;

/// <summary>
/// Puts the list of the commands a notice may carry into the generated contract (<c>scripts/gen-api.sh</c>, ADR 0030): the document extension <c>x-xdrishti-attention-commands</c>, built from <see cref="AttentionCommands.Allowed"/>, the one list the deriver builds its targets from.
/// The frontend's generator (<c>frontend/scripts/gen-attention-commands.mjs</c>) reads it and writes <c>attentionCommands.generated.ts</c>, which is all the client allows itself to send. The C# list is the single source: nothing is copied by hand on the client.
/// </summary>
internal static class AttentionCommandsContractDocument
{
    public const string ExtensionName = "x-xdrishti-attention-commands";

    /// <summary>The list as the extension holds it: for each command its name, HTTP method, path template and the whole-path pattern.</summary>
    public static JsonArray Build() =>
        new([.. AttentionCommands.Allowed.Select(c => (JsonNode)new JsonObject
        {
            ["name"] = c.Name,
            ["method"] = c.Method,
            ["template"] = c.Template,
            ["pattern"] = c.Pattern,
        })]);

    public static Task AddAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions[ExtensionName] = new JsonNodeExtension(Build());
        return Task.CompletedTask;
    }
}
