using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GrafanaProxy.Infrastructure.Persistence
{
    // CORRECTED: No IReverseProxyDbContext, no YARP DbSets
    public class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Custom Entities
        public DbSet<TenantDashboardPermission> TenantDashboardPermissions { get; set; } = null!;
        public DbSet<Tenant> Tenants { get; set; } = null!;
        public DbSet<ApiKey> ApiKeys { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Tenant
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Name).IsRequired();
                entity.HasMany(t => t.ApiKeys).WithOne(k => k.Tenant).HasForeignKey(k => k.TenantId);
                entity.HasMany(t => t.Permissions).WithOne(p => p.Tenant).HasForeignKey(p => p.TenantId);
                entity.HasIndex(t => t.Name).IsUnique();
                entity.HasIndex(t => t.ShortCode).IsUnique();
            });

            // Configure ApiKey
            modelBuilder.Entity<ApiKey>(entity =>
            {
                entity.HasKey(k => k.Id);
                entity.Property(k => k.KeyValue).IsRequired();
                entity.Property(k => k.IsActive).IsRequired();
            });

            // Configure TenantDashboardPermission
            modelBuilder.Entity<TenantDashboardPermission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.DashboardUid }).IsUnique();
                entity.Property(e => e.DashboardUid).IsRequired();
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            SetTimestamps();
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            SetTimestamps();
            return base.SaveChanges();
        }

        private void SetTimestamps()
        {
            var entries = ChangeTracker
                .Entries()
                .Where(e => e.Entity is Tenant or ApiKey or TenantDashboardPermission &&
                            (e.State == EntityState.Added || e.State == EntityState.Modified));

            foreach (var entityEntry in entries)
            {
                var now = DateTime.UtcNow;
                // Use reflection or pattern matching to set properties
                // Pattern matching is generally safer and more performant
                switch (entityEntry.Entity)
                {
                    case Tenant t:
                        t.LastModifiedAt = now;
                        if (entityEntry.State == EntityState.Added)
                        {
                            t.CreatedAt = now;
                        }
                        break;
                    case ApiKey k:
                        k.LastModifiedAt = now;
                        if (entityEntry.State == EntityState.Added)
                        {
                            k.CreatedAt = now;
                            // Ensure Created is also set if it wasn't already (e.g., during seeding)
                            // if (k.Created == default) k.Created = now;
                        }
                         // Ensure CreatedAt is set on update if it was missed (defensive)
                         else if (k.CreatedAt == default)
                         {
                             k.CreatedAt = now; // Or retrieve original value if possible
                         }
                        break;
                    case TenantDashboardPermission p:
                        p.LastModifiedAt = now;
                        if (entityEntry.State == EntityState.Added)
                        {
                            p.CreatedAt = now;
                        }
                        break;
                }
            }
        }
    }
} 