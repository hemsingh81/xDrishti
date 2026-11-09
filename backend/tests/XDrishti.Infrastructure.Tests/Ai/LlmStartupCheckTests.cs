using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Ai.Llm;

namespace XDrishti.Infrastructure.Tests.Ai;

/// <summary>Spec P9-01: <c>llm.key_missing</c> is logged once when the options are built at start, only when the server is enabled and the key is empty.</summary>
public sealed class LlmStartupCheckTests
{
    private sealed class Capture : ILoggerProvider, ILogger
    {
        public List<int> Ids { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Ids.Add(eventId.Id);

        public void Dispose()
        {
        }
    }

    private static List<int> Start(string enabled, string key)
    {
        var log = new Capture();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assistant:Llm:Enabled"] = enabled,
            ["Assistant:Llm:ApiKey"] = key,
        }).Build();
        using var provider = new ServiceCollection().AddLogging(b => b.AddProvider(log)).AddLocalLlm(configuration).BuildServiceProvider();

        _ = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<LlmOptions>>().Value; // once, not on every read
        return log.Ids;
    }

    [Fact]
    public void Enabled_without_a_key_logs_key_missing_once() => Start("true", "").ShouldBe([6001]);

    [Theory]
    [InlineData("false", "")]
    [InlineData("true", "secret-value")]
    public void Off_or_with_a_key_logs_nothing(string enabled, string key) => Start(enabled, key).ShouldBeEmpty();
}
