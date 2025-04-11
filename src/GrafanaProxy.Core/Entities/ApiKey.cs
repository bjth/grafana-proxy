using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace GrafanaProxy.Core.Entities
{
    public class ApiKey
    {
        public int Id { get; set; }
        public string KeyValue { get; set; } = string.Empty; // Now stores the HASH

        // --- Testing Only --- 
        // This property is not mapped to the database and only used for 
        // retrieving the plain text key during integration/auth tests.
        [NotMapped]
        public string? PlainTextKeyValueForTesting { get; set; }
        // --- End Testing Only ---

        public DateTime CreatedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }
        public bool IsActive { get; set; } = true;

        // Foreign key to Tenant
        public int TenantId { get; set; }
        public virtual Tenant Tenant { get; set; } = null!;
    }
} 