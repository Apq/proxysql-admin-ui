using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProxysqlAdminUi.Web.Contexts;
using ProxysqlAdminUi.Web.Data;
using ProxysqlAdminUi.Web.Models;

namespace ProxysqlAdminUi.Web.Services;


public class DefaultUserSeedService(
    ProxysqlAdminUiWebAuthContext dbContext,
    IConfiguration configuration,
    ILogger<DefaultUserSeedService> logger,
    UserManager<ProxysqlAdminUiWebUser> userManager,
    InitialCredentialService initialCredentialService)
{
    public async Task SeedDefaultUsersAsync()
    {
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

            if (pendingMigrations.Any())
            {
                await dbContext.Database.MigrateAsync();
            }

            var existingUsers = await dbContext.Users.AnyAsync();
            if (!existingUsers)
            {
                var defaultUsers = configuration.GetSection("DefaultUsers")
                    .Get<List<DefaultUserModel>>() ?? new List<DefaultUserModel>();

                foreach (var user in defaultUsers)
                {
                    var initialPassword = initialCredentialService.GeneratePassword();

                    var result = await userManager.CreateAsync(new ProxysqlAdminUiWebUser()
                    {
                        UserName = user.Username,
                        EmailConfirmed = true,
                        Email = user.Username+"@localhost"
                    }, initialPassword);

                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                        {
                            logger.LogCritical($"{error.Code}: {error.Description}");
                        }

                        continue;
                    }

                    await dbContext.SaveChangesAsync();
                    var newUser = await userManager.Users.FirstAsync(x => x.UserName == user.Username);

                    var token = await userManager.GenerateEmailConfirmationTokenAsync(newUser);

                    await userManager.ConfirmEmailAsync(newUser, token);

                    await initialCredentialService.AddCredentialAsync(
                        new InitialCredential(user.Username, initialPassword));

                    logger.LogInformation($"Added default user: {user.Username}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding default users");
        }
    }
}
