PowerCycler
===

[![GitHub Workflow Status](https://img.shields.io/github/workflow/status/Aldaviva/PowerCycler/.NET?logo=github)](https://github.com/Aldaviva/Kasa/actions/workflows/main.yml)

Automatically turn a power outlet off and on again when a health check fails.

## Prerequisites
- [.NET 7 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/7.0) or later
- [Kasa smart outlet](https://www.kasasmart.com/us/products/smart-plugs), such as the [EP10](https://www.kasasmart.com/us/products/smart-plugs/kasa-smart-plug-mini-ep10) or [KP125](https://www.kasasmart.com/us/products/smart-plugs/kasa-smart-plug-slim-energy-monitoring-kp125)

## Installation

Pre-built executables are provided for Windows (x64) and Linux (x64 and ARM).

### Linux

Tested on a Raspberry Pi 2 Model B running Raspbian 11 Bullseye.

1. Download the Linux ARM build from the [latest release](https://github.com/Aldaviva/PowerCycler/releases/latest).
1. Unzip and move the executable.
    ```sh
    unzip PowerCycler-linux-arm.zip
    sudo mv PowerCycler /usr/local/bin/
    sudo chmod +x /usr/local/bin/PowerCycler
    ```
1. Move the configuration file.
    ```sh
    sudo mv powercycler.json /usr/local/etc/
    ```
1. Install the systemd service unit file.
    ```sh
    sudo mv powercycler.service /etc/systemd/system/
    sudo systemctl daemon-reload
    sudo systemctl enable powercycler.service
    ```

### Windows

1. Download the Windows build from the [latest release](https://github.com/Aldaviva/PowerCycler/releases/latest).
1. Unzip it to a folder such as `C:\Program Files\PowerCycler\`.
1. Open PowerShell elevated and run
    ```ps1
    New-Service -Name "PowerCycler" -DisplayName "PowerCycler" -Description "Turn it off and on again." -BinaryPathName "C:\Program Files\PowerCycler\PowerCycler.exe" -DependsOn Tcpip
    ```

## Configuration

Edit the `powercycler.json` configuration file.

All fields are required.

|Field|Example value|Description|
|---|---|---|
|`healthCheckUrl`|`https://aldaviva.com`<br>or <br>`tcp://aldaviva.com:22`|The URL of the process being monitored to check and see if it's healthy.<br>For **`http`** and **`https`** schemes, it sends a GET request and requires a status code in [200, 300). Redirections are not followed.<br>For the **`tcp`** scheme, it opens a socket connection to the URL's hostname and port.|
|`healthCheckTimeoutSec`|`5`|How long, in seconds, the process being monitored has to send a health check response before that check fails. Should be shorter than `healthCheckFrequencySec`.|
|`healthCheckFrequencySec`|`60`|How often, in seconds, to send health check requests.|
|`outletHostname`|`bragi.outlets.aldaviva.com`<br>or<br>`192.168.1.100`|The FQDN or IP address of a Kasa smart outlet to turn off and on when the process is deemed to be offline and must be restarted.|
|`minOfflineDurationBeforeRestartSec`|`600`|How long, in seconds, to go without any successful health check responses before the outlet is power cycled. Should be longer than `healthCheckFrequencySec`.|
|`resumeHealthCheckAfterRestartSec`|`300`|How long, in seconds, to wait after power cycling before checking health again. Should be longer than it takes your process to become healthy after a reboot.|

With the above example values, this service will send an HTTP GET request to `https://aldaviva.com` every 60 seconds.
- If it has returned at least one 200 OK response in the last 600 seconds, this service will do nothing.
- Otherwise, if it has done nothing in the past 600 seconds but return errors (such as 400, 500, or 503) or take more than 5 seconds to respond, then this service will turn off the outlet at 192.168.1.100, wait 2 seconds, and then turn the outlet back on again. This service will then wait 300 seconds before finally starting the health check loop again.

## Running

### Linux

```sh
sudo systemctl start powercycler.service
```

### Windows

```ps1
Start-Service PowerCycler
```