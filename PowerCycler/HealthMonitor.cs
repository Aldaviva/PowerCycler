using System.Net;
using System.Net.Sockets;
using System.Timers;
using Kasa;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace PowerCycler;

public class HealthMonitor: BackgroundService {

    private readonly Configuration          configuration;
    private readonly IKasaOutlet            outlet;
    private readonly ILogger<HealthMonitor> logger;
    private readonly HttpClient             httpClient;
    private readonly Timer                  restartTrigger;
    private          CancellationToken      cancellationToken;

    public HealthMonitor(Configuration configuration, IKasaOutlet outlet, ILogger<HealthMonitor> logger) {
        this.configuration = configuration;
        this.outlet        = outlet;
        this.logger        = logger;

        restartTrigger = new Timer(TimeSpan.FromSeconds(configuration.minOfflineDurationBeforeRestartSec)) {
            AutoReset = false,
            Enabled   = false
        };
        restartTrigger.Elapsed += triggerRestart;

        httpClient = new HttpClient(new SocketsHttpHandler {
            AllowAutoRedirect        = false,
            ConnectTimeout           = TimeSpan.FromSeconds(configuration.healthCheckTimeoutSec),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        });
    }

    /// <exception cref="ArgumentOutOfRangeException"></exception>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        cancellationToken = stoppingToken;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                Uri healthCheckUrl = configuration.healthCheckUrl;

                bool success = healthCheckUrl.Scheme.ToLowerInvariant() switch {
                    "http" or "https" => await checkHttpHealth(healthCheckUrl),
                    "tcp"             => await checkTcpHealth(healthCheckUrl),
                    _                 => throw new ArgumentOutOfRangeException($"Unknown health check URI scheme {healthCheckUrl.Scheme}")
                };

                logger.LogInformation("{Hostname} is {Status}", healthCheckUrl, success ? "online" : "offline");

                if (success) {
                    onHealthCheckSuccess();
                }

                await Task.Delay(TimeSpan.FromSeconds(configuration.healthCheckFrequencySec), cancellationToken);
            } catch (TaskCanceledException) {
                // while loop will finish now
            }
        }
    }

    private async Task<bool> checkHttpHealth(Uri healthCheckUrl) {
        try {
            using HttpResponseMessage response = await httpClient.GetAsync(healthCheckUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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
        using TcpClient tcpClient = new() {
            SendTimeout    = (int) TimeSpan.FromSeconds(configuration.healthCheckTimeoutSec).TotalMilliseconds,
            ReceiveTimeout = (int) TimeSpan.FromSeconds(configuration.healthCheckTimeoutSec).TotalMilliseconds
        };

        try {
            await tcpClient.ConnectAsync(healthCheckUrl.Host, healthCheckUrl.Port, cancellationToken);

            return tcpClient.Connected;
        } catch (SocketException) {
            return false;
        } catch (TaskCanceledException) {
            return true;
        } finally {
            tcpClient.Close();
        }
    }

    private void onHealthCheckSuccess() {
        restartTrigger.Stop();
        restartTrigger.Start();
    }

    private async void triggerRestart(object? sender, ElapsedEventArgs e) {
        restartTrigger.Stop();
        if (!cancellationToken.IsCancellationRequested) {
            try {
                logger.LogInformation("Power cycling the outlet at {OutletHostname}", configuration.outletHostname);
                /*await outlet.System.SetOutletOn(false);
                // ReSharper disable once MethodSupportsCancellation - even if the service is shutting down, we still want to turn the outlet back on
                await Task.Delay(TimeSpan.FromSeconds(2));
                await outlet.System.SetOutletOn(true);*/
                await Task.Delay(TimeSpan.FromSeconds(configuration.resumeHealthCheckAfterRestartSec), cancellationToken);
                restartTrigger.Start();
            } catch (KasaException ex) {
                logger.LogError(ex, "Failed to power cycle outlet {Hostname}", outlet.Hostname);
            } catch (TaskCanceledException) { }
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            httpClient.Dispose();
            restartTrigger.Elapsed -= triggerRestart;
            restartTrigger.Dispose();
        }
    }

    public sealed override void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

}