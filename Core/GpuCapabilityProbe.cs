namespace Playli.PcScanner;

public sealed record GpuCapabilities(bool? Uma, string? ShaderModel, bool? RayTracing);

public static class GpuCapabilityProbe
{
    private static readonly int[] ShaderModels=[0x6a,0x69,0x68,0x67,0x66,0x65,0x64,0x63,0x62,0x61,0x60,0x51];
    private const int InvalidArg=unchecked((int)0x80070057);

    // The caller owns one actual D3D12 adapter. Buffers follow the documented
    // DWORD layouts, not a device name, Windows version or guessed capability.
    public static GpuCapabilities Read(Func<int,int[],int> query)
    {
        bool? uma=null,rayTracing=null;string? shader=null;
        var architecture=new int[5];var architectureResult=query(16,architecture);
        if(architectureResult<0){architecture=new int[4];architectureResult=query(1,architecture);}
        if(architectureResult>=0&&architecture[0]==0&&architecture.Skip(1).All(v=>v is 0 or 1))uma=architecture[2]==1;
        foreach(var requested in ShaderModels)
        {
            var value=new[]{requested};var result=query(7,value);
            if(result==InvalidArg)continue;
            if(result>=0&&ShaderModels.Contains(value[0])&&value[0]<=requested)shader=(value[0]>>4)+"."+(value[0]&15);
            break;
        }
        var options=new int[3];
        if(query(27,options)>=0&&options[0] is 0 or 1&&options[1] is >=0 and <=2&&options[2] is 0 or 10 or 11 or 12)rayTracing=options[2]!=0;
        return new GpuCapabilities(uma,shader,rayTracing);
    }
}
