using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FormatToolbox.App;
using FormatToolbox.Core;
using FormatToolbox.Infrastructure;
using FormatToolbox.Infrastructure.Providers;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace FormatToolbox.Acceptance;

internal static class Program
{
    private static readonly List<object> Cases = [];
    private static string Root = null!, Samples = null!, Outputs = null!;
    private static bool OfficeOnly;
    private static bool WindowsOnly;
    [STAThread]
    public static int Main(string[] args)
    {
        Root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/acceptance/generated");
        OfficeOnly = args.Contains("--office-only");
        WindowsOnly = args.Contains("--windows-only");
        Samples = Path.Combine(Root,"samples"); Outputs=Path.Combine(Root,"outputs"); Directory.CreateDirectory(Outputs);
        var app = new FormatToolbox.App.App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var task = Run(); task.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal),TaskScheduler.Default);
        Dispatcher.Run();
        try { task.GetAwaiter().GetResult(); return 0; }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task Run()
    {
        if(WindowsOnly)
        {
            using var old=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root,"results.json")));
            Cases.AddRange(old.RootElement.GetProperty("Cases").EnumerateArray().Where(c=>!c.GetProperty("Name").GetString()!.Contains("small screen")).Select(c=>(object)c.Clone()));
            goto WindowCases;
        }
        if (OfficeOnly)
        {
            using var old=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root,"results.json")));
            Cases.AddRange(old.RootElement.GetProperty("Cases").EnumerateArray().Where(c=>!c.GetProperty("Name").GetString()!.Contains("long-table")&&c.GetProperty("Name").GetString()!="AutoCAD layouts").Select(c=>(object)c.Clone()));
            goto OfficeCases;
        }
        var image = new ImageConversionProvider(); var pdf=new PdfConversionProvider();
        foreach(var name in new[]{"raw","tiff_lzw","tiff_adobe_deflate"})
        {
            var input=$"multipage-{name}.tiff";
            await Convert(image,input,"png",3,check:r=>
            {
                var expected = new[]{(1654,2339),(1240,1754),(2339,1654)};
                for(int i=0;i<3;i++) { using var b=SKBitmap.Decode(r.OutputFiles[i]); Require((b.Width,b.Height)==expected[i],"TIFF frame dimensions/order"); }
            });
            await Convert(image,input,"tiff",1,check:r=>Require(ImageFrameReader.Read(r.OutputFiles[0]).Count==3,"TIFF roundtrip page count"));
            await Convert(pdf,input,"pdf",1,check:r=>Require(Pages(r.OutputFiles[0])==3,"TIFF to PDF page count"));
        }
        await Convert(image,"webp-alpha.webp","png",1,check:r=>
        {
            using var b=SKBitmap.Decode(r.OutputFiles[0]); Require(b.GetPixel(0,0).Alpha==0,"transparent WebP corner");
            Require(b.GetPixel(60,60).Alpha is >=168 and <=172,"WebP partial alpha");
        });
        await Convert(image,"webp-lossy.webp","jpg",1,new ImageOptions(Quality:90));
        await Convert(pdf,"webp-alpha.webp","pdf",1,check:r=>Require(Pages(r.OutputFiles[0])==1,"WebP PDF page count"));
        foreach(var input in new[]{"webp-animated.webp","webp-damaged.webp"})
            await Convert(image,input,"png",0,expected:ConversionStatus.Failed,check:r=>Require(r.ErrorCode==ErrorCodes.UnsupportedFormat,"WebP explicit unsupported error"));
        await Convert(pdf,"multipage-tiff_lzw.tiff","pdf",1,new PdfOptions(AdditionalInputs:[Path.Combine(Samples,"ocr-scanned.pdf"),Path.Combine(Samples,"webp-alpha.webp")]),check:r=>Require(Pages(r.OutputFiles[0])==7,"merge all TIFF frames + PDF + WebP"));
        var ocr=new OcrConversionProvider(Path.GetFullPath("src/FormatToolbox.Infrastructure/tessdata"));
        foreach(var name in new[]{"english.png","chinese.png","mixed.png","degraded.jpg","scanned.pdf"})
            await Convert(ocr,"ocr-"+name,"pdf",1,new OcrOptions(Languages:name.StartsWith("english")?"eng":name.StartsWith("chinese")?"chi_sim":"chi_sim+eng",Dpi:200),check:r=>Require(Pages(r.OutputFiles[0])==(name.EndsWith("pdf")?3:1),"OCR page count"));
        await Convert(ocr,"multipage-tiff_lzw.tiff","pdf",1,new OcrOptions(Dpi:200),check:r=>Require(Pages(r.OutputFiles[0])==3,"OCR all TIFF pages"));
        await Convert(ocr,"multipage-distorted.tiff","pdf",1,new OcrOptions(Dpi:200),check:r=>Require(Pages(r.OutputFiles[0])==3,"OCR distorted TIFF keeps all pages"));
        await Convert(ocr,"webp-lossy.webp","pdf",1,new OcrOptions(Languages:"eng",Dpi:200));
        await Convert(ocr,"large-scanned.pdf","pdf",1,new OcrOptions(Languages:"eng",PageRange:"1,20,40",Dpi:150),check:r=>Require(Pages(r.OutputFiles[0])==3,"large OCR selected page order/count"));
        await Convert(ocr,"large-scanned.pdf","pdf",1,new OcrOptions(Languages:"eng",Dpi:150),check:r=>Require(Pages(r.OutputFiles[0])==40,"large OCR full document page count"));
        await LargeRendering();
        WindowCases:
        foreach(var size in new[]{new Rect(0,0,800,560),new Rect(0,0,1024/1.5,560/1.5),new Rect(0,0,1366/2.0,728/2.0)})
            foreach(var name in new[]{"About","Feedback","PdfTools","Confirmation","Notice"}) await WindowCase(name,size);
        if(WindowsOnly)goto SaveResults;
        OfficeCases:
        var worker=Path.GetFullPath("src/FormatToolbox.Worker/bin/Release/net8.0-windows/FormatToolbox.Worker.exe");
        var excel=new FallbackConversionProvider("office.excel",new WorkerConversionProvider("msoffice.excel",["xlsx"],"Excel","Excel.Application",worker,TimeSpan.FromMinutes(3)),new WorkerConversionProvider("wps.spreadsheet",["xlsx"],"WPS","ket.application",worker,TimeSpan.FromMinutes(3)),"Excel/WPS");
        if((await excel.CheckAvailabilityAsync()).IsAvailable)
            await Convert(excel,"long-table.xlsx","pdf",1,check:r=>Require(Pages(r.OutputFiles[0])>=20,"long table must paginate, not shrink to one page"));
        else Cases.Add(new {Name="long-table.xlsx",Status="Blocked",Reason="No Excel/WPS automation registration"});
        var wps=new WorkerConversionProvider("wps.spreadsheet",["xlsx"],"WPS","ket.application",worker,TimeSpan.FromMinutes(3));
        if((await wps.CheckAvailabilityAsync()).IsAvailable) await Convert(wps,"long-table.xlsx","pdf",1,check:r=>Require(Pages(r.OutputFiles[0])>=20,"WPS long table must paginate"));
        var cad=OfficeComDetector.Find("AutoCAD.Application");
        if(cad is null) Cases.Add(new {Name="AutoCAD layouts",Status="Blocked",Reason="AutoCAD automation not installed; generator script supplied, no DWG execution claimed"});
        else
        {
            var cadPath=Path.Combine(Samples,"cad-layouts.dwg");
            if(File.Exists(cadPath)) await Convert(new WorkerConversionProvider("autocad.dwg",["dwg"],"AutoCAD","AutoCAD.Application",worker),"cad-layouts.dwg","pdf",1,check:r=>Require(Pages(r.OutputFiles[0])==3,"CAD paper layout count"));
            else Cases.Add(new {Name="AutoCAD layouts",Status="Blocked",Reason="Run Generate-AutoCadSample.ps1 before acceptance",Registration=cad.ProgId});
        }
        SaveResults:
        await File.WriteAllTextAsync(Path.Combine(Root,"results.json"),JsonSerializer.Serialize(new{Timestamp=DateTimeOffset.Now,Source="Generated benchmarks; not production samples",Environment=Environment.OSVersion.ToString(),Cases},new JsonSerializerOptions{WriteIndented=true}));
        if(Cases.Any(c=>JsonSerializer.Serialize(c).Contains("\"Status\":\"Failed\""))) throw new InvalidOperationException("Acceptance failures; see results.json");
    }
    private static async Task Convert(IConversionProvider provider,string input,string target,int count,ConversionOptions? options=null,ConversionStatus expected=ConversionStatus.Succeeded,Action<ConversionResult>? check=null)
    {
        var name=$"{input} -> {target} ({provider.Id})"; Console.WriteLine(name);
        var directory=Path.Combine(Outputs,$"{Path.GetFileNameWithoutExtension(input)}-{provider.Id}-{target}-{Cases.Count}"); Directory.CreateDirectory(directory);
        var sw=Stopwatch.StartNew(); var result=await provider.ConvertAsync(new(Path.Combine(Samples,input),target,directory,Options:options),null,CancellationToken.None);
        try { Require(result.Status==expected,result.ErrorMessage??$"status {result.Status}"); Require(result.OutputFiles.Count==count,"output count"); check?.Invoke(result); Require(!Directory.EnumerateFiles(directory,"*.tmp",SearchOption.AllDirectories).Any(),"temporary cleanup");
            Cases.Add(new{Name=name,Input=input,Status="Passed",ActualStatus=result.Status.ToString(),Engine=result.Engine,PageRange=(options as OcrOptions)?.PageRange,Seconds=sw.Elapsed.TotalSeconds,Outputs=result.OutputFiles,Warnings=result.Warnings}); }
        catch(Exception ex) { Cases.Add(new{Name=name,Input=input,Status="Failed",Reason=ex.Message,Seconds=sw.Elapsed.TotalSeconds,Outputs=result.OutputFiles}); }
    }
    private static async Task LargeRendering()
    {
        Console.WriteLine("Large PDF background rendering / cancellation");
        var directory=Path.Combine(Outputs,"large-render"); Directory.CreateDirectory(directory);
        var ticks=0; var gaps=new List<double>(); var watch=Stopwatch.StartNew(); var last=watch.Elapsed.TotalMilliseconds;
        var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(20),DispatcherPriority.Normal,(_,_)=> {var now=watch.Elapsed.TotalMilliseconds;gaps.Add(now-last);last=now;ticks++;},Dispatcher.CurrentDispatcher);timer.Start();
        var queue=new ConversionQueue(new ConversionRegistry([new PdfRenderProvider()]));
        var item=queue.Enqueue(new(Path.Combine(Samples,"large-scanned.pdf"),"jpg",directory,Options:new PdfRenderOptions(Dpi:144,JpegQuality:80)));
        await queue.WaitForIdleAsync(); timer.Stop();
        var process=Process.GetCurrentProcess();
        var passed=item.Status==ConversionStatus.Succeeded&&item.Result!.OutputFiles.Count==40&&ticks>5&&gaps.Max()<1000;
        Cases.Add(new{Name="40-page 72 MB scan background render",Status=passed?"Passed":"Failed",Seconds=watch.Elapsed.TotalSeconds,DispatcherTicks=ticks,MaximumDispatcherGapMs=gaps.DefaultIfEmpty(0).Max(),PeakWorkingSetMb=process.PeakWorkingSet64/1048576.0,OutputCount=item.Result?.OutputFiles.Count});
        using var token=new CancellationTokenSource(); var cancelWatch=new Stopwatch();
        var result=await new PdfRenderProvider().ConvertAsync(new(Path.Combine(Samples,"large-scanned.pdf"),"png",Path.Combine(Outputs,"large-cancel"),Options:new PdfRenderOptions(Dpi:200)),new Callback(p=> {if(p.Percent>=2.5&&!token.IsCancellationRequested){cancelWatch.Start();token.Cancel();}}),token.Token);
        Cases.Add(new{Name="Large scan cancellation",Status=result.Status==ConversionStatus.PartialSucceeded&&result.OutputFiles.Count==1&&cancelWatch.Elapsed.TotalSeconds<5?"Passed":"Failed",CancelSeconds=cancelWatch.Elapsed.TotalSeconds,ActualStatus=result.Status.ToString(),OutputCount=result.OutputFiles.Count});
        using var midPage=new CancellationTokenSource();var midWatch=Stopwatch.StartNew();
        var interrupted=new PdfRenderProvider().ConvertAsync(new(Path.Combine(Samples,"large-scanned.pdf"),"png",Path.Combine(Outputs,"large-cancel-active"),Options:new PdfRenderOptions(Dpi:600)),null,midPage.Token);
        await Task.Delay(25);midWatch.Restart();midPage.Cancel();var interruptedResult=await interrupted;
        Cases.Add(new{Name="Cancel during high DPI scan rendering",Status=interruptedResult.Status==ConversionStatus.Cancelled&&interruptedResult.OutputFiles.Count==0&&midWatch.Elapsed.TotalSeconds<5?"Passed":"Failed",CancelSeconds=midWatch.Elapsed.TotalSeconds,OutputCount=interruptedResult.OutputFiles.Count,Limitation="PDFium finishes an active page before observing cancellation; no incomplete file published"});
    }
    private static async Task WindowCase(string name,Rect area)
    {
        Window window=name switch{"About"=>new AboutWindow(),"Feedback"=>new FeedbackWindow([]),"PdfTools"=>new PdfToolsWindow(_=>{},Outputs),_=>new ConfirmationWindow(string.Join("\n",Enumerable.Repeat("已完成部分任务，请检查已保存的结果和警告。",30)),"确认操作",showCancel:name!="Notice")};
        WindowSizing.Fit(window,area);
        var content=(FrameworkElement)window.Content;
        var size=new Size(window.Width-16,window.Height-40);content.Measure(size);content.Arrange(new Rect(size));content.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.Background);
        var errors=new List<string>();
        foreach(var scroll in Descendants(content).OfType<ScrollViewer>().Where(s=>s.ViewportWidth>0&&s.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled))
            if(scroll.ExtentWidth>scroll.ViewportWidth+1)errors.Add("content clipped horizontally");
        foreach(var button in Descendants(content).OfType<Button>().Where(b=>b.Visibility==Visibility.Visible&&b.ActualWidth>0))
        {
            if(HasScrollableAncestor(button,content))continue;
            var bounds=button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
            if(bounds.Left < -1||bounds.Right>size.Width+1||bounds.Top < -1||bounds.Bottom>size.Height+1)errors.Add(button.Content?.ToString()??"button outside");
        }
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle(Brushes.White,null,new Rect(size));bitmap.Render(background);bitmap.Render(content);
        var screenshot=Path.Combine(Root,$"{name}-{(int)area.Width}x{(int)area.Height}.png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(screenshot))encoder.Save(stream);
        Cases.Add(new{Name=$"{name} small screen {area.Width:F0}x{area.Height:F0} DIP",Status=errors.Count==0?"Passed":"Failed",HiddenButtons=errors,Width=window.Width,Height=window.Height,Screenshot=screenshot,Method="WPF measured layout; simulated work area, no physical display DPI change"});window.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var d in Descendants(child))yield return d;}}
    private static bool HasScrollableAncestor(DependencyObject child,DependencyObject root){for(var p=VisualTreeHelper.GetParent(child);p!=null&&p!=root;p=VisualTreeHelper.GetParent(p))if(p is ScrollViewer)return true;return false;}
    private static int Pages(string path){using var pdf=PdfReader.Open(path,PdfDocumentOpenMode.Import);return pdf.PageCount;}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class Callback(Action<ConversionProgress> callback):IProgress<ConversionProgress>{public void Report(ConversionProgress value)=>callback(value);}
}
