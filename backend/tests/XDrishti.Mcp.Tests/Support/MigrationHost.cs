using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Mcp.Tests.Support;

/// <summary>A minimal service provider with what <c>xd-api migrate</c> has when it provisions the roles: the owner's <c>DbContext</c>, the configuration and a logger that keeps every line.</summary>
internal static class MigrationHost
{
    public static ServiceProvider Build(string ownerConnection, string? readPassword, string? sqlPassword, CapturedLoggerProvider logs)
    {
        var settings = new Dictionary<string, string?> { ["AiRoles:ReadPassword"] = readPassword, ["AiRoles:SqlPassword"] = sqlPassword };
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging(b => b.AddProvider(logs));
        services.AddDbContext<XDrishtiDbContext>(o => o.UseNpgsql(ownerConnection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")).UseSnakeCaseNamingConvention());
        return services.BuildServiceProvider();
    }
}

/// <summary>Collects the lines of every logger it hands out.</summary>
internal sealed class CapturedLoggerProvider : ILoggerProvider
{
    public List<(int EventId, LogLevel Level, string Message)> Lines { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Sink(this);

    public void Dispose()
    {
    }

    public string All() => string.Join('\n', Lines.Select(l => l.Message));

    private sealed class Sink(CapturedLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Lines)
            {
                owner.Lines.Add((eventId.Id, logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
            }
        }
    }
}
