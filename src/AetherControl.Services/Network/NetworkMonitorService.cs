using System.Net.NetworkInformation;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
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
    // The ICMP send below is synchronous with a 1s timeout — at a 1s sampling cadence that is a
    // permanent ping-per-second background cost for a card that can't visually change faster than
    // a person reads it. Coalescing to ≥2s halves it outright; at the eased idle cadence the
    // sample itself is already further apart than this, so the coalescing never binds there.
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    private const string LatencyProbeHost = "1.1.1.1";

    private readonly ILogger<NetworkMonitorService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Ping _ping = new();
    // Raw byte-delta throughput is genuinely bursty (background sync, telemetry, prefetch) —
    // a real "1 Mbps then 40 then 2" pattern reads as flickering on a fixed dashboard card even
    // though every individual reading is correct. Same EMA technique already applied to CPU clock
    // for the same reason: smooth the *display*, don't chase a bug that isn't there.
    private readonly EmaSmoother _uploadSmoother = new();
    private readonly EmaSmoother _downloadSmoother = new();

    private Timer? _timer;
    private NetworkInterface? _activeInterface;
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private DateTimeOffset _lastSampleAt;
    private DateTimeOffset _lastRunAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPingAt = DateTimeOffset.MinValue;
    private double _lastLatencyMs;
    private DateTimeOffset _lastExternalIpFetch = DateTimeOffset.MinValue;
    private string _externalIp = string.Empty;
    private TimeSpan _minimumInterval = TimeSpan.Zero;

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
        _lastRunAt = DateTimeOffset.MinValue; // first sample after a (re)start runs immediately
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

    public void SetMinimumInterval(TimeSpan interval) =>
        _minimumInterval = interval < TimeSpan.Zero ? TimeSpan.Zero : interval;

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
        var ranAt = DateTimeOffset.UtcNow;
        if (ranAt - _lastRunAt < _minimumInterval)
        {
            return; // eased onto the hardware monitor's idle cadence — see SetMinimumInterval
        }

        _lastRunAt = ranAt;
        _activeInterface ??= FindActiveInterface();
        var info = new NetworkInfo { AdapterName = _activeInterface?.Description ?? "Unknown", ExternalIpAddress = _externalIp };

        if (_activeInterface is not null)
        {
            var stats = _activeInterface.GetIPv4Statistics();
            var now = DateTimeOffset.UtcNow;
            var elapsedSeconds = Math.Max((now - _lastSampleAt).TotalSeconds, 0.001);

            var sentDelta = Math.Max(stats.BytesSent - _lastBytesSent, 0);
            var receivedDelta = Math.Max(stats.BytesReceived - _lastBytesReceived, 0);

            info.UploadKbps = _uploadSmoother.Update(sentDelta * 8 / 1024.0 / elapsedSeconds);
            info.DownloadKbps = _downloadSmoother.Update(receivedDelta * 8 / 1024.0 / elapsedSeconds);

            _lastBytesSent = stats.BytesSent;
            _lastBytesReceived = stats.BytesReceived;
            _lastSampleAt = now;
        }

        if (ranAt - _lastPingAt >= PingInterval)
        {
            _lastPingAt = ranAt;
            _lastLatencyMs = MeasureLatency();
        }

        info.LatencyMs = _lastLatencyMs;

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
