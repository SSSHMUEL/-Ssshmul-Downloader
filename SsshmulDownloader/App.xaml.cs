using System;
using System.IO;
using System.Windows;
using SsshmulDownloader.Storage;
using WpfApplication = System.Windows.Application;

namespace SsshmulDownloader
{
    public partial class App : WpfApplication
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    File.AppendAllText(PathUtils.GetLogFilePath(), $"[FATAL UNHANDLED] {args.ExceptionObject}\n");
                }
                catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    File.AppendAllText(PathUtils.GetLogFilePath(), $"[DISPATCHER UNHANDLED] {args.Exception}\n");
                }
                catch { }
            };

            base.OnStartup(e);
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
