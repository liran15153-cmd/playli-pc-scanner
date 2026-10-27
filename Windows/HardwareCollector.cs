using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security;
using Microsoft.Win32;

namespace Playli.PcScanner.Windows;

internal static class HardwareCollector
{
    public static HardwareReport Collect(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        string? cpuName = null; int? cores = null, threads = null;
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true };
            using var search = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"), new ObjectQuery("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"), options);
            using var entries = search.Get();
            foreach (ManagementBaseObject entry in entries)
            {
                using (entry) { cpuName = Clean(entry["Name"] as string); cores = entry["NumberOfCores"] is uint count ? (int)Math.Clamp(count, 1u, 1024u) : null; threads=entry["NumberOfLogicalProcessors"] is uint logical&&logical is >=1 and <=2048?(int)logical:null; }
                break;
            }
        }
        catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or COMException) { }
        cancel.ThrowIfCancellationRequested();
        double? ram = GetPhysicallyInstalledSystemMemory(out var installedKiB) && installedKiB > 0 ? installedKiB / 1024d : null;
        var gpus = DxgiHardware.Read();
        cancel.ThrowIfCancellationRequested();
        var volumes = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            cancel.ThrowIfCancellationRequested();
            try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) volumes.Add(new VolumeInfo { FreeMiB = drive.AvailableFreeSpace / 1048576d, Ssd = StorageHardware.ReadSsd(drive, cancel) }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        var features = new Dictionary<string, bool> { ["avx"] = Avx.IsSupported, ["avx2"] = Avx2.IsSupported, ["sse42"] = Sse42.IsSupported, ["sse2"] = Sse2.IsSupported, ["ssse3"] = Ssse3.IsSupported };
        int? leaf1Ecx = X86Base.IsSupported && X86Base.CpuId(0, 0).Eax >= 1 ? X86Base.CpuId(1, 0).Ecx : null;
        if (CpuFeatureParser.ReadF16c(leaf1Ecx, Avx.IsSupported) is { } f16c) features["f16c"] = f16c;
        var version = Environment.OSVersion.Version;
        return new HardwareReport
        {
            Os = new OsInfo { Name = version.Build >= 22000 ? "Windows 11" : "Windows 10", Version = ReadOsVersion(version), Architecture = RuntimeInformation.OSArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", Architecture.X86 => "x86", _ => null } },
            Cpu = new CpuInfo { Name = cpuName, Cores = cores, Threads = threads, Features = features },
            RamMiB = ram, Gpus = gpus, Volumes = volumes.Take(32).ToList()
        };
    }

    internal static string? Clean(string? text) => text is null ? null : new string(text.Where(c => !char.IsControl(c)).Take(240).ToArray()).Trim();

    private static string ReadOsVersion(Version version)
    {
        // Read only these two non-identifying OS fields, in the 64-bit registry
        // view Microsoft documents for build/revision requirements:
        // https://learn.microsoft.com/intune/app-management/deployment/deploy-win32-update-package
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var build = key?.GetValue("CurrentBuildNumber", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            int? revision = key?.GetValue("UBR", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is int value ? value : null;
            return OsVersionParser.WithBuildRevision(version, build, revision);
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException)
        {
            return OsVersionParser.WithBuildRevision(version, null, null);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
