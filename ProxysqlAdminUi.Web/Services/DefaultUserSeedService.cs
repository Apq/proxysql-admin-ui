using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProxysqlAdminUi.Web.Contexts;
using ProxysqlAdminUi.Web.Data;

namespace ProxysqlAdminUi.Web.Services;


public class DefaultUserSeedService(
    ProxysqlAdminUiWebAuthContext dbContext,
    ILogger<DefaultUserSeedService> logger,
    UserManager<ProxysqlAdminUiWebUser> userManager,
    InitialCredentialService initialCredentialService)
{
    private const string DefaultUsername = "admin";

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
                var initialPassword = initialCredentialService.GeneratePassword();

                var result = await userManager.CreateAsync(new ProxysqlAdminUiWebUser
                {
                    UserName = DefaultUsername,
                    EmailConfirmed = true,
                    Email = DefaultUsername + "@localhost"
                }, initialPassword);

                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                    {
                        logger.LogCritical($"{error.Code}: {error.Description}");
                    }

                    return;
                }

                await dbContext.SaveChangesAsync();
                var newUser = await userManager.Users.FirstAsync(x => x.UserName == DefaultUsername);

                var token = await userManager.GenerateEmailConfirmationTokenAsync(newUser);

                await userManager.ConfirmEmailAsync(newUser, token);

                await initialCredentialService.AddCredentialAsync(
                    new InitialCredential(DefaultUsername, initialPassword));

                logger.LogInformation($"Added default user: {DefaultUsername}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding default users");
        }
    }
}
