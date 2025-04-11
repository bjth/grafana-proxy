using GrafanaProxy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing; // Needed for GetRouteValue
using GrafanaProxy.Infrastructure.Services; // Added using for hasher
using GrafanaProxy.Core.Entities; // Added using for ApiKey

namespace GrafanaProxy.Web.Authorization
{
    public class TenantAccessHandler : AuthorizationHandler<TenantAccessRequirement>
    {
        private readonly ILogger<TenantAccessHandler> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _serviceProvider; // To resolve scoped DbContext
        private readonly IApiKeyHasher _apiKeyHasher; // Added hasher

        // Inject IServiceProvider to resolve scoped DbContext within the handler
        public TenantAccessHandler(ILogger<TenantAccessHandler> logger, IHttpContextAccessor httpContextAccessor, IServiceProvider serviceProvider, IApiKeyHasher apiKeyHasher) // Inject hasher
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _serviceProvider = serviceProvider;
            _apiKeyHasher = apiKeyHasher; // Store hasher
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantAccessRequirement requirement)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                _logger.LogWarning("HttpContext is null, cannot perform tenant access check.");
                context.Fail();
                return;
            }

            // --- 1. Get Provided Plain Text API Key (Header preferred, then Query String) --- 
            string? plainTextApiKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
            string keySource = "Header"; // For logging
            if (string.IsNullOrEmpty(plainTextApiKey))
            {
                 plainTextApiKey = httpContext.Request.Query["apiKey"].FirstOrDefault();
                 keySource = "Query";
                 if (string.IsNullOrEmpty(plainTextApiKey))
                 {
                    _logger.LogWarning("API key not found in header (X-Api-Key) or query string (apiKey).");
                    context.Fail(new AuthorizationFailureReason(this, "API key missing."));
                    return;
                 }
                 else
                 {
                     _logger.LogDebug("API key found in {KeySource} string.", keySource);
                 }
            }
            else
            {
                 _logger.LogDebug("API key found in {KeySource}.", keySource);
            }

            // --- 2. Get Dashboard UID from Route ---
            string? dashboardUid = null;
            var routingFeature = httpContext.Features.Get<IRoutingFeature>();
            if (routingFeature?.RouteData != null)
            {
                // Try getting 'dashboardUid' directly
                if (routingFeature.RouteData.Values.TryGetValue("dashboardUid", out var uidValue))
                {
                    dashboardUid = uidValue?.ToString();
                }

                // Fallback/Alternative: If UID is part of a catch-all 'remainder'
                if (string.IsNullOrEmpty(dashboardUid) && routingFeature.RouteData.Values.TryGetValue("remainder", out var remainderValue))
                {
                    var remainder = remainderValue?.ToString();
                    if (!string.IsNullOrEmpty(remainder))
                    {
                        var parts = remainder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            dashboardUid = parts[0];
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(dashboardUid))
            {
                _logger.LogWarning("Could not extract Dashboard UID from route path.");
                context.Fail(new AuthorizationFailureReason(this, "Dashboard UID missing from request path."));
                return;
            }

            _logger.LogDebug("Attempting authorization for API Key (source: {KeySource}) to Dashboard '{DashboardUid}'", keySource, dashboardUid);

            // --- 3. Find potential matching API Keys and Verify Hash ---
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            // 3a. Find *all* active API Keys (can't filter by hash directly)
            // This could be inefficient if there are many keys. Consider optimizations if needed.
            var activeKeys = await dbContext.ApiKeys
                .Where(k => k.IsActive)
                .Include(k => k.Tenant) // Eager load the Tenant
                .ToListAsync(); // Fetch all active keys

            ApiKey? matchedApiKeyEntity = null;
            foreach(var potentialKey in activeKeys)
            {
                if (_apiKeyHasher.VerifyApiKey(plainTextApiKey, potentialKey.KeyValue))
                {
                    matchedApiKeyEntity = potentialKey; // Found a match!
                    break; 
                }
            }
            
            // 3b. Check if a key was found and verified
            if (matchedApiKeyEntity is null || matchedApiKeyEntity.Tenant is null)
            {
                _logger.LogWarning("Invalid or inactive API Key presented (verification failed or key/tenant not found).");
                context.Fail(new AuthorizationFailureReason(this, "Invalid API Key."));
                return;
            }

            var tenantId = matchedApiKeyEntity.TenantId;
            var tenantName = matchedApiKeyEntity.Tenant?.Name ?? "Unknown"; // Use null-conditional access

            // 3c. Check if that Tenant has permission for the dashboard
            bool hasPermission = await dbContext.TenantDashboardPermissions
                .AnyAsync(p => p.TenantId == tenantId &&
                               p.DashboardUid.ToUpper() == dashboardUid.ToUpper()); // Case-insensitive check for UID

            if (hasPermission)
            {
                _logger.LogInformation("Authorization succeeded for TenantId '{TenantId}' ({TenantName}) to Dashboard '{DashboardUid}' via API Key", tenantId, tenantName, dashboardUid);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("Authorization failed for TenantId '{TenantId}' ({TenantName}) to Dashboard '{DashboardUid}' - Permission not found.", tenantId, tenantName, dashboardUid);
                context.Fail(new AuthorizationFailureReason(this, "Tenant does not have permission for this dashboard."));
            }
        }
    }
} 