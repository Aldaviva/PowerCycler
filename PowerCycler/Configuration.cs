namespace PowerCycler;

public class Configuration {

    public Uri healthCheckUrl { get; set; }
    public int healthCheckTimeoutSec { get; set; }
    public int healthCheckFrequencySec { get; set; }
    public string outletHostname { get; set; }
    public int minOfflineDurationBeforeRestartSec { get; set; }
    public int resumeHealthCheckAfterRestartSec { get; set; }

}