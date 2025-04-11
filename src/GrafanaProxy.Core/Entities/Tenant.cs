using System.Collections.Generic;

namespace GrafanaProxy.Core.Entities
{
    public class Tenant
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortCode { get; set; } = string.Empty;

        // Timestamps
        public DateTime CreatedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }

        // Navigation properties
        public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
        public virtual ICollection<TenantDashboardPermission> Permissions { get; set; } = new List<TenantDashboardPermission>();
    }
} 