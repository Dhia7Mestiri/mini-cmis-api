using CMIS_IyaSoft.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMIS_IyaSoft.Tests.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Generated ONCE per factory instance (xUnit shares one factory per test class
    // via IClassFixture) and captured in the closure below - NOT inside the options
    // lambda. AddDbContext's options callback re-runs on every DbContext resolution
    // (every HTTP request gets its own DI scope -> new DbContext -> lambda re-runs).
    // A Guid generated inside that lambda would give every request its own empty
    // in-memory store, silently orphaning the app's own startup seed data.
    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs branches on IsProduction() / this env var to decide between
        // Database.MigrateAsync() (relational-only, throws on the InMemory provider)
        // and Database.EnsureCreatedAsync() (works with InMemory). Setting this
        // forces the EnsureCreatedAsync path without needing "Production" as the
        // ASP.NET Core environment name (which would also suppress dev-only
        // middleware like the Swagger UI and detailed exception pages).
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");

        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });
        });
    }
}
