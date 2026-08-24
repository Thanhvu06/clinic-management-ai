using ClinicManagement.Application.Common.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Authentication;

public static class RoleSeeder
{
    public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RoleSeeder");

        string[] roles = { RoleNames.Patient, RoleNames.Receptionist, RoleNames.Doctor, RoleNames.Admin, RoleNames.Pharmacist };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (result.Succeeded)
                {
                    logger.LogInformation($"Role '{role}' created successfully.");
                }
                else
                {
                    logger.LogError($"Failed to create role '{role}'. Errors: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
