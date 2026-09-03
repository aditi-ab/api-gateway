using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.Persistence.SqlServer;

public static class SqlServerRegistration
{
    public static IServiceCollection AddGatewaySqlServer(this IServiceCollection services, string connectionString)
    {
        return services.AddDbContext<GatewayDbContext>(options => options.UseSqlServer(connectionString,
            x => x.MigrationsAssembly(typeof(SqlServerRegistration).Assembly.FullName)));
    }
}