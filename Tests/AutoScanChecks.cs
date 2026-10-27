using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Playli.PcScanner;

internal static class AutoScanChecks
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var token=new string('a',64);
        check(ScanSession.TokenFromExecutable(@"C:\Downloads\PLAYLI-PC-Check-"+token+".exe")==token,"automatic download filename binds the scan to its transfer");
        check(ScanSession.TokenFromExecutable("PLAYLI-PC-Check-"+token+" (1).exe")==token,"browser duplicate filename suffix is supported");
        foreach(var invalid in new[]{"PLAYLI-PC-Check.exe","PLAYLI-PC-Check-short.exe","other-"+token+".exe","PLAYLI-PC-Check-"+token+".zip"})
            check(ScanSession.TokenFromExecutable(invalid) is null,"an unbound or malformed executable filename cannot collect or transmit");
        var collected=0;var transmitted=0;var receipt=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sample=new HardwareReport{Cpu=new(){Name="Synthetic CPU"},RamMiB=16384};
        var running=AutomaticScan.RunAsync(token,_=>{collected++;return Task.FromResult(sample);},(received,report,_)=>{transmitted++;check(received==token&&ReferenceEquals(report,sample),"automatic transfer uses only the collected report and bound capability");return receipt.Task;});
        check(!running.IsCompleted&&collected==1&&transmitted==1,"one-shot process waits for server acknowledgement");
        receipt.SetResult(true);check(await running,"server acknowledgement completes the one-shot flow");
        var touched=false;check(!await AutomaticScan.RunAsync("invalid",_=>{touched=true;return Task.FromResult(sample);},(_,_,_)=>Task.FromResult(true))&&!touched,"missing authorization binding exits before hardware collection");
        check(!await AutomaticScan.RunAsync(token,_=>Task.FromResult(sample),(_,_,_)=>Task.FromResult(false)),"failed delivery cannot become success");
        using(var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(20))){
            var never=new TaskCompletionSource<HardwareReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            try{await AutomaticScan.RunAsync(token,_=>never.Task,(_,_,_)=>Task.FromResult(true),timeout.Token).WaitAsync(TimeSpan.FromSeconds(1));check(false,"stalled collection must stop waiting");}
            catch(OperationCanceledException){check(true,"a stalled hardware API cannot keep the one-shot process waiting beyond its deadline");}
        }
        using(var cancelled=new CancellationTokenSource()){
            cancelled.Cancel();try{await AutomaticScan.RunAsync(token,_=>Task.FromResult(sample),(_,_,_)=>Task.FromResult(true),cancelled.Token);check(false,"cancelled automatic scan must exit");}
            catch(OperationCanceledException){check(true,"automatic flow respects its bounded lifetime");}
        }
        var calls=0;
        using var client=new HttpClient(new Handler(async(request,cancel)=>{
            calls++;check(request.RequestUri?.AbsoluteUri==ReportTransferClient.Endpoint,"native upload destination is fixed PLAYLI HTTPS");
            check(request.Method==HttpMethod.Post&&request.Headers.Authorization?.ToString()=="Bearer "+token,"native report uses the upload capability in an authorization header");
            var payload=await request.Content!.ReadAsStringAsync(cancel);check(payload.Contains("Synthetic CPU")&&!payload.Contains(token),"capability is not embedded in report data");
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"received\":true}",Encoding.UTF8,"application/json")};
        }));
        check(await new ReportTransferClient(client).SendAsync(token,sample)&&calls==1,"HTTPS client accepts an explicit server receipt");
        using var rejected=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"received\":false}")})));
        check(!await new ReportTransferClient(rejected).SendAsync(token,sample),"HTTP success without a receipt is not delivery");
        using var expired=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        check(!await new ReportTransferClient(expired).SendAsync(token,sample),"expired transfer fails without an upload retry loop");
        foreach(var malformed in new[]{"[]","null","true","42","\"received\"","{\"received\":\"true\"}"}){
            using var malformedClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(malformed,Encoding.UTF8,"application/json")})));
            check(!await new ReportTransferClient(malformedClient).SendAsync(token,sample),"a non-object or malformed receipt cannot confirm delivery");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)=>send(request,cancel);
    }
}
