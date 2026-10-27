using System.Text;

namespace Playli.PcScanner;

public sealed class ReportFileExporter
{
    private readonly Func<string, string, CancellationToken, Task> write;
    public ReportFileExporter(Func<string, string, CancellationToken, Task>? write = null)
        => this.write = write ?? ((path, value, cancel) => File.WriteAllTextAsync(path, value, new UTF8Encoding(false), cancel));
    public async Task SaveAsync(string destination, string json, CancellationToken cancel = default)
    {
        var target=Path.GetFullPath(destination);
        var temporary=Path.Combine(Path.GetDirectoryName(target)!,".playli-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            await write(temporary,json,cancel);
            cancel.ThrowIfCancellationRequested();
            File.Move(temporary,target,true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
