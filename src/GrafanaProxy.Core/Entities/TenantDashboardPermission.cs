namespace GrafanaProxy.Core.Entities
{
    public class TenantDashboardPermission
    {
        public int Id { get; set; }
        public string DashboardUid { get; set; } = string.Empty; // Grafana Public Dashboard UID

        // Timestamps
        public DateTime CreatedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }

        // Add Foreign key to Tenant
        public int TenantId { get; set; }
        public virtual Tenant Tenant { get; set; } = null!;
    }
} 