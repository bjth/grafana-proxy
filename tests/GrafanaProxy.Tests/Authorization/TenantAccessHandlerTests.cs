using Xunit;
using Moq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using GrafanaProxy.Web.Authorization;
using GrafanaProxy.Application.Interfaces;
using GrafanaProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using GrafanaProxy.Core.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using System.IO; // Needed for Path
using GrafanaProxy.Infrastructure.Services; // For IApiKeyHasher

namespace GrafanaProxy.Tests.Authorization
{
    public class TenantAccessHandlerTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        // private readonly ApplicationDbContext _dbContext; // We'll get context instances from the provider
        private readonly Mock<ILogger<TenantAccessHandler>> _mockLogger;
        private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
        private readonly DefaultHttpContext _httpContext;
        private readonly TenantAccessRequirement _requirement;
        private readonly string _dbPath; // Path for the test database file
        private readonly IApiKeyHasher _apiKeyHasher; // Added hasher for seeding

        public TenantAccessHandlerTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"grafana-proxy-test-{Guid.NewGuid()}.db");
            var connectionString = $"Data Source={_dbPath}";

            _requirement = new TenantAccessRequirement();
            _mockLogger = new Mock<ILogger<TenantAccessHandler>>();
            _apiKeyHasher = new ApiKeyHasher(); // Instantiate concrete hasher for test setup

            // Setup DbContext with SQLite file and DI Provider
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(connectionString),
                ServiceLifetime.Scoped);
            serviceCollection.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
            serviceCollection.AddSingleton<IApiKeyHasher>(_apiKeyHasher); // Register hasher instance

            _httpContext = new DefaultHttpContext();

            // Add a default valid API key to requests unless overridden by a specific test
            _httpContext.Request.Headers["X-Api-Key"] = "valid-default-key";

            _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            _mockHttpContextAccessor.Setup(_ => _.HttpContext).Returns(_httpContext);
            serviceCollection.AddScoped(_ => _mockHttpContextAccessor.Object);

            serviceCollection.AddSingleton(_mockLogger.Object);
            _serviceProvider = serviceCollection.BuildServiceProvider();

            // Ensure database schema is created and seed default key
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.EnsureCreated();

                var defaultTenant = new Tenant { Id = 99, Name = "DefaultTenant", ShortCode = "DEFAULT" };
                string defaultPlainText = "valid-default-key";
                var defaultHashedKey = _apiKeyHasher.HashApiKey(defaultPlainText);
                var defaultApiKey = new ApiKey { Tenant = defaultTenant, KeyValue = defaultHashedKey, IsActive = true, PlainTextKeyValueForTesting = defaultPlainText };
                context.Tenants.Add(defaultTenant);
                context.ApiKeys.Add(defaultApiKey);
                context.SaveChanges();
            }
        }

        // Helper to setup HttpContext for testing - Added back
        private void SetupTestContext(string apiKeyLocation, string? apiKey, ClaimsPrincipal? user = null)
        {
            // Clear existing headers/query
            _httpContext.Request.Headers.Clear();
            _httpContext.Request.QueryString = QueryString.Empty;

            if (!string.IsNullOrEmpty(apiKey))
            {
                 if (apiKeyLocation == "Header")
                 {
                    _httpContext.Request.Headers["X-Api-Key"] = apiKey;
                 }
                 else if (apiKeyLocation == "Query")
                 {
                    _httpContext.Request.QueryString = new QueryString($"?apiKey={apiKey}");
                 }
            }

            _httpContext.User = user ?? new ClaimsPrincipal(new ClaimsIdentity()); // Default to unauthenticated user
        }

        private async Task<(Tenant Tenant, ApiKey ApiKey, TenantDashboardPermission Permission)> SeedTenantAndPermission(int tenantId, string dashboardUid)
        {
             using var scope = _serviceProvider.CreateScope();
             var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

             Tenant? tenant = await context.Tenants.FindAsync(tenantId);
             ApiKey? apiKey = await context.ApiKeys.FirstOrDefaultAsync(k => k.TenantId == tenantId);
             string plainTextKey;

             if (tenant == null)
             {
                 tenant = new Tenant { Id = tenantId, Name = $"TestTenant{tenantId}", ShortCode = $"TEST{tenantId}" };
                 context.Tenants.Add(tenant);
             }

             if (apiKey == null)
             {
                 plainTextKey = Guid.NewGuid().ToString(); // Generate new key for this tenant
                 var hashedKey = _apiKeyHasher.HashApiKey(plainTextKey);
                 apiKey = new ApiKey { Tenant = tenant, KeyValue = hashedKey, IsActive = true, PlainTextKeyValueForTesting = plainTextKey };
                 context.ApiKeys.Add(apiKey);
             }
             else
             {
                 plainTextKey = apiKey.PlainTextKeyValueForTesting ?? Guid.NewGuid().ToString(); // Fallback if somehow missing
             }

            TenantDashboardPermission? permission = await context.TenantDashboardPermissions
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.DashboardUid == dashboardUid);

            if (permission == null)
            {
                 permission = new TenantDashboardPermission { TenantId = tenantId, DashboardUid = dashboardUid, Tenant = tenant };
                 context.TenantDashboardPermissions.Add(permission);
            }

             await context.SaveChangesAsync();
             return (tenant, apiKey, permission); // Return seeded entities
        }

        private AuthorizationHandlerContext CreateAuthorizationContext(ClaimsPrincipal user)
        {
            return new AuthorizationHandlerContext(
                new[] { _requirement },
                user,
                null); // Resource is not needed for this handler
        }

        private void SetupRouteData(string? dashboardUid, string? remainder = null)
        {
            var routeData = new RouteData();
            if (!string.IsNullOrEmpty(dashboardUid))
            {
                routeData.Values["dashboardUid"] = dashboardUid;
            }
             if (!string.IsNullOrEmpty(remainder))
            {
                routeData.Values["remainder"] = remainder;
            }
            _httpContext.Features.Set<IRoutingFeature>(new RoutingFeature { RouteData = routeData });
        }

        // --- Test Cases ---
        // Re-enabled tests

        [Theory]
        [InlineData("Header")]
        [InlineData("Query")]
        public async Task HandleRequirementAsync_ShouldSucceed_WhenValidKeyAndTenantHasPermission(string apiKeyLocation)
        {
             // Arrange
            var tenantId = 1;
            var dashboardUid = "dash-abc";
            var seededData = await SeedTenantAndPermission(tenantId, dashboardUid);
            var plainTextApiKey = seededData.ApiKey.PlainTextKeyValueForTesting;
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            SetupTestContext(apiKeyLocation, plainTextApiKey, user);
            SetupRouteData(dashboardUid);
            var context = CreateAuthorizationContext(user);
            // Pass the hasher instance from the service provider
            var handler = new TenantAccessHandler(_mockLogger.Object, _mockHttpContextAccessor.Object, _serviceProvider, _apiKeyHasher);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded, "Context should have succeeded.");
            Assert.False(context.HasFailed);
        }

        [Theory]
        [InlineData("Header")]
        [InlineData("Query")]
        public async Task HandleRequirementAsync_ShouldFail_WhenValidKeyButTenantLacksPermission(string apiKeyLocation)
        {
             // Arrange
            var tenantId = 2;
            var dashboardUid = "dash-xyz"; // Trying to access this
            var seededData = await SeedTenantAndPermission(tenantId, "dash-other"); // But tenant only has permission for this
            var plainTextApiKey = seededData.ApiKey.PlainTextKeyValueForTesting;
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            SetupTestContext(apiKeyLocation, plainTextApiKey, user);
            SetupRouteData(dashboardUid);
            var context = CreateAuthorizationContext(user);
             // Pass the hasher instance from the service provider
            var handler = new TenantAccessHandler(_mockLogger.Object, _mockHttpContextAccessor.Object, _serviceProvider, _apiKeyHasher);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
            Assert.True(context.HasFailed);
            Assert.Contains("Tenant does not have permission for this dashboard", context.FailureReasons.First().Message);
        }

        [Fact]
        public async Task HandleRequirementAsync_ShouldFail_WhenApiKeyIsMissingCompletely()
        {
             // Arrange
            var dashboardUid = "dash-abc";
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            SetupTestContext("None", null, user); // Explicitly no key
            SetupRouteData(dashboardUid);
            var context = CreateAuthorizationContext(user);
             // Pass the hasher instance from the service provider
            var handler = new TenantAccessHandler(_mockLogger.Object, _mockHttpContextAccessor.Object, _serviceProvider, _apiKeyHasher);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
            Assert.True(context.HasFailed);
            Assert.Contains("API key missing", context.FailureReasons.First().Message);
        }

        [Theory]
        [InlineData("Header")]
        [InlineData("Query")]
        public async Task HandleRequirementAsync_ShouldFail_WhenApiKeyIsInvalid(string apiKeyLocation)
        {
            // Arrange
            var dashboardUid = "dash-abc";
            var user = new ClaimsPrincipal(new ClaimsIdentity());
            // Ensure a tenant exists so failure is due to key, not missing FK
            await SeedTenantAndPermission(5, dashboardUid);

            SetupTestContext(apiKeyLocation, "invalid-key-that-wont-verify", user);
            SetupRouteData(dashboardUid);
            var context = CreateAuthorizationContext(user);
             // Pass the hasher instance from the service provider
            var handler = new TenantAccessHandler(_mockLogger.Object, _mockHttpContextAccessor.Object, _serviceProvider, _apiKeyHasher);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
            Assert.True(context.HasFailed);
            Assert.Contains("Invalid API Key", context.FailureReasons.First().Message);
        }

        [Theory]
        [InlineData("Header")]
        [InlineData("Query")]
         public async Task HandleRequirementAsync_ShouldFail_WhenDashboardUidIsMissingFromRoute(string apiKeyLocation)
        {
             // Arrange
            var tenantId = 1;
            var seededData = await SeedTenantAndPermission(tenantId, "some-dashboard"); // Seed tenant and a dummy permission
            var plainTextApiKey = seededData.ApiKey.PlainTextKeyValueForTesting;
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            SetupTestContext(apiKeyLocation, plainTextApiKey, user);
            SetupRouteData(null); // No dashboardUid in route
            var context = CreateAuthorizationContext(user);
             // Pass the hasher instance from the service provider
            var handler = new TenantAccessHandler(_mockLogger.Object, _mockHttpContextAccessor.Object, _serviceProvider, _apiKeyHasher);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
            Assert.True(context.HasFailed);
            Assert.Contains("Dashboard UID missing from request path", context.FailureReasons.First().Message);
        }

        [Theory]
        [InlineData("Header")]
        [InlineData("Query")]
        public async Task HandleRequirementAsync_ShouldSucceed_WhenUidIsInRemainderRouteValue(string apiKeyLocation)
        {
             // Arrange
            var tenantId = 3;
            var dashboardUid = "dash-remainder";
            var remainder = $"{dashboardUid}/some/other/path/segments";
            var seededData = await SeedTenantAndPermission(tenantId, dashboardUid);
            var plainTextApiKey = seededData.ApiKey.PlainTextKeyValueForTesting;
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            SetupTestContext(apiKeyLocation, plainTextApiKey, user);
            SetupRouteData(null, remainder);
            var context = CreateAuthorizationContext(user);
             // Pass the hasher instance from the service provider
            var handler = new TenantAccessHandler(_mockLogger.Object, _mockHttpContextAccessor.Object, _serviceProvider, _apiKeyHasher);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded, "Context should have succeeded with UID from remainder.");
            Assert.False(context.HasFailed);
        }

        public void Dispose()
        {
             _serviceProvider?.Dispose();
             // Delete the test database file
             if (File.Exists(_dbPath))
             {
                 try { File.Delete(_dbPath); }
                 catch (IOException ex)
                 {
                    // Log or handle potential file lock issues during cleanup
                    Console.WriteLine($"Could not delete test database '{_dbPath}': {ex.Message}");
                 }
             }
        }
    }
} 