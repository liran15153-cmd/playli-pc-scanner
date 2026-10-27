using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Playli.PcScanner;

public sealed class ReportTransferClient(HttpClient client)
{
    public const string Endpoint="https://partners.playli.co/pc-check/report";
    public async Task<bool> SendAsync(string token,HardwareReport report,CancellationToken cancel=default)
    {
        if(!ScanSession.IsToken(token))return false;
        var json=report.ToJson();if(Encoding.UTF8.GetByteCount(json)>65536)return false;
        for(var attempt=0;attempt<3;attempt++)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,Endpoint){Content=new StringContent(json,Encoding.UTF8,"application/json")};
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel);
                if(response.IsSuccessStatusCode)
                {
                    if(response.Content.Headers.ContentLength>4096||response.Content.Headers.ContentType?.MediaType!="application/json")return false;
                    await using var stream=await response.Content.ReadAsStreamAsync(cancel);
                    var bytes=new byte[4097];var total=0;
                    while(total<bytes.Length){var read=await stream.ReadAsync(bytes.AsMemory(total),cancel);if(read==0)break;total+=read;}
                    if(total>4096)return false;
                    try{using var receipt=JsonDocument.Parse(bytes.AsMemory(0,total));return receipt.RootElement.ValueKind==JsonValueKind.Object&&receipt.RootElement.TryGetProperty("received",out var accepted)&&accepted.ValueKind==JsonValueKind.True;}
                    catch(JsonException){return false;}
                }
                if((int)response.StatusCode<500&&response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))return false;
            }
            catch(OperationCanceledException) when(!cancel.IsCancellationRequested){}
            catch(Exception error) when(error is HttpRequestException or IOException){}
            if(attempt<2)await Task.Delay(250*(attempt+1),cancel);
        }
        return false;
    }
}
