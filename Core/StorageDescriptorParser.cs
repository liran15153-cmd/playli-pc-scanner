using System.Buffers.Binary;

namespace Playli.PcScanner;

// The collector targets win-x64. These layouts are from winioctl.h:
// https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-storage_adapter_descriptor
// https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-device_seek_penalty_descriptor
// https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-volume_disk_extents
public static class StorageDescriptorParser
{
    public static bool? ReadSsd(ReadOnlySpan<byte> adapter, ReadOnlySpan<byte> seek)
    {
        // Only known complete descriptors are accepted; a zeroed/partial output buffer
        // or a future layout must never turn an unsupported query into an SSD result.
        if (!HasLayout(adapter, 32) || !HasLayout(seek, 12)) return null;
        // ATA, SATA and NVMe only. Virtual, RAID, Spaces, network and generic SCSI
        // transports can hide mixed/unknown backing media, so remain unknown.
        if (adapter[24] is not (3 or 11 or 17)) return null;
        return seek[8] switch { 0 => true, 1 => false, _ => null };
    }

    public static uint? ReadSingleDiskNumber(ReadOnlySpan<byte> extents)
    {
        // VOLUME_DISK_EXTENTS has 8-byte alignment on win-x64. A multi-extent
        // volume is deliberately unsupported, even if some extents share a disk.
        if (extents.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(extents) != 1) return null;
        var offset = BinaryPrimitives.ReadInt64LittleEndian(extents[16..]);
        var length = BinaryPrimitives.ReadInt64LittleEndian(extents[24..]);
        if (offset < 0 || length <= 0 || offset > long.MaxValue - length) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(extents[8..]);
    }

    private static bool HasLayout(ReadOnlySpan<byte> descriptor, uint expectedSize) =>
        descriptor.Length >= expectedSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(descriptor) == expectedSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..]) == expectedSize;
}
