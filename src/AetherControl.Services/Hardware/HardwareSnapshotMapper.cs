using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace AetherControl.Services.Hardware;

/// <summary>
/// Translates LibreHardwareMonitor's raw sensor tree into the strongly-typed
/// models the rest of the app consumes. Sensor availability and naming vary
/// significantly by vendor/BIOS, so lookups are done by best-effort name
/// matching with graceful fallbacks to zero rather than throwing.
/// </summary>
internal static class HardwareSnapshotMapper
{
    public static CpuInfo MapCpu(IHardware? cpu)
    {
        var info = new CpuInfo();
        if (cpu is null)
        {
            return info;
        }

        info.Name = cpu.Name;
        // Fallback order (Package/Tctl-Tdie/Core Max) reflects real AMD/Intel BIOS naming
        // differences observed across boards — an AMD board with no "Package" sensor still
        // reports temperature correctly via "Tctl/Tdie" rather than falling through to zero.
        info.TemperatureCelsius = FindValue(cpu, SensorType.Temperature, "Package", "CPU Package", "Tctl/Tdie", "Core Max");
        info.PackagePowerWatts = FindValue(cpu, SensorType.Power, "Package", "CPU Package");
        info.UtilisationPercent = FindValue(cpu, SensorType.Load, "CPU Total");
        // A newer LibreHardwareMonitorLib release (bumped 2026-09) added a SEPARATE set of "current
        // actual" clock sensors alongside the pre-existing ones on AMD parts — e.g. this Ryzen 7
        // 9800X3D reports both "Cores (Average)" (5395 MHz, effectively the fixed boost-clock
        // ceiling this chip requests almost regardless of load — confirmed via a live capture, it
        // barely moves) and "Cores (Average Effective)" (1535 MHz at the same instant, which DOES
        // track real load). Preferring the plain aggregate meant the dashboard showed a number that
        // looked live but was near-constant — a subtler version of the same "confidently wrong"
        // problem as CoreVoltage below. Effective is preferred wherever it exists.
        info.ClockSpeedMhz = FindNamedOrZero(cpu, SensorType.Clock, "Cores (Average Effective)", "Core Average Effective");
        if (info.ClockSpeedMhz <= 0)
        {
            // Per-core effective sensors specifically — NOT a plain "Core #" substring match, which
            // on this same CPU would also match the non-effective "Core #1".."Core #8" sensors and
            // blend two different quantities (boost ceiling and real clock) into one meaningless average.
            info.ClockSpeedMhz = AverageMatching(cpu, SensorType.Clock, "Core #", requireAll: ["(Effective)"]);
        }

        if (info.ClockSpeedMhz <= 0)
        {
            // No effective data on this board/CPU/LHM version at all (e.g. Intel, or an older LHM) —
            // the boost-ceiling aggregate is still better than nothing.
            info.ClockSpeedMhz = FindNamedOrZero(cpu, SensorType.Clock, "Cores (Average)", "Core Average");
        }

        if (info.ClockSpeedMhz <= 0)
        {
            info.ClockSpeedMhz = AverageMatching(cpu, SensorType.Clock, "Core #", requireNone: ["(Effective)"]);
        }

        // Confirmed via a one-shot sensor dump on a real AMD Ryzen 7 9800X3D (LibreHardwareMonitorLib
        // 0.9.4 exposes no real measured-voltage sensor for this CPU yet, only "Core #N VID" —
        // the VID *requested* of the VRM, not a measurement, and it reads ~0.2 on this chip, nowhere
        // near a real ~1.0-1.4V core voltage). The old "Core #1" candidate matched "Core #1 VID" by
        // substring and displayed it as if it were real voltage — confidently wrong is worse than
        // honestly unavailable, so this only matches actual voltage-rail sensor names and otherwise
        // returns 0, the same "not available" convention used elsewhere (e.g. GPU fan RPM). A real
        // Vcore measurement DOES exist for this chip, just not from the CPU's own sensor set — the
        // motherboard's Super I/O chip reports it (confirmed live: 1.376V) — see
        // HardwareMonitorService.Poll's motherboard-Vcore fallback.
        info.CoreVoltage = FindNamedOrZero(cpu, SensorType.Voltage, "CPU Core", "Core Voltage", "VCore", "SVI2", "SVI3");

        // Indexed by exact extracted core number rather than substring-matched — "#1" as a Contains()
        // check also matches "#10"-"#19", which would silently pair the wrong core's clock/temp together.
        // Effective-only for per-core clock, same reasoning as the aggregate above — a plain "Core #"
        // index would non-deterministically pick whichever of the two same-numbered sensors LHM
        // happens to enumerate first each poll.
        var clockByCore = IndexSensorsByCore(cpu, SensorType.Clock, requireAll: ["(Effective)"]);
        if (clockByCore.Count == 0)
        {
            clockByCore = IndexSensorsByCore(cpu, SensorType.Clock, requireNone: ["(Effective)"]);
        }

        var temperatureByCore = IndexSensorsByCore(cpu, SensorType.Temperature);

        var cores = new List<CpuCoreInfo>();
        foreach (var sensor in cpu.Sensors.Where(s => s.SensorType == SensorType.Load && s.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase)))
        {
            var index = ExtractCoreIndex(sensor.Name);
            cores.Add(new CpuCoreInfo
            {
                Index = index,
                LoadPercent = sensor.Value ?? 0,
                ClockSpeedMhz = clockByCore.GetValueOrDefault(index, 0f),
                TemperatureCelsius = temperatureByCore.GetValueOrDefault(index, 0f)
            });
        }

