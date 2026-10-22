using Microsoft.Extensions.DependencyInjection;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>The entry the deployment step calls after the migrations (spec P3-02d, D5): writes the built-in strategies to the store. One place, so that the host and the worker do not both seed.</summary>
public static class StrategySeeding
{
    /// <summary>Seeds the embedded built-ins; the number of them stored afterwards. A text that does not pass is logged and skipped, it does not stop the step.</summary>
    public static async Task<int> SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StrategySeeder>().SeedAsync(cancellationToken);
    }
}
