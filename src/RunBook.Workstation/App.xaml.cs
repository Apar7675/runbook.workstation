using RunBook.Workstation.Services;
using System;
using System.Net.Http;
using System.Net.Sockets;
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
            DebugLogService.Write($"StartupCompanyContext | app=RunBook.Workstation | settings_path={WorkstationStorageService.SettingsPath} | shop_scope={WorkstationStorageService.GetCurrentShopScopeKey()}");

            if (ShouldRunControlIndependenceVerification())
            {
                RunControlIndependenceVerificationAsync().GetAwaiter().GetResult();
                Shutdown(0);
                return;
            }

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
                if (IsConnectivityException(args.Exception))
                {
                    args.Handled = true;
                    MessageBox.Show(
                        "RunBook could not reach RunBook Service. The app will stay open so you can reconnect or register the workstation.",
                        "RunBook Service Unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
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
                args.SetObserved();
            };
        }

        private static bool IsConnectivityException(Exception? ex)
        {
            if (ex == null)
                return false;

            if (ex is HttpRequestException || ex is TimeoutException)
                return true;

            if (ex is SocketException)
                return true;

            if (ex.InnerException is SocketException || ex.InnerException is HttpRequestException)
                return true;

            return false;
        }

        private static bool ShouldRunControlIndependenceVerification()
        {
            var value = Environment.GetEnvironmentVariable("RUNBOOK_WORKSTATION_VERIFY_CONTROL_INDEPENDENCE");
            return !string.IsNullOrWhiteSpace(value)
                && (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
        }

        private static async Task RunControlIndependenceVerificationAsync()
        {
            try
            {
                var verifier = new ControlIndependenceVerifier();
                var result = await verifier.RunAsync().ConfigureAwait(false);
                DebugLogService.Write($"Control independence verification completed | PASS={result.Passed} | ControlCallAttempts={result.ControlCallAttempts}");
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("RunControlIndependenceVerificationAsync", ex);
                throw;
            }
        }
    }
}
