using Microsoft.AspNetCore.Authorization;

namespace GrafanaProxy.Web.Authorization
{
    /// <summary>
    /// Requirement that the authenticated user's tenant claim must have permission
    /// to access the requested Grafana dashboard UID.
    /// </summary>
    public class TenantAccessRequirement : IAuthorizationRequirement
    {
        // This requirement doesn't need specific properties itself,
        // as the necessary information (TenantId, DashboardUid) will be
        // extracted from the HttpContext within the handler.
    }
} 