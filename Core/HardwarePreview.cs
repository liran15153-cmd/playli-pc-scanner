using System.Text;

namespace Playli.PcScanner;

public static class HardwarePreview
{
    public static string Format(HardwareReport value)
    {
        static string Size(double? mib) => mib is null ? "לא ידוע" : mib>=1024?$"{mib/1024:0.#} GB":$"{mib:0.#} MB";
        var output=new StringBuilder();output.AppendLine("מעבד: "+(value.Cpu.Name??"לא זוהה"));output.AppendLine("זיכרון RAM: "+Size(value.RamMiB));output.AppendLine();
        output.AppendLine("ליבות פיזיות: "+(value.Cpu.Cores?.ToString()??"לא ידוע"));
        foreach(var gpu in value.Gpus) {
            output.AppendLine("כרטיס מסך: "+(gpu.Name??"לא זוהה"));
            var kind=gpu.MemoryType switch {"dedicated"=>"ייעודי","unified"=>"מאוחד","shared"=>"משותף",_=>"לא זוהה"};
            output.AppendLine("סוג זיכרון: "+kind+(gpu.MemoryTypeBasis=="model-family"?" (זיהוי משפחת הדגם)":""));
            output.AppendLine("הקצאה ייעודית: "+Size(gpu.VramMiB));
            output.AppendLine("תקרת זיכרון משותף מדווחת: "+Size(gpu.SharedMemoryMiB)+" · לא מחברים לייעודי");
            output.AppendLine("DirectX: "+(gpu.DirectX?.ToString()??"לא ידוע"));
            output.AppendLine("רמות תכונה: "+(gpu.FeatureLevels.Count>0?string.Join(", ",gpu.FeatureLevels):"לא נמדדו"));output.AppendLine();
        }
        if(value.Gpus.Count==0)output.AppendLine("כרטיס מסך: לא זוהה");
        output.AppendLine("מערכת הפעלה: "+value.Os.Name+" · "+(value.Os.Version??"גרסה לא ידועה")+" · "+value.Os.Architecture);
        for(var index=0;index<value.Volumes.Count;index++)
        {
            var volume=value.Volumes[index];
            var media=volume.Ssd switch {true=>"SSD",false=>"HDD",null=>"סוג לא ידוע"};
            output.AppendLine($"כונן קבוע {index+1}: {media} · נפח פנוי: {Size(volume.FreeMiB)}");
        }
        output.AppendLine();output.AppendLine("תמיכת מעבד: "+string.Join(" · ",value.Cpu.Features.Select(f=>f.Key.ToUpperInvariant()+": "+(f.Value?"כן":"לא"))));
        output.AppendLine("זמן הבדיקה: "+value.CapturedAt);
        return output.ToString();
    }
}
