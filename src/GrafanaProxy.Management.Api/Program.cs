using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;
using GrafanaProxy.Infrastructure.Logging;
using GrafanaProxy.Infrastructure.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

// --- 1. Configure Serilog --- 
// Read configuration from appsettings.json
Log.Logger = SerilogConfigurationHelper.Configure(new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build());

try
{
    Log.Information("Starting GrafanaProxy.Management.Api host");

    var builder = WebApplication.CreateBuilder(args);

    // --- 2. Clear default providers and Use Serilog ---
    builder.Host.UseSerilog(); // Use Serilog for all logging

    // Add services to the container.

    // --- Add DbContext ---
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"),
            b => b.MigrationsAssembly("GrafanaProxy.Infrastructure"))); // Specify the migrations assembly

    // --- Register DbContext Interface ---
    // Register ApplicationDbContext also as IApplicationDbContext for dependency injection
    builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

    builder.Services.AddControllers();
    builder.Services.AddScoped<IApiKeyHasher, ApiKeyHasher>(); // Register the hasher

    // --- Add Rate Limiting --- 
    builder.Services.AddRateLimiter(_ => _
        .AddFixedWindowLimiter(policyName: "fixed", options =>
        {
            options.PermitLimit = 100; // Max 100 requests
            options.Window = TimeSpan.FromMinutes(1); // Per 1 minute window
            options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            options.QueueLimit = 5; // Max 5 queued requests
        }));

    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();

    var app = builder.Build();

    // Configure the HTTP request pipeline.

     // --- Serilog Request Logging (Optional but recommended) ---
    app.UseSerilogRequestLogging(); // Adds request logging middleware

    // --- Use Rate Limiter --- 
    app.UseRateLimiter(); // Apply rate limiting middleware globally

    if (app.Environment.IsDevelopment())
    {
        // app.UseSwagger(); // Also commented out for now - required for serving swagger.json
        // app.UseSwaggerUI(); // Removed to comply with rules
    }

    app.UseHttpsRedirection();

    // app.UseAuthorization(); // Authorization can be added later if needed

    app.MapControllers()
       .RequireRateLimiting("fixed"); // Apply the "fixed" policy to all controllers

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GrafanaProxy.Management.Api host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
