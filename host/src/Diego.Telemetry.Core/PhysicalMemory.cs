// Physical RAM versus the library's virtual-memory node. GPU VRAM is neither.
namespace Diego.Telemetry;

/// <summary>
/// LibreHardwareMonitorLib 0.9.6 names both the physical node and the virtual
/// node "Memory Used" and "Memory Available". The physical node is hardware id
/// <c>/ram</c> ("Total Memory"). The virtual node is hardware id <c>/vram</c>
/// ("Virtual Memory"). That <c>/vram</c> id is not GPU video memory. GPU
/// counters stay on the GPU hardware id.
/// </summary>
public static class PhysicalMemory
{
    public static bool IsVirtual(SensorSample sample)
    {
        if (string.Equals(sample.HardwareName, "Virtual Memory", StringComparison.Ordinal))
        {
            return true;
        }

        var id = sample.HardwareId ?? "";
        return string.Equals(id, "/vram", StringComparison.Ordinal)
            || id.Contains("/vram/", StringComparison.Ordinal);
    }

    public static bool IsPhysicalData(SensorSample sample, string name)
    {
        return sample.HardwareType == "Memory"
            && sample.SensorType == "Data"
            && string.Equals(sample.Name, name, StringComparison.Ordinal)
            && !IsVirtual(sample);
    }
}
