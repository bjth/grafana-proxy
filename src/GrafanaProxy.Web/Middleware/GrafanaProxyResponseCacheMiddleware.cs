using Microsoft.Net.Http.Headers; // Needed for Cache-Control

namespace GrafanaProxy.Web.Middleware
{
    /// <summary>
    /// Applies Response Caching headers specifically for Grafana Proxy routes.
    /// </summary>
    public class GrafanaProxyResponseCacheMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GrafanaProxyResponseCacheMiddleware> _logger;
        private const string GrafanaProxyPathPrefix = "/grafana/public/"; // Adjust if your route path changes
        private const int CacheDurationSeconds = 5;

        public GrafanaProxyResponseCacheMiddleware(RequestDelegate next, ILogger<GrafanaProxyResponseCacheMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if the request path matches the Grafana proxy route
            if (context.Request.Path.StartsWithSegments(GrafanaProxyPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Only apply caching to successful GET requests
                if (HttpMethods.IsGet(context.Request.Method))
                {
                     _logger.LogTrace("Applying response cache headers for proxy request: {Path}", context.Request.Path);
                    // Set cache headers BEFORE executing the rest of the pipeline
                    // This allows the ResponseCachingMiddleware (registered earlier) to act on these headers.
                    context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
                    {
                        Public = true,
                        MaxAge = TimeSpan.FromSeconds(CacheDurationSeconds)
                    };
                    // Vary by query string and header to ensure correct caching if API key is passed differently
                    context.Response.Headers[HeaderNames.Vary] = new string[] { "Accept-Encoding", "X-Api-Key", "Query" }; // Query isn't standard, but helps ResponseCaching distinguish keys in URL
                }
                else
                {
                    _logger.LogTrace("Skipping cache header application for non-GET proxy request: {Method} {Path}", context.Request.Method, context.Request.Path);
                }
            }
             else
            {
                _logger.LogTrace("Skipping cache header application for non-proxy request: {Path}", context.Request.Path);
            }

            await _next(context);
        }
    }

    // Extension method for easy registration
    public static class GrafanaProxyResponseCacheMiddlewareExtensions
    {
        public static IApplicationBuilder UseGrafanaProxyResponseCaching(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GrafanaProxyResponseCacheMiddleware>();
        }
    }
} 