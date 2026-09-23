using System;
using System.Diagnostics;

namespace SsshmulDownloader.Engine
{
    public static class ProcessTreeHelper
    {
        public static void KillProcessTree(Process? process)
        {
            if (process == null) return;
            try
            {
                if (process.HasExited) return;

                int pid = process.Id;
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                }
                catch { }

                // Fallback via taskkill to guarantee no hanging children
                if (!process.HasExited)
                {
                    try
                    {
                        using var killer = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {pid}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        killer?.WaitForExit(1000);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error killing process tree: {ex.Message}");
            }
        }
    }
}
