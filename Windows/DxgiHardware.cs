using System.Runtime.InteropServices;

namespace Playli.PcScanner.Windows;

internal static class DxgiHardware
{
    private static readonly uint[] Levels = [0xc100, 0xc000, 0xb100, 0xb000, 0xa100, 0xa000, 0x9300];
    public static List<GpuInfo> Read()
    {
        var result = new List<GpuInfo>(); IntPtr factory = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(in iid, out factory) < 0) return result;
            var enumerate = Function<EnumAdapter>(factory, 12);
            for (uint index = 0; index < 16; index++)
            {
                if (enumerate(factory, index, out var adapter) < 0) break;
                try
                {
                    if (Function<GetDescription>(adapter, 10)(adapter, out var desc) < 0 || GpuAdapterClassifier.IsSoftware(desc.Flags, desc.VendorId, desc.DeviceId)) continue;
                    double? api = null; uint highest = 0;var capabilities=new GpuCapabilities(null,null,null);
                    IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
                    try
                    {
                        if (D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0, Levels, (uint)Levels.Length, 7, out device, out highest, out context) >= 0) api = 11;
                    }
                    finally { if (context != IntPtr.Zero) Marshal.Release(context); if (device != IntPtr.Zero) Marshal.Release(device); }
                    device = IntPtr.Zero;
                    try
                    {
                        var deviceId = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
                        if (D3D12CreateDevice(adapter, 0xb000, in deviceId, out device) >= 0&&device!=IntPtr.Zero){
                            api=12;var query=Function<CheckFeatureSupport>(device,13);
                            capabilities=GpuCapabilityProbe.Read((feature,data)=>{
                                var pinned=GCHandle.Alloc(data,GCHandleType.Pinned);
                                try{return query(device,feature,pinned.AddrOfPinnedObject(),checked((uint)data.Length*4));}
                                finally{pinned.Free();}
                            });
                        }
                    }
                    catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
                    finally { if (device != IntPtr.Zero) Marshal.Release(device); }
                    var name=HardwareCollector.Clean(desc.Description);var memoryType=capabilities.Uma is { } uma?uma?"unified":desc.DedicatedVideoMemory.ToUInt64()>0?"dedicated":"unknown":GpuMemoryClassifier.FromReportedName(name);
                    result.Add(new GpuInfo {Name=name,MemoryType=memoryType,MemoryTypeBasis=memoryType=="unknown"?"unknown":capabilities.Uma is not null?"api":"model-family",VramMiB=desc.DedicatedVideoMemory.ToUInt64()/1048576d,SharedMemoryMiB=desc.SharedSystemMemory.ToUInt64()/1048576d,DirectX=api,ShaderModel=capabilities.ShaderModel,RayTracing=capabilities.RayTracing,FeatureLevels=Levels.Where(level=>level<=highest).Select(level=>(level>>12)+"_"+((level>>8)&15)).ToList()});
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or COMException) { }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return result;
    }

    private static T Function<T>(IntPtr pointer, int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), index * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckFeatureSupport(IntPtr self,int feature,IntPtr data,uint bytes);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(in Guid id, out IntPtr factory);
    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, uint[] levels, uint levelCount, uint sdkVersion, out IntPtr device, out uint level, out IntPtr context);
    [DllImport("d3d12.dll", ExactSpelling = true)] private static extern int D3D12CreateDevice(IntPtr adapter, uint minimumLevel, in Guid id, out IntPtr device);
}
