using FluentValidation;
using MarketingStudio.Application.Abstractions;
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
        services.AddScoped<Users.SeatGuard>();
        services.AddScoped<Knowledge.KnowledgeProcessor>();
        services.AddScoped<BackgroundTenantScope>();

        return services;
    }
}
