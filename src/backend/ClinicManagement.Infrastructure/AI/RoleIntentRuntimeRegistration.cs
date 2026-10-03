using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.Infrastructure.AI;

public static class RoleIntentRuntimeRegistration
{
    public static IServiceCollection AddRoleIntentRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RoleIntentModelOptions>(configuration.GetSection("RoleIntentModel"));
        services.AddSingleton<RoleIntentModel>();
        services.AddSingleton<IRoleIntentModel>(sp => sp.GetRequiredService<RoleIntentModel>());
        services.AddScoped<AiDeterministicPlanner>();
        services.AddScoped<HybridIntentRouter>();
        services.AddScoped<IAiDeterministicPlanner>(sp => sp.GetRequiredService<HybridIntentRouter>());
        return services;
    }
}
