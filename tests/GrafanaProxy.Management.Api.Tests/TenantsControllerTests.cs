using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using GrafanaProxy.Management.Api.Controllers;
using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using GrafanaProxy.Infrastructure.Persistence; // Needed for concrete DbContext
using Microsoft.AspNetCore.Http; // For status codes

namespace GrafanaProxy.Management.Api.Tests
{
    public class TenantsControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly Mock<ILogger<TenantsController>> _loggerMock;
        private readonly TenantsController _controller;

        // Setup common resources for tests
        public TenantsControllerTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Unique DB per test run
                .Options;
            _dbContext = new ApplicationDbContext(options);
            _loggerMock = new Mock<ILogger<TenantsController>>();
            _controller = new TenantsController(_dbContext, _loggerMock.Object);
        }

        // Dispose the DbContext after tests
        public void Dispose()
        {
            _dbContext.Dispose();
            GC.SuppressFinalize(this);
        }

        // Helper to seed a tenant with 2 keys
        private async Task<Tenant> SeedTenantWithKeysAsync(string name = "Test Tenant", string shortCode = null)
        {
            var tenant = new Tenant { Name = name, ShortCode = shortCode };
            tenant.ApiKeys.Add(new ApiKey { KeyValue = "key1", Tenant = tenant });
            tenant.ApiKeys.Add(new ApiKey { KeyValue = "key2", Tenant = tenant });
            _dbContext.Tenants.Add(tenant);
            await _dbContext.SaveChangesAsync(); // This will trigger timestamp logic
            return tenant;
        }

        [Fact]
        public async Task CreateTenant_WhenCalled_ReturnsCreatedAtActionResultAndAddsTenantWithTwoApiKeysAndTimestamps()
        {
            // Arrange
            var request = new TenantsController.CreateTenantRequest { Name = "New Tenant" };
            var initialTime = DateTime.UtcNow;

            // Act
            var result = await _controller.CreateTenant(request);

            // Assert
            var createdAtActionResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            var tenant = Assert.IsType<Tenant>(createdAtActionResult.Value);
            Assert.Equal(request.Name, tenant.Name);
            Assert.Equal(2, tenant.ApiKeys.Count);
            Assert.True(tenant.CreatedAt >= initialTime);
            Assert.True(tenant.LastModifiedAt >= initialTime);
            Assert.Equal(tenant.CreatedAt, tenant.LastModifiedAt);

            var tenantInDb = await _dbContext.Tenants.Include(t => t.ApiKeys).FirstAsync(t => t.Id == tenant.Id);
            Assert.NotNull(tenantInDb);
            Assert.Equal(request.Name, tenantInDb.Name);
            Assert.Equal(request.ShortCode, tenantInDb.ShortCode);
            Assert.Equal(2, tenantInDb.ApiKeys.Count);
             Assert.True(tenantInDb.CreatedAt >= initialTime);
             Assert.True(tenantInDb.LastModifiedAt >= initialTime);
             Assert.Equal(tenantInDb.CreatedAt, tenantInDb.LastModifiedAt);
            foreach (var key in tenantInDb.ApiKeys)
            {
                Assert.True(key.CreatedAt >= initialTime);
                Assert.True(key.LastModifiedAt >= initialTime);
                Assert.Equal(key.CreatedAt, key.LastModifiedAt);
            }
        }

        [Fact]
        public async Task CreateTenant_WhenShortCodeExists_ReturnsConflict()
        {
            // Arrange
            await SeedTenantWithKeysAsync("Existing Name", "EXISTING_CODE");
            var request = new TenantsController.CreateTenantRequest { Name = "New Name", ShortCode = "EXISTING_CODE" };

            // Act
            var result = await _controller.CreateTenant(request);

            // Assert
            var conflictResult = Assert.IsType<ConflictObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
            Assert.Contains("ShortCode", conflictResult.Value?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task CreateTenant_WhenNameExists_ReturnsConflict()
        {
            // Arrange
            await SeedTenantWithKeysAsync("Existing Tenant");
            var request = new TenantsController.CreateTenantRequest { Name = "Existing Tenant" };

            // Act
            var result = await _controller.CreateTenant(request);

            // Assert
            var conflictResult = Assert.IsType<ConflictObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
        }

        [Fact]
        public async Task RegenerateApiKey_WithValidIndex_ReturnsOkAndUpdatesKey()
        {
            // Arrange
            var tenant = await SeedTenantWithKeysAsync();
            var originalKey = tenant.ApiKeys.OrderBy(k => k.Id).First().KeyValue;
            var originalCreationDate = tenant.ApiKeys.OrderBy(k => k.Id).First().CreatedAt;
            var initialModTime = tenant.ApiKeys.OrderBy(k => k.Id).First().LastModifiedAt;
            await Task.Delay(10); // Ensure time progresses slightly for timestamp check

            // Act
            var result = await _controller.RegenerateApiKey(tenant.Id, 0);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic value = okResult.Value ?? throw new NullReferenceException("Result value is null");
            string newKeyValue = value.GetType().GetProperty("NewApiKey")?.GetValue(value, null) ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(newKeyValue));
            Assert.NotEqual(originalKey, newKeyValue);

            var keyInDb = await _dbContext.ApiKeys.FindAsync(tenant.ApiKeys.OrderBy(k => k.Id).First().Id);
            Assert.NotNull(keyInDb);
            Assert.Equal(newKeyValue, keyInDb.KeyValue);
            Assert.True(keyInDb.IsActive);
            Assert.Equal(originalCreationDate, keyInDb.CreatedAt); // CreatedAt should NOT change
            Assert.True(keyInDb.LastModifiedAt > initialModTime); // LastModifiedAt SHOULD change
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public async Task RegenerateApiKey_WithInvalidIndex_ReturnsBadRequest(int keyIndex)
        {
            // Arrange
            var tenant = await SeedTenantWithKeysAsync();

            // Act
            var result = await _controller.RegenerateApiKey(tenant.Id, keyIndex);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task RegenerateApiKey_WhenTenantNotFound_ReturnsNotFound()
        {
            // Arrange
            var nonExistentTenantId = 999;

            // Act
            var result = await _controller.RegenerateApiKey(nonExistentTenantId, 0);

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task RegenerateApiKey_WhenTenantHasIncorrectKeyCount_ReturnsInternalServerError()
        {
            // Arrange
            // Seed a tenant but manually mess up the key count
            var tenant = new Tenant { Name = "Inconsistent Tenant" };
            tenant.ApiKeys.Add(new ApiKey { KeyValue = "onlyone", Tenant = tenant }); // Only one key
            _dbContext.Tenants.Add(tenant);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _controller.RegenerateApiKey(tenant.Id, 0);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task AddDashboardPermission_WhenCalled_ReturnsCreatedAtActionAndAddsPermission()
        {
            // Arrange
            var tenant = await SeedTenantWithKeysAsync();
            var request = new TenantsController.AddDashboardPermissionRequest { DashboardUid = "dash123" };
             var initialTime = DateTime.UtcNow;

            // Act
            var result = await _controller.AddDashboardPermission(tenant.Id, request);

            // Assert
            var createdAtActionResult = Assert.IsType<CreatedAtActionResult>(result);
            var permission = Assert.IsType<TenantDashboardPermission>(createdAtActionResult.Value);
            Assert.Equal(tenant.Id, permission.TenantId);
            Assert.Equal(request.DashboardUid, permission.DashboardUid);
             Assert.True(permission.CreatedAt >= initialTime);
             Assert.True(permission.LastModifiedAt >= initialTime);
             Assert.Equal(permission.CreatedAt, permission.LastModifiedAt);

            var permissionInDb = await _dbContext.TenantDashboardPermissions.FirstAsync(p => p.Id == permission.Id);
            Assert.NotNull(permissionInDb);
            Assert.Equal(request.DashboardUid, permissionInDb.DashboardUid);
             Assert.True(permissionInDb.CreatedAt >= initialTime);
             Assert.True(permissionInDb.LastModifiedAt >= initialTime);
             Assert.Equal(permissionInDb.CreatedAt, permissionInDb.LastModifiedAt);
        }

        [Fact]
        public async Task AddDashboardPermission_WhenTenantNotFound_ReturnsNotFound()
        {
             // Arrange
            var nonExistentTenantId = 999;
            var request = new TenantsController.AddDashboardPermissionRequest { DashboardUid = "dash123" };

            // Act
            var result = await _controller.AddDashboardPermission(nonExistentTenantId, request);

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task AddDashboardPermission_WhenPermissionExists_ReturnsConflict()
        {
            // Arrange
            var tenant = await SeedTenantWithKeysAsync();
            var dashboardUid = "existing-dash";
            _dbContext.TenantDashboardPermissions.Add(new TenantDashboardPermission { TenantId = tenant.Id, DashboardUid = dashboardUid });
            await _dbContext.SaveChangesAsync();
            var request = new TenantsController.AddDashboardPermissionRequest { DashboardUid = dashboardUid };

            // Act
            var result = await _controller.AddDashboardPermission(tenant.Id, request);

            // Assert
            var conflictResult = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
        }
    }
} 