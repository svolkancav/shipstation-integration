using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ShipStation.Application;

public static class MappingRegistery
{
    public static IServiceCollection RegisterProfiles(this IServiceCollection services)
    {
        services.AddAutoMapper(configuration => configuration.AddMaps(Assembly.GetExecutingAssembly()));

        return services;
    }
}
