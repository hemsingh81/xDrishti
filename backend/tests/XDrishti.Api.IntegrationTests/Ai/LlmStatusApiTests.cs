using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// Spec P9-01 AC 7 and 8 end to end: a session is needed; with the deployment flag off (the default) or an unreachable server the API stays healthy and the status says down; against a fake llama.cpp server on a loopback port it is ready and shows the pinned file's hash;
/// no answer ever holds the key, the path of the file or the address of the server.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LlmStatusApiTests(ApiFactory factory)
{
    private const string Key = "integration-test-key-0123456789abcdef";
    private static readonly Uri Route = new("/api/assistant/llm-status", UriKind.Relative);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<WebApplication> StartFakeServerAsync()
    {
        var app = WebApplication.CreateSlimBuilder();
        app.WebHost.UseUrls("http://127.0.0.1:0");
        var server = app.Build();
        string Bearer(HttpContext c) => c.Request.Headers.Authorization.ToString();
        server.MapGet("/health", () => Results.Json(new { status = "ok" }));
        server.MapGet("/v1/models", (HttpContext c) => Bearer(c) == "Bearer " + Key ? Results.Json(new { data = new[] { new { id = "xd-main" } } }) : Results.Unauthorized());
        server.MapGet("/props", (HttpContext c) => Bearer(c) == "Bearer " + Key ? Results.Json(new { model_path = "/mnt/models/gpt-oss-20b-MXFP4.gguf", build_info = "b11530-fake" }) : Results.Unauthorized());
        await server.StartAsync(Ct);
        return server;
    }

    private async Task<(HttpStatusCode Code, string Body)> GetAsync(WebApplicationFactory<Program> host, bool signedIn = true)
    {
        using var client = host.CreateClient();
        if (signedIn)
        {
            var session = await factory.SignInAsync(Ct);
            client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={session.CookieValue}");
        }

        using var response = await client.GetAsync(Route, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Without_a_session_the_status_is_refused()
    {
        using var response = await factory.CreateAnonymousClient().GetAsync(Route, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task By_default_the_flag_is_off_and_the_status_says_down_with_the_start_command_and_the_api_is_healthy()
    {
        var (code, body) = await GetAsync(factory);

        code.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("state").GetString().ShouldBe("down");
        json.RootElement.GetProperty("startCommand").GetString().ShouldBe("scripts/xd-ai.sh start");
        json.RootElement.GetProperty("modelFile").ValueKind.ShouldBe(JsonValueKind.Null);
        using var ready = await factory.CreateAnonymousClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_the_flag_on_and_nothing_listening_the_status_is_down_and_the_api_stays_healthy()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Assistant:Llm:Enabled", "true");
            b.UseSetting("Assistant:Llm:ApiKey", Key);
            b.UseSetting("Assistant:Llm:BaseUrl", "http://127.0.0.1:1/v1");
        });

        var (code, body) = await GetAsync(host);

        code.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(body).RootElement.GetProperty("state").GetString().ShouldBe("down");
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain(Key);
        using var ready = await host.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Against_a_fake_server_it_is_ready_with_the_file_hash_and_build_and_shows_no_key_path_or_address()
    {
        await using var fake = await StartFakeServerAsync();
        var address = fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Assistant:Llm:Enabled", "true");
            b.UseSetting("Assistant:Llm:ApiKey", Key);
            b.UseSetting("Assistant:Llm:BaseUrl", address.TrimEnd('/') + "/v1");
        });

        var (code, body) = await GetAsync(host);

        code.ShouldBe(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("state").GetString().ShouldBe("ready");
        json.GetProperty("model").GetString().ShouldBe("xd-main");
        json.GetProperty("modelFile").GetString().ShouldBe("gpt-oss-20b-MXFP4.gguf");
        json.GetProperty("modelSha256").GetString().ShouldBe("27cd6c432c7672cb812a92f611cf3ba7bbc35928262bb1e1253ff4ee6ae35901");
        json.GetProperty("build").GetString().ShouldBe("b11530-fake");
        body.ShouldNotContain(Key);
        body.ShouldNotContain("/mnt/models");
        body.ShouldNotContain("127.0.0.1");
        await fake.StopAsync(Ct);
    }

    [Fact]
    public async Task A_wrong_key_is_misconfigured()
    {
        await using var fake = await StartFakeServerAsync();
        var address = fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Assistant:Llm:Enabled", "true");
            b.UseSetting("Assistant:Llm:ApiKey", "some-other-key-0123456789");
            b.UseSetting("Assistant:Llm:BaseUrl", address.TrimEnd('/') + "/v1");
        });

        var (_, body) = await GetAsync(host);

        JsonDocument.Parse(body).RootElement.GetProperty("state").GetString().ShouldBe("misconfigured");
        await fake.StopAsync(Ct);
    }
}
