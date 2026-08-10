using Microsoft.AspNetCore.Identity;

namespace CMIS_IyaSoft.Data;

public static class DbSeeder
{
    public static async Task SeedRolesAndUsersAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

        // 1. Define Roles
        string[] roles = { "Admin", "Manager", "User" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Define Initial Demo Users
        var seedUsers = new[]
        {
            new { Email = "admin@cmis.com", Password = "AdminPass123!", Role = "Admin" },
            new { Email = "manager@cmis.com", Password = "ManagerPass123!", Role = "Manager" },
            new { Email = "user@cmis.com", Password = "UserPass123!", Role = "User" }
        };

        foreach (var userDef in seedUsers)
        {
            var user = await userManager.FindByEmailAsync(userDef.Email);
            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = userDef.Email,
                    Email = userDef.Email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, userDef.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, userDef.Role);
                }
            }
        }
    }
}