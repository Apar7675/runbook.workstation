using RunBook.Workstation.Services;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace RunBook.Workstation
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            WorkstationStorageService.EnsureRuntimeFolders();
            WireGlobalExceptionLogging();
            DebugLogService.Write("OnStartup | Begin");
            base.OnStartup(e);

            try
            {
                DebugLogService.Write("OnStartup | Creating MainWindow");
                var window = new MainWindow();
                MainWindow = window;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                DebugLogService.Write("OnStartup | Showing MainWindow");
                window.Show();
                DebugLogService.Write("OnStartup | MainWindow shown");
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("OnStartup", ex);
                throw;
            }
        }

        private static void WireGlobalExceptionLogging()
        {
            Current.DispatcherUnhandledException += (_, args) =>
            {
                DebugLogService.WriteException("DispatcherUnhandledException", args.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                DebugLogService.Write("AppDomainUnhandledException | IsTerminating=" + args.IsTerminating);
                if (args.ExceptionObject is Exception ex)
                    DebugLogService.WriteException("AppDomainUnhandledException", ex);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                DebugLogService.WriteException("TaskSchedulerUnobservedTaskException", args.Exception);
            };
        }
    }
}
