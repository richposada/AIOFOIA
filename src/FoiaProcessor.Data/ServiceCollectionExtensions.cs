using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoiaProcessor.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoiaData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FoiaDb")
            ?? throw new InvalidOperationException("Connection string 'FoiaDb' is not configured.");

        services.AddDbContext<FoiaDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                // NOTE: Do NOT call EnableRetryOnFailure here. The SqlServer retrying execution
                // strategy re-executes the entire SaveChanges operation on transient errors.
                // Without a concurrency token (RowVersion), a retry whose original write already
                // committed will report 0 rows affected and surface as DbUpdateConcurrencyException.
                sql.MigrationsAssembly(typeof(FoiaDbContext).Assembly.FullName);
            }));

        return services;
    }
}
