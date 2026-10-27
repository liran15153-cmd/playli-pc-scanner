namespace Playli.PcScanner;

public static class GpuAdapterClassifier
{
    // DXGI's Microsoft Basic Render Driver can also be the primary fallback
    // adapter with Flags == 0 when the physical display driver is disabled.
    // Microsoft documents its well-known, non-unique vendor/device pair:
    // https://learn.microsoft.com/windows/win32/direct3ddxgi/d3d10-graphics-programming-guide-dxgi
    public static bool IsSoftware(uint flags, uint vendorId, uint deviceId) =>
        (flags & 2) != 0 || (vendorId == 0x1414 && deviceId == 0x8c);
}
