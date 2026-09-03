using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApiGateway.Persistence.Sqlite;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite("Data Source=design.db",
            x => x.MigrationsAssembly(typeof(DesignTimeFactory).Assembly.FullName)).Options;
        return new GatewayDbContext(options);
    }
}