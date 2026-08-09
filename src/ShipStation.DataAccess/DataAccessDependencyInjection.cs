using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShipStation.DataAccess.Repositories;
using ShipStation.DataAccess.Repositories.Impl;

namespace ShipStation.DataAccess;

public static class DataAccessDependencyInjection
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("ShipStation")
            ?? throw new InvalidOperationException("Connection string 'ShipStation' is missing.");

        services.AddDbContext<AppDatabaseContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        services.AddRepositories();

        return services;
    }

    private static void AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IShipStationOrderRepository, ShipStationOrderRepository>();
    }
}
