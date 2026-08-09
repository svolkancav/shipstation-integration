using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShipStation.Application.Configuration;
using ShipStation.Application.Http;
using ShipStation.Application.Services;

namespace ShipStation.Application;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ShipStationOptions>()
            .Bind(configuration.GetSection(ShipStationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        services.AddTransient<ShipStationAuthenticationHandler>();
        services.AddTransient<RateLimitHandler>();

        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddHttpClient<IShipStationOrderClient, ShipStationOrderClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ShipStationOptions>>().Value;

                // The trailing slash matters: without it the last path segment of the
                // base address is dropped when a relative URI is resolved against it.
                client.BaseAddress = new Uri(options.BaseAddress.AbsoluteUri.TrimEnd('/') + "/");
                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddHttpMessageHandler<ShipStationAuthenticationHandler>()
            .AddHttpMessageHandler<RateLimitHandler>();

        services.AddScoped<IOrderSyncService, OrderSyncService>();

        return services;
    }
}
