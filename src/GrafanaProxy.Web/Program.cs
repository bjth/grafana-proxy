using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Infrastructure.Persistence;
using GrafanaProxy.Web.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;
using Serilog;
using GrafanaProxy.Infrastructure.Logging;
using GrafanaProxy.Infrastructure.Services;
using GrafanaProxy.Web.Middleware; // Added for custom cache middleware

// --- 1. Configure Serilog --- 
// Read configuration from appsettings.json
Log.Logger = SerilogConfigurationHelper.Configure(new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build());

try
{
    Log.Information("Starting GrafanaProxy.Web host");

    var builder = WebApplication.CreateBuilder(args);

    // --- 2. Clear default providers and Use Serilog ---
    builder.Host.UseSerilog(); // Use Serilog for all logging

    // --- Configuration ---
    var configuration = builder.Configuration;

    // Need HttpContextAccessor for the Auth Handler
    builder.Services.AddHttpContextAccessor();

    // --- Database Context ---
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(configuration.GetConnectionString("DefaultConnection"),
            b => b.MigrationsAssembly("GrafanaProxy.Infrastructure")));

    // Register IApplicationDbContext
    builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

    // --- YARP Configuration ---
    // Load YARP configuration from the IConfiguration (appsettings.json)
    builder.Services.AddReverseProxy()
        .LoadFromConfig(configuration.GetSection("ReverseProxy")); // Use appsettings

    // --- Authentication ---
    // Add JWT Bearer Authentication
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Configure JWT validation parameters (replace with your actual values)
            options.Authority = configuration["Auth:Authority"]; // e.g., "https://your-auth-server.com/"
            options.Audience = configuration["Auth:Audience"];   // e.g., "your-api-audience"

            // In development, you might disable HTTPS metadata requirement
            if (builder.Environment.IsDevelopment())
            {
                options.RequireHttpsMetadata = false;
            }

            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // ValidIssuer = configuration["Auth:Authority"], // Often inferred from Authority
                // ValidAudience = configuration["Auth:Audience"], // Already set above
                // IssuerSigningKey = ... // If using symmetric key, provide it here. Usually keys are fetched from Authority's metadata endpoint.
            };
        });

    // --- Authorization ---
    builder.Services.AddAuthorization(options =>
    {
        // Policy requiring valid tenant access to a specific dashboard
        options.AddPolicy("TenantCanAccessDashboard", policy =>
        {
            policy.RequireAuthenticatedUser(); // Must be authenticated first
            policy.Requirements.Add(new TenantAccessRequirement()); // Add our custom requirement
        });

        // You might have other policies here
        options.AddPolicy("AuthenticatedUser", policy =>
        {
            policy.RequireAuthenticatedUser();
        });
    });

    // Register the custom Authorization Handler
    builder.Services.AddSingleton<IAuthorizationHandler, TenantAccessHandler>(); // Can be Singleton if using IServiceProvider for DbContext
    builder.Services.AddScoped<IApiKeyHasher, ApiKeyHasher>(); // Register the hasher

    // --- Other Services ---
    builder.Services.AddResponseCaching(); // Added response caching services
    
    // builder.Services.AddControllers(); // Keep controllers if you add custom API endpoints later
    // builder.Services.AddEndpointsApiExplorer();
    // builder.Services.AddOpenApi();

    var app = builder.Build();

    // --- Database Migrations & Seeding ---
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var dbContext = services.GetRequiredService<IApplicationDbContext>();
            var logger = services.GetRequiredService<ILogger<Program>>(); // Get logger for seeding

            // Apply Migrations
            logger.LogInformation("Applying database migrations...");
            if (dbContext is DbContext concreteContext)
            {
                 await concreteContext.Database.MigrateAsync();
                 logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                logger.LogError("Could not apply migrations: DbContext is not a concrete DbContext.");
            }

            // Seed Data
            await DbInitializer.SeedAsync(dbContext, logger);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred during database migration or seeding.");
            // Decide if you want to stop the application here
            // throw;
        }
    }

    // --- Middleware Pipeline ---
    // if (app.Environment.IsDevelopment())
    // {
    //     app.MapOpenApi();
    // }

    app.UseResponseCaching(); // Add response caching middleware
    app.UseGrafanaProxyResponseCaching(); // Add our custom middleware for proxy cache headers

    app.UseHttpsRedirection();

    app.UseRouting(); // Routing must come before AuthN/AuthZ

    app.UseAuthentication(); // Add Authentication middleware
    app.UseAuthorization(); // Add Authorization middleware

    // Map controllers if any exist
    // app.MapControllers();

    // Map YARP Reverse Proxy
    // Specific routes in the YARP config (DB) will need to specify `.RequireAuthorization("TenantCanAccessDashboard")`
    app.MapReverseProxy();

    // --- Serilog Request Logging (Optional but recommended) ---
    app.UseSerilogRequestLogging(); // Adds request logging middleware

    app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "GrafanaProxy.Web host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
