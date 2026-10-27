using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Playli.PcScanner.Windows;

internal static class StorageHardware
{
    // Both query-only IOCTLs use FILE_ANY_ACCESS in the Windows SDK winioctl.h.
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint IoctlStorageQueryProperty = 0x002d1400;
    private const uint StorageAdapterProperty = 1;
    private const uint StorageDeviceSeekPenaltyProperty = 7;

    public static bool? ReadSsd(DriveInfo drive, CancellationToken cancel)
    {
        // DriveInfo.GetDrives supplies roots. Keep paths and device numbers local;
        // do not infer a volume's media from another drive or enumerate disk IDs.
        var root = drive.Name;
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\') return null;
        cancel.ThrowIfCancellationRequested();
        using var volume = OpenQueryHandle(@"\\.\" + root[..2]);
        if (volume.IsInvalid) return null;
        var diskNumber = StorageDescriptorParser.ReadSingleDiskNumber(Query(volume, IoctlVolumeGetVolumeDiskExtents, null, 32));
        if (diskNumber is null) return null;
        cancel.ThrowIfCancellationRequested();
        using var disk = OpenQueryHandle(@"\\.\PhysicalDrive" + diskNumber.Value.ToString(CultureInfo.InvariantCulture));
        if (disk.IsInvalid) return null;
        // The adapter descriptor contains a bus type and no serial/name/unique-ID
        // strings. Never request STORAGE_DEVICE_DESCRIPTOR or device identifiers.
        var adapter = QueryProperty(disk, StorageAdapterProperty, 32);
        cancel.ThrowIfCancellationRequested();
        var seek = QueryProperty(disk, StorageDeviceSeekPenaltyProperty, 12);
        return StorageDescriptorParser.ReadSsd(adapter, seek);
    }

    private static SafeFileHandle OpenQueryHandle(string path) =>
        // Desired access 0: no data read/write rights, elevation or access retry.
        // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew
        CreateFileW(path, 0, 1 | 2, IntPtr.Zero, 3, 0, IntPtr.Zero);

    private static byte[] QueryProperty(SafeFileHandle device, uint property, int outputSize)
    {
        // STORAGE_PROPERTY_QUERY includes the one-byte AdditionalParameters
        // placeholder and trailing padding. QueryType 0 is PropertyStandardQuery.
        var query = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(query, property);
        return Query(device, IoctlStorageQueryProperty, query, outputSize);
    }

    private static byte[] Query(SafeFileHandle device, uint controlCode, byte[]? input, int outputSize)
    {
        var output = new byte[outputSize];
        return DeviceIoControl(device, controlCode, input, (uint)(input?.Length ?? 0), output,
            (uint)output.Length, out var bytesReturned, IntPtr.Zero) && bytesReturned == output.Length ? output : [];
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, [In] byte[]? input,
        uint inputSize, [Out] byte[] output, uint outputSize, out uint bytesReturned, IntPtr overlapped);
}
