using System.Net.NetworkInformation;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Network;

/// <summary>
/// Tracks upload/download throughput via interface byte counters, latency via
/// ICMP ping to a well-known host, and the public-facing IP via an occasional
/// outbound lookup (throttled independently of the polling interval since it
/// is the only sensor here that leaves the machine).
/// </summary>
public sealed class NetworkMonitorService : INetworkMonitorService
{
    private static readonly TimeSpan ExternalIpRefreshInterval = TimeSpan.FromMinutes(5);
    private const string LatencyProbeHost = "1.1.1.1";

    private readonly ILogger<NetworkMonitorService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Ping _ping = new();

    private Timer? _timer;
    private NetworkInterface? _activeInterface;
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private DateTimeOffset _lastSampleAt;
    private DateTimeOffset _lastExternalIpFetch = DateTimeOffset.MinValue;
    private string _externalIp = string.Empty;

    public NetworkMonitorService(ILogger<NetworkMonitorService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public NetworkInfo? Latest { get; private set; }

    public event EventHandler<NetworkInfo>? Updated;

    public void Start(TimeSpan interval)
    {
        _activeInterface = FindActiveInterface();
        if (_activeInterface is not null)
        {
            var stats = _activeInterface.GetIPv4Statistics();
            _lastBytesSent = stats.BytesSent;
            _lastBytesReceived = stats.BytesReceived;
            _lastSampleAt = DateTimeOffset.UtcNow;
        }

        _timer?.Dispose();
        _timer = new Timer(_ => SafeSample(), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeSample()
    {
        try
        {
            Sample();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network sampling failed");
        }
    }

    private void Sample()
    {
        _activeInterface ??= FindActiveInterface();
        var info = new NetworkInfo { AdapterName = _activeInterface?.Description ?? "Unknown", ExternalIpAddress = _externalIp };

        if (_activeInterface is not null)
        {
            var stats = _activeInterface.GetIPv4Statistics();
            var now = DateTimeOffset.UtcNow;
            var elapsedSeconds = Math.Max((now - _lastSampleAt).TotalSeconds, 0.001);

            var sentDelta = Math.Max(stats.BytesSent - _lastBytesSent, 0);
            var receivedDelta = Math.Max(stats.BytesReceived - _lastBytesReceived, 0);

            info.UploadKbps = sentDelta * 8 / 1024.0 / elapsedSeconds;
            info.DownloadKbps = receivedDelta * 8 / 1024.0 / elapsedSeconds;

            _lastBytesSent = stats.BytesSent;
            _lastBytesReceived = stats.BytesReceived;
            _lastSampleAt = now;
        }

        info.LatencyMs = MeasureLatency();

        if (DateTimeOffset.UtcNow - _lastExternalIpFetch > ExternalIpRefreshInterval)
        {
            _ = RefreshExternalIpAsync();
        }

        info.ExternalIpAddress = _externalIp;
        Latest = info;
        Updated?.Invoke(this, info);
    }

    private double MeasureLatency()
    {
        try
        {
            var reply = _ping.Send(LatencyProbeHost, 1000);
            return reply?.Status == IPStatus.Success ? reply.RoundtripTime : 0;
        }
        catch (PingException)
        {
            return 0;
        }
    }

    private async Task RefreshExternalIpAsync()
    {
        _lastExternalIpFetch = DateTimeOffset.UtcNow;
        try
        {
            var ip = await _httpClient.GetStringAsync("https://api.ipify.org").ConfigureAwait(false);
            _externalIp = ip.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "External IP lookup failed (offline or blocked)");
        }
    }

    private static NetworkInterface? FindActiveInterface() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                      && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                      && nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
        .OrderByDescending(nic => nic.Speed)
        .FirstOrDefault();

    public void Dispose()
    {
        _timer?.Dispose();
        _ping.Dispose();
    }
}
