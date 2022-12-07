using System.Net;
using System.Net.Sockets;
using System.Timers;
using Kasa;
using Timer = System.Timers.Timer;

namespace PowerCycler;

public class HealthMonitor: BackgroundService {

    private readonly Configuration          configuration;
    private readonly IKasaOutlet            outlet;
    private readonly ILogger<HealthMonitor> logger;
    private readonly HttpClient             httpClient;
    private readonly Timer                  restartTrigger;
    private readonly ManualResetEventSlim   notRestarting = new(true);

    private CancellationToken serviceShutdown;

    public HealthMonitor(Configuration configuration, IKasaOutlet outlet, ILogger<HealthMonitor> logger) {
        this.configuration = configuration;
        this.outlet        = outlet;
        this.logger        = logger;

        restartTrigger = new Timer(TimeSpan.FromSeconds(configuration.minOfflineDurationBeforeRestartSec)) {
            AutoReset = false,
            Enabled   = false // wait for one healthy check at startup before running the kill timer. helps after power outages when the router is slow to boot.
        };
        restartTrigger.Elapsed += restart;

        httpClient = new HttpClient(new SocketsHttpHandler {
            AllowAutoRedirect        = false,
            ConnectTimeout           = TimeSpan.FromSeconds(configuration.healthCheckTimeoutSec),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        });

        logger.LogInformation("Checking {Url} every {CheckInterval:N0} seconds, and power cycling {OutletHostname} after it is offline for {MaxOffline:N0} seconds", configuration.healthCheckUrl,
            configuration.healthCheckFrequencySec, configuration.outletHostname, configuration.minOfflineDurationBeforeRestartSec);
    }

    /// <exception cref="ArgumentOutOfRangeException"></exception>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        serviceShutdown = stoppingToken;
        while (!stoppingToken.IsCancellationRequested) {
            try {
                notRestarting.Wait(serviceShutdown);
                bool isHealthy = await checkHealth();

                if (isHealthy) {
                    onHealthCheckSuccess();
                    logger.LogDebug("{Hostname} is online", configuration.healthCheckUrl);
                } else {
                    logger.LogInformation("{Hostname} is offline", configuration.healthCheckUrl);
                }

                await Task.Delay(TimeSpan.FromSeconds(configuration.healthCheckFrequencySec), stoppingToken);
            } catch (TaskCanceledException) {
                // while loop will finish now
            } catch (OperationCanceledException) {
                // while loop will finish now
            }
        }
    }

    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private Task<bool> checkHealth() =>
        configuration.healthCheckUrl.Scheme.ToLowerInvariant() switch {
            "http" or "https" => checkHttpHealth(configuration.healthCheckUrl),
            "tcp"             => checkTcpHealth(configuration.healthCheckUrl),
            _                 => throw new ArgumentOutOfRangeException($"Unknown health check URI scheme {configuration.healthCheckUrl.Scheme}")
        };

    private async Task<bool> checkHttpHealth(Uri healthCheckUrl) {
        try {
            using HttpResponseMessage response = await httpClient.GetAsync(healthCheckUrl, HttpCompletionOption.ResponseHeadersRead, serviceShutdown);

            return response.IsSuccessStatusCode;
        } catch (HttpRequestException) {
            return false;
        } catch (HttpListenerException) {
            return false;
        } catch (HttpProtocolException) {
            return false;
        } catch (TaskCanceledException) {
            return true;
        }
    }

    private async Task<bool> checkTcpHealth(Uri healthCheckUrl) {
        int             timeoutMillis = (int) TimeSpan.FromSeconds(configuration.healthCheckTimeoutSec).TotalMilliseconds;
        using TcpClient tcpClient     = new() { SendTimeout = timeoutMillis, ReceiveTimeout = timeoutMillis };

        try {
            await tcpClient.ConnectAsync(healthCheckUrl.Host, healthCheckUrl.Port, serviceShutdown);

            return tcpClient.Connected;
        } catch (SocketException) {
            return false;
        } catch (TaskCanceledException) {
            return true;
        }
    }

    private void onHealthCheckSuccess() {
        restartTrigger.Stop();
        restartTrigger.Start();
    }

    private async void restart(object? sender, ElapsedEventArgs e) {
        restartTrigger.Stop();
        if (serviceShutdown.IsCancellationRequested) return;
        notRestarting.Reset();

        try {
            //last-ditch health check before restarting
            bool isHealthy = await checkHealth();
            if (!isHealthy) {
                logger.LogInformation("Power cycling the outlet at {OutletHostname}", outlet.Hostname);
                try {
                    await outlet.System.SetOutletOn(false);
                    // ReSharper disable once MethodSupportsCancellation - even if the service is shutting down, we still want to turn the outlet back on
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    await outlet.System.SetOutletOn(true);
                } catch (KasaException ex) {
                    logger.LogError(ex, "Failed to power cycle outlet {Hostname}", outlet.Hostname);
                }

                await Task.Delay(TimeSpan.FromSeconds(configuration.resumeHealthCheckAfterRestartSec), serviceShutdown);
            }
        } catch (TaskCanceledException) {
            // continue
        } finally {
            notRestarting.Set();
            restartTrigger.Start();
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            restartTrigger.Elapsed -= restart;
            restartTrigger.Dispose();
            httpClient.Dispose();
        }
    }

    public sealed override void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

}