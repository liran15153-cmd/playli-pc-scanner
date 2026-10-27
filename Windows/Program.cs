using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Playli.PcScanner.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try{return Run();}catch{return 1;}
    }
    private static int Run()
    {
        var token=ScanSession.TokenFromExecutable(Environment.ProcessPath??"");
        if(token is null)return 2;
        var mutexName=@"Local\PLAYLI.PcCheck."+Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        using var single=new Mutex(true,mutexName,out var owns);
        if(!owns)return 0;
        try
        {
            using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var handler=new HttpClientHandler{AllowAutoRedirect=false};
            using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
            var sender=new ReportTransferClient(client);
            return AutomaticScan.RunAsync(token,cancel=>Task.Run(()=>HardwareCollector.Collect(cancel),cancel),sender.SendAsync,lifetime.Token).GetAwaiter().GetResult()?0:1;
        }
        catch{return 1;}
        finally{single.ReleaseMutex();}
    }
}
