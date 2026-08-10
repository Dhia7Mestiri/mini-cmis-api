using CMIS_IyaSoft.Data;
using CMIS_IyaSoft.Middleware;
using CMIS_IyaSoft.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database & Health Checks
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// .NET 8 Identity API & Authorization
builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<IdentityUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// ➕ Register CMIS Service for Dependency Injection
builder.Services.AddScoped<ICmisService, CmisService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Serve index.html static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Map built-in Identity endpoints (/auth/register, /auth/login)
app.MapGroup("/auth").MapIdentityApi<IdentityUser>();
app.MapHealthChecks("/health");
app.MapControllers();

// Seed Database and Identity Users on Startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();

        // 1. Ensure database schema is up to date
        context.Database.Migrate();

        // 2. Seed CMIS Domain Data (Objects & Types)
        DbInitializer.Initialize(context);

        // 3. Seed Identity Roles & Accounts (Admin, Manager, User)
        await DbSeeder.SeedRolesAndUsersAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();