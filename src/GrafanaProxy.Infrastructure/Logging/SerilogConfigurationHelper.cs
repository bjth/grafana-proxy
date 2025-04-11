using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace GrafanaProxy.Infrastructure.Logging
{
    public static class SerilogConfigurationHelper
    {
        public static Logger Configure(IConfiguration configuration)
        {
            return new LoggerConfiguration()
                .ReadFrom.Configuration(configuration) // Read settings from appsettings.json ("Serilog" section)
                .CreateLogger();
        }
    }
} 