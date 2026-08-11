using Microsoft.AspNetCore.Identity;

namespace CMIS_IyaSoft.Data
{
    public static class DbSeeder
    {
        public static async Task SeedRolesAndUsersAsync(IServiceProvider services)
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var configuration = services.GetRequiredService<IConfiguration>();

            // 1. Ensure Roles Exist
            string[] roles = { "Admin", "Manager", "User" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // 2. Read strictly from Environment / AppSettings (No hardcoded fallbacks)
            var adminEmail = configuration["SeedData:AdminEmail"];
            var adminPassword = configuration["SeedData:AdminPassword"];

            var managerEmail = configuration["SeedData:ManagerEmail"];
            var managerPassword = configuration["SeedData:ManagerPassword"];

            var userEmail = configuration["SeedData:UserEmail"];
            var userPassword = configuration["SeedData:UserPassword"];

            // 3. Helper Method to Seed Account
            async Task CreateUserIfNotExist(string? email, string? password, string role)
            {
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    // Skip seeding if credentials aren't provided in environment
                    return;
                }

                if (await userManager.FindByEmailAsync(email) == null)
                {
                    var user = new IdentityUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };

                    var result = await userManager.CreateAsync(user, password);
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, role);
                    }
                }
            }

            await CreateUserIfNotExist(adminEmail, adminPassword, "Admin");
            await CreateUserIfNotExist(managerEmail, managerPassword, "Manager");
            await CreateUserIfNotExist(userEmail, userPassword, "User");
        }
    }
}