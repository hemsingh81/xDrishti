using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Llm;

/// <summary>
/// Registers the local model server (spec P9-01, ADR 0005, 0136): the status port, and a typed <see cref="IChatClient"/> over the server's OpenAI-compatible endpoint. This is the only place that references the OpenAI packages. The chat client is created lazily and
/// only when something asks for it (P9-03 does); with the deployment flag off or the key empty asking for it fails with a plain message. The function-invoking wrapper is P9-03's.
/// </summary>
public static class LlmClientRegistration
{
    public const string ChatClientName = "llm-chat";

    public static IServiceCollection AddLocalLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.HasValidBaseUrl(), "Assistant:Llm:BaseUrl must be an absolute http or https address.")
            .ValidateOnStart();

        // No redirects, no cookies: a model server that answers with a redirect is treated as not understood, never followed.
        services.AddHttpClient(HttpLlmHealth.ClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler)
            .ConfigureHttpClient((sp, c) => c.MaxResponseContentBufferSize = 1_000_000)
            .RemoveAllLoggers();
        services.AddHttpClient(ChatClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler)
            .ConfigureHttpClient((sp, c) => c.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<LlmOptions>>().Value.RequestTimeoutSeconds))
            .RemoveAllLoggers();

        services.AddSingleton<ILlmHealth, HttpLlmHealth>();
        services.AddSingleton<IChatClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            if (!settings.Enabled || string.IsNullOrEmpty(settings.ApiKey))
            {
                throw new InvalidOperationException("The local model server is not enabled or has no API key.");
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ChatClientName);
            var client = new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
            return client.GetChatClient(settings.Model).AsIChatClient();
        });
        return services;
    }

    private static HttpMessageHandler CreateHandler() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
}
