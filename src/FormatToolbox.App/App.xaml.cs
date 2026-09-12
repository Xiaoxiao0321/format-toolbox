using System.Windows;
using FormatToolbox.Infrastructure;
namespace FormatToolbox.App;
public partial class App : System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) DiagnosticLog.Write("app.unhandled", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticLog.Write("app.unobserved-task", e.Exception);
            e.SetObserved();
        };
        DispatcherUnhandledException += (_, e) => DiagnosticLog.Write("app.dispatcher", e.Exception);
    }
}
