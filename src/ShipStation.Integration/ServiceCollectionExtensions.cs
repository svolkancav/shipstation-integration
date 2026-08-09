using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShipStation.Integration.Authentication;
using ShipStation.Integration.Configuration;
using ShipStation.Integration.Http;
using ShipStation.Integration.Orders;

namespace ShipStation.Integration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ShipStation client against the <c>ShipStation</c> configuration
    /// section. Credentials are validated at startup so a misconfigured deployment
    /// fails on boot rather than on the first order sync.
    /// </summary>
    public static IServiceCollection AddShipStation(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddShipStation(options =>
            configuration.GetSection(ShipStationOptions.SectionName).Bind(options));
    }

    public static IServiceCollection AddShipStation(
        this IServiceCollection services,
        Action<ShipStationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ShipStationOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTimeProvider();
        services.AddTransient<ShipStationAuthenticationHandler>();
        services.AddTransient<RateLimitHandler>();

        services.AddHttpClient<IShipStationOrderClient, ShipStationOrderClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ShipStationOptions>>().Value;

                // A trailing slash matters: without it the last path segment of the
                // base address is dropped when a relative URI is resolved against it.
                client.BaseAddress = EnsureTrailingSlash(options.BaseAddress);
                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddHttpMessageHandler<ShipStationAuthenticationHandler>()
            .AddHttpMessageHandler<RateLimitHandler>();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
