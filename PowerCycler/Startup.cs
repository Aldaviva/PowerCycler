using Kasa;
using PowerCycler;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .UseWindowsService(options => options.ServiceName = "PowerCycler")
    .ConfigureAppConfiguration(builder => builder
        .AddJsonFile("./powercycler.json", true)
        .AddJsonFile("/etc/powercycler.json", true)
        .AddJsonFile("/usr/local/etc/powercycler.json", true))
    .ConfigureServices((context, services) => {
        services.AddHostedService<HealthMonitor>();
        services.AddSingleton(_ => context.Configuration.Get<Configuration>()!);

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