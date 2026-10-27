namespace Playli.PcScanner;

public static class AutomaticScan
{
    public static async Task<bool> RunAsync(string token,Func<CancellationToken,Task<HardwareReport>> collect,Func<string,HardwareReport,CancellationToken,Task<bool>> send,CancellationToken cancel=default)
    {
        if(!ScanSession.IsToken(token))return false;
        cancel.ThrowIfCancellationRequested();
        var report=await collect(cancel).WaitAsync(cancel);
        cancel.ThrowIfCancellationRequested();
        return await send(token,report,cancel).WaitAsync(cancel);
    }
}
