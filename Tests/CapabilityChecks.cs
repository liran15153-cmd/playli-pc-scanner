using System.Text.Json;
using Playli.PcScanner;

internal static class CapabilityChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int InvalidArg=unchecked((int)0x80070057);
        var attempts=new List<int>();
        var measured=GpuCapabilityProbe.Read((feature,data)=>{
            if(feature==16){check(data.Length==5&&data.All(n=>n==0),"architecture1 uses node zero and the documented five DWORD layout");data[2]=0;data[4]=1;return 0;}
            if(feature==7){check(data.Length==1,"shader query has exactly one enum field");attempts.Add(data[0]);if(data[0]>0x67)return InvalidArg;data[0]=0x66;return 0;}
            if(feature==27){check(data.Length==3,"options5 uses exactly BOOL and two enum fields");data[1]=2;data[2]=11;return 0;}
            return InvalidArg;
        });
        check(measured.Uma==false&&measured.ShaderModel=="6.6"&&measured.RayTracing==true,"successful API queries retain separate memory, shader and DXR facts");
        check(attempts.SequenceEqual(new[]{0x6a,0x69,0x68,0x67}),"unknown shader requests decrease until the current runtime recognizes an enum");
        var current=GpuCapabilityProbe.Read((feature,data)=>{if(feature==7)return 0;return InvalidArg;});
        check(current.ShaderModel=="6.10","shader model 6.10 is a version string and cannot collapse to 6.1");
        var old=GpuCapabilityProbe.Read((feature,data)=>{if(feature==1){check(data.Length==4,"legacy architecture has four DWORD fields");data[2]=1;data[3]=1;return 0;}if(feature==27)return 0;return InvalidArg;});
        check(old.Uma==true&&old.ShaderModel is null&&old.RayTracing==false,"a measured UMA and unsupported DXR remain distinct from an unknown shader query");
        var failed=GpuCapabilityProbe.Read((_,_)=>InvalidArg);
        check(failed.Uma is null&&failed.ShaderModel is null&&failed.RayTracing is null,"unavailable runtime or API queries never become unsupported or dedicated facts");
        foreach(int invalid in new[]{-1,2,99}){
            var result=GpuCapabilityProbe.Read((feature,data)=>{if(feature==16){data[2]=invalid;return 0;}return InvalidArg;});
            check(result.Uma is null,"invalid architecture boolean stays unknown: "+invalid);
        }
        foreach(int invalid in new[]{0,0x50,0x6b,0xff}){
            var result=GpuCapabilityProbe.Read((feature,data)=>{if(feature==7){data[0]=invalid;return 0;}return InvalidArg;});
            check(result.ShaderModel is null,"invalid or newer-than-requested shader output stays unknown: "+invalid);
        }
        foreach(int invalid in new[]{-1,1,9,13,99}){
            var result=GpuCapabilityProbe.Read((feature,data)=>{if(feature==27){data[2]=invalid;return 0;}return InvalidArg;});
            check(result.RayTracing is null,"unknown ray tracing tier is not guessed: "+invalid);
        }
        var report=new HardwareReport{Cpu=new(){Name="Synthetic CPU",Cores=6,Threads=12,Features=new(){["sse2"]=true,["ssse3"]=true}},Gpus=[new(){Name="Synthetic GPU",ShaderModel="6.10",RayTracing=false}]};
        using(var document=JsonDocument.Parse(report.ToJson())){
            check(document.RootElement.GetProperty("cpu").GetProperty("threads").GetInt32()==12,"logical processor count is explicitly exported");
            check(document.RootElement.GetProperty("gpus")[0].GetProperty("shaderModel").GetString()=="6.10","shader version survives JSON without decimal truncation");
            check(document.RootElement.GetProperty("gpus")[0].GetProperty("rayTracing").GetBoolean()==false,"measured unsupported DXR is exported as false, not omitted");
        }
        foreach(int invalid in new[]{0,-1,2049}){var bad=new HardwareReport{Cpu=new(){Threads=invalid}};bool rejected=false;try{bad.ToJson();}catch(InvalidOperationException){rejected=true;}check(rejected,"logical processor bounds reject "+invalid);}
        foreach(string invalid in new[]{"6.11","6,7","7.0","6.01","unknown"}){var bad=new HardwareReport{Gpus=[new(){ShaderModel=invalid}]};bool rejected=false;try{bad.ToJson();}catch(InvalidOperationException){rejected=true;}check(rejected,"shader contract rejects "+invalid);}
    }
}
