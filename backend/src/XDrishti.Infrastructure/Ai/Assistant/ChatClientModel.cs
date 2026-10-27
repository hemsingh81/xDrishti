using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The local model as <see cref="IChatModel"/> (spec P9-03): turns a <see cref="ModelRequest"/> into a streaming chat request on the <see cref="IChatClient"/> of P9-01 and its updates into <see cref="ModelStreamPart"/>s. The tools are given as declarations only and there is no function-invoking
/// wrapper: the model server never runs a tool, the turn loop does (budget, audit, discard). The chat client is asked for only when a turn runs, so a disabled or unconfigured model server costs nothing at start-up. Any failure of the server becomes a <see cref="ModelUnavailableException"/> without its text.
/// </summary>
internal sealed class ChatClientModel(IServiceProvider services) : IChatModel
{
    public async IAsyncEnumerable<ModelStreamPart> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IChatClient client;
        try
        {
            client = services.GetRequiredService<IChatClient>();
        }
        catch (InvalidOperationException ex)
        {
            throw new ModelUnavailableException("The model client is not available.", ex);
        }

        var options = new ChatOptions
        {
            Temperature = request.Temperature,
            MaxOutputTokens = request.MaxOutputTokens,
            Seed = request.Seed,
            ToolMode = request.Tools.Count > 0 ? ChatToolMode.Auto : null,
            Tools = request.Tools.Count > 0 ? [.. request.Tools.Select(Declare)] : null,
        };

        var finish = "stop";
        int? promptTokens = null;
        int? completionTokens = null;
        var stream = client.GetStreamingResponseAsync(request.Messages.Select(Map).ToList(), options, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool more;
            try
            {
                more = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ModelUnavailableException("The model server failed.", ex);
            }

            if (!more)
            {
                break;
            }

            var update = enumerator.Current;
            if (update.FinishReason is { } reason)
            {
                finish = reason.Value;
            }

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        yield return new ModelTextPart(text.Text);
                        break;
                    case FunctionCallContent call:
                        yield return new ModelToolCallPart(new ModelToolCall(call.CallId, call.Name, JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object?>())));
                        break;
                    case UsageContent usage:
                        promptTokens = usage.Details.InputTokenCount is { } p ? (int)p : promptTokens;
                        completionTokens = usage.Details.OutputTokenCount is { } c ? (int)c : completionTokens;
                        break;
                    default:
                        break;
                }
            }
        }

        yield return new ModelFinishPart(finish, promptTokens, completionTokens);
    }

    private static AITool Declare(ToolDescriptor tool)
    {
        using var schema = JsonDocument.Parse(string.IsNullOrWhiteSpace(tool.InputSchemaJson) ? "{\"type\":\"object\"}" : tool.InputSchemaJson);
        return AIFunctionFactory.CreateDeclaration(tool.Name, tool.Description, schema.RootElement.Clone());
    }

    private static ChatMessage Map(ModelMessage message) => message.Role switch
    {
        ModelRole.System => new ChatMessage(ChatRole.System, message.Content),
        ModelRole.User => new ChatMessage(ChatRole.User, message.Content),
        ModelRole.Tool => new ChatMessage(ChatRole.Tool, [new FunctionResultContent(message.ToolCallId ?? string.Empty, message.Content)]),
        _ => MapAssistant(message),
    };

    private static ChatMessage MapAssistant(ModelMessage message)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(message.Content))
        {
            contents.Add(new TextContent(message.Content));
        }

        foreach (var call in message.ToolCalls ?? [])
        {
            contents.Add(new FunctionCallContent(call.Id, call.Name, Arguments(call.ArgumentsJson)));
        }

        return new ChatMessage(ChatRole.Assistant, contents);
    }

    private static Dictionary<string, object?> Arguments(string json)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    result[property.Name] = property.Value.Clone();
                }
            }
        }
        catch (JsonException)
        {
            // Not an object: the call goes back without arguments (the server refused it once already).
        }

        return result;
    }
}
