using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Playli.PcScanner;

var passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
await AutoScanChecks.RunAsync(Check);
CapabilityChecks.Run(Check);
Check(GpuAdapterClassifier.IsSoftware(2,0,0),"DXGI software-flagged adapters are excluded");
Check(GpuAdapterClassifier.IsSoftware(0,0x1414,0x8c),"Microsoft basic primary renderer is excluded even without a software flag");
Check(!GpuAdapterClassifier.IsSoftware(0,0x1002,0x9999),"physical AMD adapters are retained");
Check(!GpuAdapterClassifier.IsSoftware(0,0x10de,0x9999),"physical NVIDIA adapters are retained");
Check(!GpuAdapterClassifier.IsSoftware(0,0x8086,0x9999),"physical integrated Intel adapters are retained");
Check(!GpuAdapterClassifier.IsSoftware(0,0x1414,1),"Microsoft vendor alone does not classify unrelated devices as software");

var operations=new ScannerOperations();
var blockedScan=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
using(var waitCancel=new CancellationTokenSource()){
 var scan=operations.RunAsync(ScannerOperation.Scan,()=>blockedScan.Task,waitCancel.Token);
 Check(operations.Current==ScannerOperation.Scan,"scan owns its operation until actual collection settles");
 waitCancel.Cancel();try{await scan;Check(false,"cancelled scan wait must stop waiting");}catch(OperationCanceledException){Check(true,"UI scan wait can be cancelled");}
 Check(operations.Current==ScannerOperation.Scan&&!operations.WhenIdle.IsCompleted,"cancelled wait does not pretend blocked collection has stopped");
 var duplicateStarted=false;try{await operations.RunAsync(ScannerOperation.Scan,()=>{duplicateStarted=true;return Task.FromResult(2);});Check(false,"overlapping scan must be rejected");}catch(InvalidOperationException){Check(!duplicateStarted,"repeated scan cannot start another worker after timeout");}
 blockedScan.SetResult(1);await operations.WhenIdle.WaitAsync(TimeSpan.FromSeconds(2));
 Check(operations.Current==ScannerOperation.None,"operation becomes idle only after original worker ends");
}
var blockedSave=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var saving=operations.RunAsync(ScannerOperation.Export,()=>blockedSave.Task);
Check(!operations.CanClose&&!operations.WhenIdle.IsCompleted,"closing must defer while a save is pending");
try{await operations.RunAsync(ScannerOperation.Share,()=>Task.FromResult(true));Check(false,"share must wait for saving");}catch(InvalidOperationException){Check(true,"saving excludes sharing and duplicate actions");}
blockedSave.SetResult(3);Check(await saving==3&&operations.CanClose,"completed save allows pending close");
try{await operations.RunAsync<int>(ScannerOperation.Scan,()=>Task.FromException<int>(new IOException("controlled")));}catch(IOException){}
Check(operations.Current==ScannerOperation.None,"failed operation releases its state for recovery");

