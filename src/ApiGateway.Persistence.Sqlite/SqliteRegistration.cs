using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.Persistence.Sqlite;

public static class SqliteRegistration
{
    public static IServiceCollection AddGatewaySqlite(this IServiceCollection services, string connectionString)
    {
        return services.AddDbContext<GatewayDbContext>(options => options.UseSqlite(connectionString,
            x => x.MigrationsAssembly(typeof(SqliteRegistration).Assembly.FullName)));
    }
}