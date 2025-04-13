using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using GrafanaProxy.Infrastructure.Services;

namespace GrafanaProxy.Management.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TenantsController : ControllerBase
    {
        private readonly IApplicationDbContext _context;
        private readonly ILogger<TenantsController> _logger;
        private readonly IApiKeyHasher _apiKeyHasher;

        public TenantsController(IApplicationDbContext context, ILogger<TenantsController> logger,
            IApiKeyHasher apiKeyHasher)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiKeyHasher = apiKeyHasher ?? throw new ArgumentNullException(nameof(apiKeyHasher));
        }

        // Placeholder for GET Tenants (Optional)
        // [HttpGet]
        // public async Task<ActionResult<IEnumerable<Tenant>>> GetTenants()
        // {
        //     // Implementation needed
        // }

        // Endpoint for Creating a Tenant and generating 2 API Keys
        [HttpPost]
        public async Task<ActionResult<Tenant>> CreateTenant([FromBody] CreateTenantRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Tenant name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(request.ShortCode))
            {
                return BadRequest("Tenant ShortCode cannot be empty.");
            }
            // Optional: Add validation for ShortCode format (e.g., length, characters)

            // Check if tenant name or shortcode already exists
            // Using || might be slightly less efficient than two separate queries but is concise
            var existingTenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Name.ToLower() == request.Name.ToLower()
                                          || t.ShortCode.ToLower() == request.ShortCode.ToLower());

            if (existingTenant != null)
            {
                return Conflict(existingTenant.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)
                    ? $"Tenant with name '{request.Name}' already exists."
                    : $"Tenant with ShortCode '{request.ShortCode}' already exists.");
            }

            var tenant = new Tenant
            {
                Name = request.Name,
                ShortCode = request.ShortCode
            };

            // Generate TWO plain text keys first
            var plainTextKey1 = GenerateNewApiKey();
            var plainTextKey2 = GenerateNewApiKey();

            // Hash the keys for storage
            var hashedKey1 = _apiKeyHasher.HashApiKey(plainTextKey1);
            var hashedKey2 = _apiKeyHasher.HashApiKey(plainTextKey2);

            var apiKey1 = new ApiKey
            {
                KeyValue = hashedKey1, // Store HASHED key
                IsActive = true,
                Tenant = tenant
            };
            var apiKey2 = new ApiKey
            {
                KeyValue = hashedKey2, // Store HASHED key
                IsActive = true,
                Tenant = tenant
            };

            tenant.ApiKeys.Add(apiKey1);
            tenant.ApiKeys.Add(apiKey2);

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation("Created Tenant '{TenantName}' with ID {TenantId} and 2 API Keys.", tenant.Name,
                tenant.Id);

            // IMPORTANT: Return the PLAIN TEXT keys only this one time.
            // Create a DTO to avoid returning the hashed keys from the Tenant object.
            var responseDto = new CreateTenantResponse
            {
                Id = tenant.Id,
                Name = tenant.Name,
                ShortCode = tenant.ShortCode,
                CreatedAt = tenant.CreatedAt,
                LastModifiedAt = tenant.LastModifiedAt,
                // Expose the generated plain text keys
                GeneratedApiKeys = [plainTextKey1, plainTextKey2]
            };

            // Return the DTO
            // Note: CreatedAtAction might not work perfectly if GetTenant returns the entity (with hashed keys)
            // Consider returning just Ok(responseDto) or creating a specific GetTenant DTO.
            return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.Id }, responseDto);
        }

        // Helper method to get a tenant by ID (Needed for CreatedAtAction)
        // Ideally, implement a proper GET endpoint
        [HttpGet("{tenantId}")]
        [ApiExplorerSettings(IgnoreApi = true)] // Hide from Swagger for now
        public async Task<ActionResult<Tenant>> GetTenant(int tenantId)
        {
            var tenant = await _context.Tenants
                .Include(t => t.ApiKeys)
                .Include(t => t.Permissions)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                return NotFound();
            }

            return Ok(tenant);
        }

        // Endpoint for Regenerating an API Key for a Tenant
        [HttpPost("{tenantId}/regenerateKey/{keyIndex}")]
        public async Task<IActionResult> RegenerateApiKey(int tenantId, int keyIndex)
        {
            if (keyIndex != 0 && keyIndex != 1)
            {
                return BadRequest("Invalid key index. Must be 0 or 1.");
            }

            // Find the tenant and include their existing API keys
            var tenant = await _context.Tenants
                .Include(t => t.ApiKeys)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                _logger.LogWarning("RegenerateApiKey failed: Tenant with ID {TenantId} not found.", tenantId);
                return NotFound($"Tenant with ID {tenantId} not found.");
            }

            // Ensure the tenant has the expected number of keys (should be 2)
            if (tenant.ApiKeys.Count != 2)
            {
                _logger.LogError("RegenerateApiKey failed: Tenant ID {TenantId} has {KeyCount} keys instead of 2.",
                    tenantId, tenant.ApiKeys.Count);
                // This indicates an inconsistent state, might need investigation
                return StatusCode(StatusCodes.Status500InternalServerError, "Tenant key data is inconsistent.");
            }

            // Get the key at the specified index (order might depend on retrieval, be careful if order matters)
            // Assuming the order is consistent for now, or sort if necessary.
            var apiKeyToUpdate = tenant.ApiKeys.OrderBy(k => k.Id).ElementAt(keyIndex); // Order by ID for consistency

            // Generate NEW plain text key
            var newPlainTextKey = GenerateNewApiKey();
            // Hash the new key for storage
            var newHashedKey = _apiKeyHasher.HashApiKey(newPlainTextKey);

            apiKeyToUpdate.KeyValue = newHashedKey; // Store HASHED key
            apiKeyToUpdate.CreatedAt = DateTime.UtcNow; // Corrected property name
            apiKeyToUpdate.IsActive = true; // Ensure it's active

            await _context.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation(
                "Regenerated API Key at index {KeyIndex} for Tenant '{TenantName}' (ID: {TenantId}).", keyIndex,
                tenant.Name, tenantId);

            // IMPORTANT: Return the PLAIN TEXT key only this one time.
            return Ok(new { NewApiKey = newPlainTextKey });
        }


        // Endpoint for Associating a Tenant with a Dashboard
        [HttpPost("{tenantId}/dashboards")]
        public async Task<IActionResult> AddDashboardPermission(int tenantId,
            [FromBody] AddDashboardPermissionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DashboardUid))
            {
                return BadRequest("Dashboard UID cannot be empty.");
            }

            // Find the tenant
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("AddDashboardPermission failed: Tenant with ID {TenantId} not found.", tenantId);
                return NotFound($"Tenant with ID {tenantId} not found.");
            }

            // Check if this specific permission already exists
            var permissionExists = await _context.TenantDashboardPermissions
                .AnyAsync(p => p.TenantId == tenantId && p.DashboardUid.ToLower() == request.DashboardUid.ToLower());

            if (permissionExists)
            {
                _logger.LogInformation(
                    "AddDashboardPermission skipped: Tenant '{TenantName}' (ID: {TenantId}) already has permission for Dashboard '{DashboardUid}'.",
                    tenant.Name, tenantId, request.DashboardUid);
                // Return Conflict or Ok depending on desired idempotency behavior
                return Conflict($"Tenant already has permission for dashboard '{request.DashboardUid}'.");
            }

            // Create and add the new permission
            var newPermission = new TenantDashboardPermission
            {
                TenantId = tenantId,
                DashboardUid = request.DashboardUid
            };

            _context.TenantDashboardPermissions.Add(newPermission);
            await _context.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation(
                "Added permission for Tenant '{TenantName}' (ID: {TenantId}) to access Dashboard '{DashboardUid}'.",
                tenant.Name, tenantId, request.DashboardUid);

            // Return success (consider returning the created permission object if useful)
            return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.Id },
                newPermission); // Reuse GetTenant or create a specific GetPermission endpoint
        }

        // --- Response DTO (can be moved) ---
        public class CreateTenantResponse
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string ShortCode { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime LastModifiedAt { get; set; }
            public List<string> GeneratedApiKeys { get; set; } = new List<string>();
        }

        // --- Helper for generating keys (consider a dedicated service) ---
        private string GenerateNewApiKey()
        {
            // Simple GUID based key generation. Replace with a more robust method if needed.
            return Guid.NewGuid().ToString();
        }

        // --- Request Models (can be moved to a separate file/folder) ---
        public class CreateTenantRequest
        {
            [Required(AllowEmptyStrings = false)] public string Name { get; set; } = string.Empty;
            [Required(AllowEmptyStrings = false)] public string ShortCode { get; set; } = string.Empty;
        }

        public class AddDashboardPermissionRequest
        {
            [Required(AllowEmptyStrings = false)] public string DashboardUid { get; set; } = string.Empty;
        }
    }
}