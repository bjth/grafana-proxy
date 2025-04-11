using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
// using Yarp.ReverseProxy.EntityFrameworkCore; // No longer needed

namespace GrafanaProxy.Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(IApplicationDbContext context, ILogger logger)
        {
            // Cast to concrete type to access DbSets
            if (context is not ApplicationDbContext dbContext)
            {
                logger.LogError("Cannot seed data. DbContext is not of type ApplicationDbContext.");
                return;
            }

            // --- Seed YARP Config --- REMOVED
            // YARP configuration needs to be added to the database tables directly.
            // The schema for these tables is defined internally by YARP.
            // You can typically find the schema by creating an initial migration
            // after calling `.AddReverseProxy().LoadFromEntityFrameworkCore()` but before adding custom entities.
            // Or check YARP documentation/source code.
            logger.LogInformation("Skipping YARP config seeding. Add YARP routes/clusters directly to the database tables or use appsettings.json.");

            // Seed Tenants, Permissions, and API Keys if empty
            if (!await dbContext.Tenants.AnyAsync())
            {
                logger.LogInformation("Seeding initial Tenant, Permissions, and API Keys.");

                // --- Tenant 1 ---
                var tenant1 = new Tenant { Name = "Tenant ABC" };
                dbContext.Tenants.Add(tenant1);
                // Save Tenant first to get its ID
                await dbContext.SaveChangesAsync(CancellationToken.None);

                // Add Permissions for Tenant 1
                dbContext.TenantDashboardPermissions.Add(new TenantDashboardPermission
                {
                    TenantId = tenant1.Id, // Use generated Tenant ID
                    DashboardUid = "gJr564dVz"
                });

                // Add API Keys for Tenant 1
                dbContext.ApiKeys.Add(new ApiKey
                {
                     TenantId = tenant1.Id,
                     KeyValue = "abc-key-1", // Replace with a securely generated key in practice
                     IsActive = true
                });
                 dbContext.ApiKeys.Add(new ApiKey
                 {
                     TenantId = tenant1.Id,
                     KeyValue = "abc-key-2", // Second key for the tenant
                     IsActive = true
                 });

                // --- Tenant 2 ---
                var tenant2 = new Tenant { Name = "Tenant XYZ" };
                dbContext.Tenants.Add(tenant2);
                await dbContext.SaveChangesAsync(CancellationToken.None);

                dbContext.TenantDashboardPermissions.Add(new TenantDashboardPermission
                {
                     TenantId = tenant2.Id,
                     DashboardUid = "some-other-dashboard-uid"
                 });
                 dbContext.ApiKeys.Add(new ApiKey
                 {
                     TenantId = tenant2.Id,
                     KeyValue = "xyz-key-1",
                     IsActive = true
                 });
                 // Add only one key for this tenant

                await dbContext.SaveChangesAsync(CancellationToken.None);
                logger.LogInformation("Finished seeding Tenants, Permissions, and API Keys.");
            }
             else
            {
                 logger.LogInformation("Tenant data already exists, skipping seeding.");
            }
        }
    }
} 