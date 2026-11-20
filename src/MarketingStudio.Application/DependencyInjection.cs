using FluentValidation;
using MarketingStudio.Application.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MarketingStudio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
