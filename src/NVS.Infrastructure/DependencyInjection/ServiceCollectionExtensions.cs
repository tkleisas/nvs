using Microsoft.Extensions.DependencyInjection;

namespace NVS.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNvsInfrastructure(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddOptions();
        
        return services;
    }
}
