using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShipStation.Integration.Persistence.Sync;
using ShipStation.Integration.Persistence.Upsert;

namespace ShipStation.Integration.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL store and the sync service. <c>AddShipStation</c>
    /// must be registered too — the sync service reads through the API client.
    /// </summary>
    public static IServiceCollection AddShipStationPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "ShipStation")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is missing.");

        return services.AddShipStationPersistence(connectionString);
    }

    public static IServiceCollection AddShipStationPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<ShipStationDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IOrderSyncService, OrderSyncService>();

        return services;
    }
}
