using System.Text.Json;

namespace Playli.PcScanner;

public sealed class HardwareReport
{
    public int SchemaVersion => 1;
    public string Source => "playli-scanner";
    public string CapturedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public OsInfo Os { get; init; } = new();
    public CpuInfo Cpu { get; init; } = new();
    public double? RamMiB { get; init; }
    public List<GpuInfo> Gpus { get; init; } = [];
    public List<VolumeInfo> Volumes { get; init; } = [];

    public string ToJson()
    {
        if (Gpus.Count > 16 || Volumes.Count > 32) throw new InvalidOperationException("Hardware list exceeds schema bounds.");
        if (Cpu.Cores is { } cores && (cores < 1 || cores > 1024)) throw new InvalidOperationException("Invalid physical core count.");
        if (Cpu.Threads is { } threads && (threads < 1 || threads > 2048)) throw new InvalidOperationException("Invalid logical processor count.");
        if (RamMiB is { } ram && (!double.IsFinite(ram) || ram <= 0 || ram > 16 * 1024 * 1024)) throw new InvalidOperationException("Invalid RAM value.");
        foreach (var gpu in Gpus)
        {
            if(gpu.MemoryType is not ("unknown" or "dedicated" or "shared" or "unified"))throw new InvalidOperationException("Invalid GPU memory type.");
            if(gpu.MemoryTypeBasis is not ("unknown" or "model-family" or "reported" or "api"))throw new InvalidOperationException("Invalid GPU memory classification basis.");
            if(gpu.ShaderModel is { } shader&&!System.Text.RegularExpressions.Regex.IsMatch(shader,@"^(?:[2-5]\.[0-9]|6\.(?:[0-9]|10))$"))throw new InvalidOperationException("Invalid shader model version.");
            if(gpu.VramMiB is { } memory && (!double.IsFinite(memory) || memory < 0 || memory > 1024 * 1024))throw new InvalidOperationException("Invalid GPU memory.");
            if(gpu.SharedMemoryMiB is { } shared && (!double.IsFinite(shared) || shared < 0 || shared > 16 * 1024 * 1024))throw new InvalidOperationException("Invalid shared memory.");
        }
        foreach (var volume in Volumes) if (volume.FreeMiB is { } free && (!double.IsFinite(free) || free < 0 || free > 1024d * 1024 * 1024)) throw new InvalidOperationException("Invalid volume size.");
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}

public sealed class OsInfo
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Architecture { get; init; }
}

public sealed class CpuInfo
{
    public string? Name { get; init; }
    public int? Cores { get; init; }
    public int? Threads { get; init; }
    public Dictionary<string, bool> Features { get; init; } = [];
}

public sealed class GpuInfo
{
    public string? Name { get; init; }
    public string MemoryType { get; init; } = "unknown";
    public string MemoryTypeBasis { get; init; } = "unknown";
    public double? SharedMemoryMiB { get; init; }
    public double? VramMiB { get; init; }
    public double? DirectX { get; init; }
    public string? ShaderModel { get; init; }
    public bool? RayTracing { get; init; }
    public List<string> FeatureLevels { get; init; } = [];
}

public sealed class VolumeInfo
{
    public double? FreeMiB { get; init; }
    public bool? Ssd { get; init; }
}
