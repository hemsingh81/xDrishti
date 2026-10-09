using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Platform;

namespace XDrishti.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One NpgsqlDataSource per process: owns the connection pool; shared by EF Core and raw ADO.NET.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                CommandTimeout = options.CommandTimeoutSeconds,
                MaxPoolSize = options.MaxPoolSize,
            };
            if (!string.IsNullOrEmpty(options.Password))
            {
                builder.Password = options.Password;
            }

            return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
        });

        // Pooled DbContext instances: avoids per-request allocation cost.
        services.AddDbContextPool<XDrishtiDbContext>((sp, options) =>
            ConfigureDbContext(options, sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddScoped<IServiceHeartbeatStore, ServiceHeartbeatStore>();
        services.AddScoped<IDatabaseInfoProvider, DatabaseInfoProvider>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, ["ready"]);

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        ApplyConventions(options.UseNpgsql(dataSource, Npgsql));

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        ApplyConventions(options.UseNpgsql(connectionString, Npgsql));

    private static void Npgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform)
            .EnableRetryOnFailure(maxRetryCount: 3);

    // snake_case tables/columns. Reads opt out of change tracking explicitly with AsNoTracking().
    private static void ApplyConventions(DbContextOptionsBuilder options) =>
        options.UseSnakeCaseNamingConvention();
}
