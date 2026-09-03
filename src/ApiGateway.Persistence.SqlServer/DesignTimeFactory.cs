using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApiGateway.Persistence.SqlServer;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=ApiGatewayDesign;Trusted_Connection=True",
            x => x.MigrationsAssembly(typeof(DesignTimeFactory).Assembly.FullName)).Options;
        return new GatewayDbContext(options);
    }
}