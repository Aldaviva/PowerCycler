using Kasa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerCycler;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices(services => {
        services.AddHostedService<HealthMonitor>();
        services.AddSingleton(s => s.GetRequiredService<IConfiguration>().Get<Configuration>()!);

        services.AddSingleton<IKasaOutlet>(s => {
            Configuration config  = s.GetRequiredService<Configuration>();
            TimeSpan      timeout = TimeSpan.FromSeconds(config.healthCheckTimeoutSec);
            return new KasaOutlet(config.outletHostname, new Options {
                LoggerFactory  = s.GetService<ILoggerFactory>(),
                ReceiveTimeout = timeout,
                SendTimeout    = timeout
            });
        });
    })
    .Build();

await host.RunAsync();