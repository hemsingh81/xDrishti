using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to create migrations; never connects to a real database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<XDrishtiDbContext>
{
    public XDrishtiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<XDrishtiDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=xdrishti;Username=design_time");
        return new XDrishtiDbContext(options.Options);
    }
}