var exportFolder=Path.Combine(Path.GetTempPath(),"playli-export-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(exportFolder);
try{
 var destination=Path.Combine(exportFolder,"spec.json");await File.WriteAllTextAsync(destination,"old complete report");
 var writeStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finishWrite=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 var exporter=new ReportFileExporter(async(path,value,cancel)=>{await File.WriteAllTextAsync(path,"partial",cancel);writeStarted.SetResult();await finishWrite.Task.WaitAsync(cancel);await File.WriteAllTextAsync(path,value,cancel);});
 var save=exporter.SaveAsync(destination,"new complete report");await writeStarted.Task;
 Check(await File.ReadAllTextAsync(destination)=="old complete report","pending write cannot truncate the previous complete exported report");
 finishWrite.SetResult();await save;Check(await File.ReadAllTextAsync(destination)=="new complete report","only completed export replaces the selected report file");
 Check(Directory.GetFiles(exportFolder).Length==1,"completed atomic export removes its temporary file");
 var partialStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 using(var midCancel=new CancellationTokenSource()){
  var slow=new ReportFileExporter(async(path,value,cancel)=>{await File.WriteAllTextAsync(path,"incomplete",cancel);partialStarted.SetResult();await Task.Delay(Timeout.Infinite,cancel);});
  var partialSave=slow.SaveAsync(destination,"must not replace",midCancel.Token);await partialStarted.Task;midCancel.Cancel();
  try{await partialSave;Check(false,"mid-write cancellation must not commit");}catch(OperationCanceledException){Check(true,"pending export observes cancellation before commit");}
  Check(await File.ReadAllTextAsync(destination)=="new complete report"&&Directory.GetFiles(exportFolder).Length==1,"interrupted temporary write cannot replace the previous complete file");
 }

 using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{await new ReportFileExporter().SaveAsync(destination,"bad",cancelled.Token);}catch(OperationCanceledException){}
 Check(await File.ReadAllTextAsync(destination)=="new complete report"&&Directory.GetFiles(exportFolder).Length==1,"cancelled export preserves the previous file and cleans temporary data");
}finally{Directory.Delete(exportFolder,true);}

var windowsVersion=new Version(10,0,18363,0);
Check(GpuMemoryClassifier.FromReportedName("Intel(R) HD Graphics 630")=="unified","Intel HD family does not present a dedicated allocation as a graphics budget");
Check(GpuMemoryClassifier.FromReportedName("Intel UHD Graphics 770")=="unified","Intel UHD family is unified without an invented shared-memory budget");
Check(GpuMemoryClassifier.FromReportedName("NVIDIA GeForce RTX 3060")=="dedicated","identified discrete NVIDIA family retains its dedicated memory meaning");
Check(GpuMemoryClassifier.FromReportedName("AMD Radeon RX 6600")=="dedicated","identified discrete Radeon RX family retains its dedicated memory meaning");
Check(GpuMemoryClassifier.FromReportedName("AMD Radeon 780M")=="unknown","an unclassified integrated product cannot be assigned a dedicated pool");
var memoryReport=new HardwareReport{Gpus=[new GpuInfo{Name="Intel HD Graphics 630",MemoryType="unified",MemoryTypeBasis="model-family",VramMiB=128,SharedMemoryMiB=8192}]};
using(var memoryJson=JsonDocument.Parse(memoryReport.ToJson())){var gpu=memoryJson.RootElement.GetProperty("gpus")[0];Check(gpu.GetProperty("vramMiB").GetDouble()==128&&gpu.GetProperty("sharedMemoryMiB").GetDouble()==8192,"GPU export keeps dedicated and shared allocations separate");Check(gpu.GetProperty("memoryTypeBasis").GetString()=="model-family","classification is not misrepresented as a measured architecture API result");}
Check(HardwarePreview.Format(memoryReport).Contains("מאוחד")&&HardwarePreview.Format(memoryReport).Contains("משותף"),"consent preview explains unified and shared memory without adding it to dedicated VRAM");
Check(OsVersionParser.WithBuildRevision(windowsVersion,"18363",1350)=="10.0.18363.1350","matching Windows build appends the actual update revision");
Check(OsVersionParser.WithBuildRevision(windowsVersion,"18363",0)=="10.0.18363.0","explicit registry revision zero is preserved");
Check(OsVersionParser.WithBuildRevision(windowsVersion,"19045",1350)=="10.0.18363","another build cannot supply this OS revision");
Check(OsVersionParser.WithBuildRevision(windowsVersion,"18363",null)=="10.0.18363","missing UBR does not invent revision zero");
Check(OsVersionParser.WithBuildRevision(windowsVersion,null,1350)=="10.0.18363","missing registry build cannot validate a revision");
Check(OsVersionParser.WithBuildRevision(windowsVersion,"18363",-1)=="10.0.18363","invalid negative revision remains unknown");
Check(OsVersionParser.WithBuildRevision(windowsVersion,"18363 extra",1350)=="10.0.18363","malformed registry build remains unknown");
Check(OsVersionParser.WithBuildRevision(new Version(10,0,26100,1),"26100",2314)=="10.0.26100.2314","runtime revision never replaces a validated UBR");

Check(CpuFeatureParser.ReadF16c(0x20000000,true)==true,"F16C requires its CPUID bit and usable runtime AVX");
Check(CpuFeatureParser.ReadF16c(0x20000000,false)==false,"F16C CPU bit cannot bypass unavailable AVX OS state");
Check(CpuFeatureParser.ReadF16c(0,true)==false,"usable AVX alone does not imply F16C");
Check(CpuFeatureParser.ReadF16c(0x18000000,true)==false,"OSXSAVE and AVX CPUID bits do not imply F16C");
Check(CpuFeatureParser.ReadF16c(null,true) is null,"unavailable CPUID remains unknown");
Check(CpuFeatureParser.ReadF16c(null,false) is null,"unavailable CPUID cannot become a known F16C failure");

// Hand-built fixtures use the documented win-x64 structures, not a Windows API mock.
byte[] Adapter(byte bus) { var bytes=new byte[32];bytes[0]=32;bytes[4]=32;bytes[24]=bus;return bytes; }
byte[] Seek(byte incursPenalty) { var bytes=new byte[12];bytes[0]=12;bytes[4]=12;bytes[8]=incursPenalty;return bytes; }
Check(StorageDescriptorParser.ReadSsd(Adapter(11),Seek(0))==true,"complete SATA no-seek descriptor reports SSD");
Check(StorageDescriptorParser.ReadSsd(Adapter(11),Seek(1))==false,"complete SATA seek-penalty descriptor reports non-SSD");
Check(StorageDescriptorParser.ReadSsd(Adapter(3),Seek(0))==true,"ATA no-seek descriptor supported");
Check(StorageDescriptorParser.ReadSsd(Adapter(17),Seek(0))==true,"NVMe needs a complete no-seek descriptor");
Check(StorageDescriptorParser.ReadSsd(Adapter(17),[]) is null,"NVMe alone never substitutes for failed seek query");
foreach(byte bus in new byte[]{0,1,2,7,8,9,10,14,15,16,18,19,20,127,255})
 Check(StorageDescriptorParser.ReadSsd(Adapter(bus),Seek(0)) is null,"ambiguous storage transport stays unknown: "+bus);
Check(StorageDescriptorParser.ReadSsd([],Seek(0)) is null,"failed adapter query remains unknown");
Check(StorageDescriptorParser.ReadSsd(new byte[32],Seek(0)) is null,"zeroed adapter buffer cannot imply SSD");
Check(StorageDescriptorParser.ReadSsd(Adapter(11),new byte[12]) is null,"zeroed seek buffer cannot imply SSD");
foreach(int length in new[]{0,4,8,9,11})
 Check(StorageDescriptorParser.ReadSsd(Adapter(11),Seek(0).AsSpan(0,length)) is null,"short seek result stays unknown: "+length);
Check(StorageDescriptorParser.ReadSsd(Adapter(11).AsSpan(0,31),Seek(0)) is null,"truncated adapter stays unknown");
foreach(int field in new[]{0,4}) {
 var shortHeader=Seek(0);shortHeader[field]=8;
 Check(StorageDescriptorParser.ReadSsd(Adapter(11),shortHeader) is null,"undersized seek header stays unknown: "+field);
 var futureHeader=Seek(0);futureHeader[field]=16;
 Check(StorageDescriptorParser.ReadSsd(Adapter(11),futureHeader) is null,"unsupported or incomplete seek header stays unknown: "+field);
 var shortAdapter=Adapter(11);shortAdapter[field]=24;
 Check(StorageDescriptorParser.ReadSsd(shortAdapter,Seek(0)) is null,"undersized adapter header stays unknown: "+field);
 var futureAdapter=Adapter(11);futureAdapter[field]=36;
 Check(StorageDescriptorParser.ReadSsd(futureAdapter,Seek(0)) is null,"unsupported or incomplete adapter header stays unknown: "+field);
}
Check(StorageDescriptorParser.ReadSsd(Adapter(11),Seek(2)) is null,"unexpected seek flag stays unknown");
var extent=Convert.FromHexString("0100000000000000020000000000000000001000000000000000004000000000");
Check(StorageDescriptorParser.ReadSingleDiskNumber(extent)==2,"single win-x64 extent maps the volume to its own backing disk");
var diskZero=(byte[])extent.Clone();diskZero[8]=0;
Check(StorageDescriptorParser.ReadSingleDiskNumber(diskZero)==0,"physical disk zero is valid");
Check(StorageDescriptorParser.ReadSingleDiskNumber([]) is null,"failed extent query remains unknown");
Check(StorageDescriptorParser.ReadSingleDiskNumber(extent.AsSpan(0,31)) is null,"incomplete extent does not select a disk");
foreach(byte count in new byte[]{0,2,255}) {
 var multiple=(byte[])extent.Clone();multiple[0]=count;
 Check(StorageDescriptorParser.ReadSingleDiskNumber(multiple) is null,"non-single extent result stays unknown: "+count);
}
var invalidOffset=(byte[])extent.Clone();BinaryPrimitives.WriteInt64LittleEndian(invalidOffset.AsSpan(16),-1);
Check(StorageDescriptorParser.ReadSingleDiskNumber(invalidOffset) is null,"negative extent offset rejected");
var invalidLength=(byte[])extent.Clone();BinaryPrimitives.WriteInt64LittleEndian(invalidLength.AsSpan(24),0);
Check(StorageDescriptorParser.ReadSingleDiskNumber(invalidLength) is null,"empty extent rejected");
var overflowingExtent=(byte[])extent.Clone();BinaryPrimitives.WriteInt64LittleEndian(overflowingExtent.AsSpan(16),long.MaxValue);
Check(StorageDescriptorParser.ReadSingleDiskNumber(overflowingExtent) is null,"overflowing extent rejected");

var report=new HardwareReport {Cpu=new CpuInfo{Name="Intel Core i5-10400"},RamMiB=16384,Gpus=[new GpuInfo{Name="NVIDIA GeForce RTX 3060",VramMiB=12288,DirectX=12}],Volumes=[new VolumeInfo{FreeMiB=102400,Ssd=false},new VolumeInfo{FreeMiB=512,Ssd=true},new VolumeInfo{FreeMiB=2048}]};
var json=report.ToJson();
using(var document=JsonDocument.Parse(json)){
 Check(document.RootElement.GetProperty("schemaVersion").GetInt32()==1,"schema version");
 Check(document.RootElement.GetProperty("gpus")[0].GetProperty("vramMiB").GetDouble()==12288,"64-bit VRAM survives serialization");
 Check(document.RootElement.GetProperty("source").GetString()=="playli-scanner","source identity");
 var volumes=document.RootElement.GetProperty("volumes");
 Check(volumes[0].GetProperty("freeMiB").GetDouble()==102400&&volumes[0].GetProperty("ssd").GetBoolean()==false,"non-SSD capacity stays attached to its own volume");
 Check(volumes[1].GetProperty("freeMiB").GetDouble()==512&&volumes[1].GetProperty("ssd").GetBoolean()==true,"SSD capacity cannot borrow free space from a different volume");
 Check(volumes[2].GetProperty("ssd").ValueKind==JsonValueKind.Null,"unknown storage remains null through JSON serialization");
 Check(volumes.EnumerateArray().All(volume=>volume.EnumerateObject().Select(property=>property.Name).Order().SequenceEqual(new[]{"freeMiB","ssd"})),"volume export allowlist excludes device numbers and paths");
}
Check(!json.Contains("MachineName")&&!json.Contains("UserName")&&!json.Contains("SerialNumber"),"hardware projection has no personal properties");

var previewReport=new HardwareReport {
 Os=new OsInfo{Name="Windows 10",Version="10.0.18363.1350",Architecture="x64"},
 Cpu=new CpuInfo{Name="Example CPU",Cores=6,Features=new Dictionary<string,bool>{{"f16c",true},{"avx2",false}}},
 Volumes=[new VolumeInfo{FreeMiB=1024,Ssd=true},new VolumeInfo{FreeMiB=2048,Ssd=false},new VolumeInfo{FreeMiB=3072,Ssd=null}]
};
var consentPreview=HardwarePreview.Format(previewReport);
Check(consentPreview.Contains("ליבות פיזיות: 6"),"consent preview includes the physical core count exported for comparison");
Check(HardwarePreview.Format(report).Contains("ליבות פיזיות: לא ידוע"),"unknown physical core count is visible without inference");
foreach(var cores in new[]{0,1025}){
 var rejected=false;try{_ = new HardwareReport{Cpu=new CpuInfo{Cores=cores}}.ToJson();}catch(InvalidOperationException){rejected=true;}
 Check(rejected,"export rejects physical core count outside the report bounds: "+cores);
}
using(var exportedPreview=JsonDocument.Parse(previewReport.ToJson())) {
 var volumes=exportedPreview.RootElement.GetProperty("volumes");
 Check(volumes[0].GetProperty("ssd").GetBoolean()&&volumes[0].GetProperty("freeMiB").GetDouble()==1024&&consentPreview.Contains("כונן קבוע 1: SSD · נפח פנוי: 1 GB"),"consent preview pairs the first volume's exported SSD fact with its capacity");
 Check(!volumes[1].GetProperty("ssd").GetBoolean()&&volumes[1].GetProperty("freeMiB").GetDouble()==2048&&consentPreview.Contains("כונן קבוע 2: HDD · נפח פנוי: 2 GB"),"consent preview pairs the second volume's exported HDD fact with its capacity");
 Check(volumes[2].GetProperty("ssd").ValueKind==JsonValueKind.Null&&volumes[2].GetProperty("freeMiB").GetDouble()==3072&&consentPreview.Contains("כונן קבוע 3: סוג לא ידוע · נפח פנוי: 3 GB"),"consent preview preserves the third volume's exported unknown media");
}
Check(consentPreview.Split('\n').Count(line=>line.StartsWith("כונן קבוע "))==3,"consent preview presents every volume in a separate entry");
Check(consentPreview.Contains("10.0.18363.1350"),"consent preview includes the exported Windows update revision");
Check(consentPreview.Contains("F16C: כן")&&consentPreview.Contains("AVX2: לא"),"consent preview preserves supported and unsupported CPU feature facts");

async Task<string> Send(LoopbackBridge bridge,string request){
 using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",bridge.Port);var stream=client.GetStream();
 await stream.WriteAsync(Encoding.ASCII.GetBytes(request));await stream.FlushAsync();
 using var reader=new StreamReader(stream,Encoding.UTF8);return await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
}
await using(var bridge=new LoopbackBridge(report,TimeSpan.FromSeconds(15))){
 var task=bridge.ServeAsync();var host="127.0.0.1:"+bridge.Port;
 string Get(string origin,string token,string? overrideHost=null)=>"GET /v1/specs HTTP/1.1\r\nHost: "+(overrideHost??host)+"\r\nOrigin: "+origin+"\r\nAuthorization: Bearer "+token+"\r\n\r\n";
 var bad=await Send(bridge,Get("https://evil.invalid",bridge.Token));Check(bad.StartsWith("HTTP/1.1 403"),"untrusted origin denied");Check(!bad.Contains("i5-10400"),"denial contains no hardware");
 bad=await Send(bridge,Get("https://playli.co",new string('0',64)));Check(bad.StartsWith("HTTP/1.1 403"),"wrong bearer token denied");
 bad=await Send(bridge,Get("https://playli.co",bridge.Token,"evil.invalid"));Check(bad.StartsWith("HTTP/1.1 403"),"wrong host denied");
 bad=await Send(bridge,"GET /files HTTP/1.1\r\nHost: "+host+"\r\nOrigin: https://playli.co\r\n\r\n");Check(!bad.Contains("i5-10400")&&bad.StartsWith("HTTP/1.1 404"),"only one fixed endpoint");
 var preflight=await Send(bridge,"OPTIONS /v1/specs HTTP/1.1\r\nHost: "+host+"\r\nOrigin: https://playli.co\r\nAccess-Control-Request-Method: GET\r\nAccess-Control-Request-Headers: authorization\r\nAccess-Control-Request-Private-Network: true\r\n\r\n");
 Check(preflight.StartsWith("HTTP/1.1 204"),"preflight succeeds");Check(preflight.Contains("Access-Control-Allow-Private-Network: true"),"private network preflight explicit");Check(!preflight.Contains("i5-10400"),"preflight reveals no report");
 var duplicated=await Send(bridge,"GET /v1/specs HTTP/1.1\r\nHost: "+host+"\r\nOrigin: https://playli.co\r\nOrigin: https://evil.invalid\r\n\r\n");Check(duplicated.StartsWith("HTTP/1.1 400"),"duplicate headers rejected");
 var oversized=await Send(bridge,"GET /v1/specs HTTP/1.1\r\nHost: "+host+"\r\nX-Padding: "+new string('x',9000)+"\r\n\r\n");Check(oversized.StartsWith("HTTP/1.1 431"),"HTTP headers bounded");
 var success=await Send(bridge,Get("https://playli.co",bridge.Token));
 Check(success.StartsWith("HTTP/1.1 200"),"valid handoff succeeds");Check(success.Contains("Access-Control-Allow-Origin: https://playli.co"),"CORS uses exact origin");Check(!success.Contains("Allow-Origin: *"),"no wildcard CORS");Check(success.Contains("Cache-Control: no-store"),"report cannot be cached");
 var body=success.Split("\r\n\r\n",2)[1];Check(JsonDocument.Parse(body).RootElement.GetProperty("ramMiB").GetDouble()==16384,"real HTTP response uses the report schema");
 Check(await task.WaitAsync(TimeSpan.FromSeconds(3)),"one successful response ends the bridge");
 using var second=new TcpClient();try{await second.ConnectAsync("127.0.0.1",bridge.Port);Check(false,"listener must close");}catch(SocketException){Check(true,"one-shot listener is closed");}
}
await using(var timeout=new LoopbackBridge(report,TimeSpan.FromMilliseconds(200))){Check(!await timeout.ServeAsync(),"unused bridge expires");}
await using(var cancel=new LoopbackBridge(report)){using var source=new CancellationTokenSource();var waiting=cancel.ServeAsync(source.Token);source.Cancel();Check(!await waiting,"cancellation stops an already waiting share");Check(report.ToJson()==json,"cancelling sharing preserves the exportable report");}
await using(var disposing=new LoopbackBridge(report)){
 var waiting=disposing.ServeAsync();
 using var idleClient=new TcpClient();await idleClient.ConnectAsync("127.0.0.1",disposing.Port);
 // Ordinary window shutdown while a browser connection is idle. No request
 // bytes, malformed traffic, forced disconnect or reset are sent.
 await Task.Delay(50);
 await disposing.DisposeAsync();
 Check(!await waiting.WaitAsync(TimeSpan.FromSeconds(1)),"disposal completes a pending connection without a fault or the request timeout");
 await disposing.DisposeAsync();
 Check(!await disposing.ServeAsync(),"disposed sharing cannot restart and repeated disposal is harmless");
}
Console.WriteLine($"{passed} scanner contract/protocol checks passed on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. Windows hardware/UI execution is separate.");
