using GrafanaProxy.Core.Entities;
using Microsoft.EntityFrameworkCore; // For DbSet<>
// using Yarp.ReverseProxy.Configuration; // No longer needed here
using System.Threading;
using System.Threading.Tasks;

namespace GrafanaProxy.Application.Interfaces
{
    // Remove inheritance from IReverseProxyDbContext
    public interface IApplicationDbContext
    {
        DbSet<Tenant> Tenants { get; }
        DbSet<ApiKey> ApiKeys { get; }
        DbSet<TenantDashboardPermission> TenantDashboardPermissions { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
        int SaveChanges(); // If synchronous save is ever needed
    }
} 