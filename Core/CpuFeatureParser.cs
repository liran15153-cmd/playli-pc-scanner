namespace Playli.PcScanner;

public static class CpuFeatureParser
{
    // Intel SDM vol. 1 section 14.4.1 requires ECX.F16C[29] plus usable
    // AVX/YMM OS state. The caller supplies Avx.IsSupported for that gate.
    public static bool? ReadF16c(int? leaf1Ecx, bool avxUsable) =>
        leaf1Ecx is { } flags ? avxUsable && (flags & 0x20000000) != 0 : null;
}