        info.Cores = cores.OrderBy(c => c.Index).ToList();
        return info;
    }

    public static GpuInfo MapGpu(IHardware? gpu)
    {
        var info = new GpuInfo();
        if (gpu is null)
        {
            return info;
        }

        info.Name = gpu.Name;
        info.TemperatureCelsius = FindValue(gpu, SensorType.Temperature, "GPU Core", "GPU");
        info.HotspotTemperatureCelsius = FindValue(gpu, SensorType.Temperature, "GPU Hot Spot", "Memory Junction", "Hot Spot");
        info.UtilisationPercent = FindValue(gpu, SensorType.Load, "GPU Core", "D3D 3D");
        info.PowerDrawWatts = FindValue(gpu, SensorType.Power, "GPU Package", "GPU Power");
        info.VramUsedMb = FindValue(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        info.VramTotalMb = FindValue(gpu, SensorType.SmallData, "GPU Memory Total");
        info.FanSpeedPercent = FindValue(gpu, SensorType.Control, "GPU Fan");
        info.FanSpeedRpm = FindValue(gpu, SensorType.Fan, "GPU Fan");
        info.CoreClockMhz = FindValue(gpu, SensorType.Clock, "GPU Core");
        info.MemoryClockMhz = FindValue(gpu, SensorType.Clock, "GPU Memory");
        return info;
    }

    public static MotherboardInfo MapMotherboard(IHardware? motherboard)
    {
        var info = new MotherboardInfo();
        if (motherboard is null)
        {
            return info;
        }

        info.Model = motherboard.Name;

        var superIo = motherboard.SubHardware.FirstOrDefault(h => h.HardwareType == HardwareType.SuperIO) ?? motherboard;

        // Sorted by name for the same reason storage drives are sorted by identifier — an unstable
        // enumeration order reshuffles which card ends up in which screen position every poll, which
        // looks identical to the values themselves flickering even though each one is individually correct.
        info.Voltages = superIo.Sensors
            .Where(s => s.SensorType == SensorType.Voltage)
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(ToNamedValue("V"))
            .ToList();

        info.FanSpeeds = superIo.Sensors
            .Where(s => s.SensorType == SensorType.Fan)
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(ToNamedValue("RPM"))
            .ToList();

        info.VrmTemperatures = superIo.Sensors
            .Where(s => s.SensorType == SensorType.Temperature &&
                        (s.Name.Contains("VRM", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("MOS", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(ToNamedValue("°C"))
            .ToList();

        if (info.VrmTemperatures.Count == 0)
        {
            // This board's Nuvoton chip (like many) exposes its temperature headers as plain
            // "Temperature #1".."Temperature #6" — nothing in the name says which is VRM, chipset,
            // or something else. The empty-state UI used to claim "no VRM temperature sensors
            // reported by this board" — false; it reports 6, just not under a name this filter
            // matched. Real, if unlabelled, board temperature data beats a box that looks broken
            // when the board data exists — same call already made for fan headers (also generically
            // named, also shown as-is with a user-rename option rather than hidden).
            info.VrmTemperatures = superIo.Sensors
                .Where(s => s.SensorType == SensorType.Temperature)
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .Select(ToNamedValue("°C"))
                .ToList();
        }

        return info;
    }

    public static IReadOnlyList<StorageDriveInfo> MapStorage(IEnumerable<IHardware> storageDevices)
    {
        var results = new List<StorageDriveInfo>();
        foreach (var device in storageDevices)
        {
            // A drive with no real SMART temperature sensor exposed (common on some external/USB
            // enclosures, and older or bridge-chip drives) used to silently report 0°C via
            // FindValue's fallback-to-zero — indistinguishable on screen from a genuinely-measured
            // 0°C. TryFindValue reports whether an actual sensor was found, not inferred from the
            // value, so that gets surfaced as MetricQuality.Unsupported instead (Phase 33).
            var (temperature, hasTemperatureSensor) = TryFindValue(device, SensorType.Temperature, "Temperature", "Drive");
            results.Add(new StorageDriveInfo
            {
                DeviceId = device.Identifier.ToString(),
                Model = device.Name,
                TemperatureCelsius = temperature,
                TemperatureQuality = hasTemperatureSensor ? MetricQuality.Good : MetricQuality.Unsupported,
                IsNvme = device.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            });
        }

        return results;
    }

    private static Func<ISensor, NamedSensorValue> ToNamedValue(string unit) => sensor => new NamedSensorValue
    {
        Name = sensor.Name,
        Value = sensor.Value ?? 0,
        Unit = unit
    };

    private static float FindValue(IHardware hardware, SensorType type, params string[] nameCandidates)
    {
        foreach (var candidate in nameCandidates)
        {
            var sensor = hardware.Sensors.FirstOrDefault(s =>
                s.SensorType == type && s.Name.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (sensor?.Value is { } value)
            {
                return value;
            }
        }

        var firstOfType = hardware.Sensors.FirstOrDefault(s => s.SensorType == type);
        return firstOfType?.Value ?? 0f;
    }

    /// <summary>Like <see cref="FindValue"/>, but reports whether an actual sensor was found rather
    /// than silently folding "not found" and "found, value happens to be 0" into the same 0f — see
    /// MapStorage's use of this for why that distinction matters for a display quality state.</summary>
    private static (float Value, bool Found) TryFindValue(IHardware hardware, SensorType type, params string[] nameCandidates)
    {
        foreach (var candidate in nameCandidates)
        {
            var sensor = hardware.Sensors.FirstOrDefault(s =>
                s.SensorType == type && s.Name.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (sensor?.Value is { } value)
            {
                return (value, true);
            }
        }

        var firstOfType = hardware.Sensors.FirstOrDefault(s => s.SensorType == type);
        return firstOfType?.Value is { } fallback ? (fallback, true) : (0f, false);
    }

    /// <summary>Like <see cref="FindValue"/> but returns 0 on a miss instead of grabbing the first sensor of the type — for callers that have their own, more targeted fallback.</summary>
    private static float FindNamedOrZero(IHardware hardware, SensorType type, params string[] nameCandidates)
    {
        foreach (var candidate in nameCandidates)
        {
            var sensor = hardware.Sensors.FirstOrDefault(s =>
                s.SensorType == type && s.Name.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (sensor?.Value is { } value)
            {
                return value;
            }
        }

        return 0f;
    }

    private static Dictionary<int, float> IndexSensorsByCore(
        IHardware hardware, SensorType type, string[]? requireAll = null, string[]? requireNone = null)
    {
        var byCore = new Dictionary<int, float>();
        foreach (var sensor in hardware.Sensors.Where(s =>
                     s.SensorType == type && s.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase) && MatchesFilters(s.Name, requireAll, requireNone)))
        {
            byCore.TryAdd(ExtractCoreIndex(sensor.Name), sensor.Value ?? 0f);
        }

        return byCore;
    }

    private static float AverageMatching(
        IHardware hardware, SensorType type, string nameContains, string[]? requireAll = null, string[]? requireNone = null)
    {
        var matches = hardware.Sensors
            .Where(s => s.SensorType == type && s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase) &&
                        s.Value.HasValue && MatchesFilters(s.Name, requireAll, requireNone))
            .Select(s => s.Value!.Value)
            .ToList();

        return matches.Count == 0 ? 0f : matches.Average();
    }

    /// <summary>Extra name filters for disambiguating sensor variants that share a common substring
    /// (e.g. "Core #1" vs. "Core #1 (Effective)") — every string in <paramref name="requireAll"/>
    /// must appear in the name, none in <paramref name="requireNone"/> may.</summary>
    private static bool MatchesFilters(string name, string[]? requireAll, string[]? requireNone) =>
        (requireAll is null || requireAll.All(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) &&
        (requireNone is null || requireNone.All(s => !name.Contains(s, StringComparison.OrdinalIgnoreCase)));

    private static int ExtractCoreIndex(string sensorName)
    {
        var digits = new string(sensorName.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var index) ? index : 0;
    }
}
