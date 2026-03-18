using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace BlockUpdateWindowsDefender
{
    public partial class App : Application
    {
        private static int _fatalErrorReported;

        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ReportFatalError(e.Exception);
            e.Handled = true;
            Shutdown(-1);
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception ?? new Exception("Unknown fatal error.");
            ReportFatalError(exception);
        }

        private static void ReportFatalError(Exception exception)
        {
            if (Interlocked.Exchange(ref _fatalErrorReported, 1) == 1)
            {
                return;
            }

            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlockUpdateWindowsDefender");
            Directory.CreateDirectory(appFolder);
            var logFile = Path.Combine(appFolder, "startup-error.log");
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n";
            File.AppendAllText(logFile, message);

            MessageBox.Show(
                "Application startup failed.\n\n" +
                exception.Message +
                "\n\nDetails were written to:\n" + logFile,
                "Block Update Windows Defender",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
