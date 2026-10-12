using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SsshmulDownloader.Server
{
    public static class ProtocolHandler
    {
        public static void RegisterProtocols()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ??
                                 Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsshmulDownloader.exe");

                if (!File.Exists(exePath)) return;

                // Register ssshmul and ssshmuldownloader protocols
                RegisterScheme("ssshmul", exePath);
                RegisterScheme("ssshmuldownloader", exePath);

                // Auto-register Native Messaging Host for Chrome, Edge, and Brave
                RegisterNativeMessagingHost(exePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register URI schemes: {ex.Message}");
            }
        }

        private static void RegisterScheme(string scheme, string exePath)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
                if (key != null)
                {
                    key.SetValue("", "URL:Ssshmul Downloader");
                    key.SetValue("FriendlyTypeName", "Ssshmul Downloader");
                    key.SetValue("URL Protocol", "");

                    using var iconKey = key.CreateSubKey("DefaultIcon");
                    iconKey?.SetValue("", $"\"{exePath}\",0");

                    using var shellKey = key.CreateSubKey(@"shell\open\command");
                    shellKey?.SetValue("", $"\"{exePath}\" --protocol-launch \"%1\"");
                }
            }
            catch { }
        }

        private static void RegisterNativeMessagingHost(string exePath)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string baseDir = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", ".."));
                string nativeHostDir = Path.Combine(baseDir, "extension", "native_host");
                
                if (!Directory.Exists(nativeHostDir))
                {
                    nativeHostDir = Path.Combine(appDir, "extension", "native_host");
                }

                if (Directory.Exists(nativeHostDir))
                {
                    string batPath = Path.Combine(nativeHostDir, "ssshmul_host.bat");
                    string jsonPath = Path.Combine(nativeHostDir, "com.ssshmuldownloader.host.json");

                    string jsonContent = "{\n" +
                        "  \"name\": \"com.ssshmuldownloader.host\",\n" +
                        "  \"description\": \"Ssshmul Downloader Native Messaging Host\",\n" +
                        $"  \"path\": \"{batPath.Replace("\\", "\\\\")}\",\n" +
                        "  \"type\": \"stdio\",\n" +
                        "  \"allowed_origins\": [\n" +
                        "    \"chrome-extension://pmadpefjbmdcdcjphbbdbljnncecebnp/\"\n" +
                        "  ]\n" +
                        "}";

                    File.WriteAllText(jsonPath, jsonContent);

                    string[] browsers = new[] {
                        @"Software\Google\Chrome\NativeMessagingHosts\com.ssshmuldownloader.host",
                        @"Software\Microsoft\Edge\NativeMessagingHosts\com.ssshmuldownloader.host",
                        @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.ssshmuldownloader.host"
                    };

                    foreach (var subKey in browsers)
                    {
                        using var rKey = Registry.CurrentUser.CreateSubKey(subKey);
                        rKey?.SetValue("", jsonPath);
                    }
                }
            }
            catch { }
        }
    }
}
