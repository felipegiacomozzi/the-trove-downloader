using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Threading.Tasks;

namespace TheTroveDownloader
{
    static class Program
    {
        private static async Task Main()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(outputTemplate:"[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("the-trove-downloader.log", outputTemplate: "[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var host = new HostBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost
                    .SetBasePath(Directory.GetCurrentDirectory());
                })
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.SetBasePath(Directory.GetCurrentDirectory());
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging()                        
                        .Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Information)
                        .AddHostedService<TheTroveDownloader>();

                    services.Configure<HostOptions>(option =>
                    {
                        option.ShutdownTimeout = System.TimeSpan.FromSeconds(1);                        
                    });
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging.AddConsole();
                })
                .UseSerilog()
                .UseConsoleLifetime()
                .Build();

            await host.RunAsync();            
        }
    }
}
