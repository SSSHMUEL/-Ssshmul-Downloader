using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.Server
{
    public class LocalHttpServer
    {
        public const int Port = 9595;
        private readonly HttpListener _listener = new();
        private CancellationTokenSource? _cts;

        public bool IsRunning => _listener.IsListening;

        public void Start()
        {
            if (_listener.IsListening) return;

            try
            {
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();

                _cts = new CancellationTokenSource();
                Task.Run(() => ListenLoopAsync(_cts.Token));
                Debug.WriteLine($"LocalHttpServer started on port {Port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting LocalHttpServer: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener.Stop();
            }
            catch { }
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(ctx), ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Listener exception: {ex.Message}");
                }
            }
        }

        private static bool IsOriginAllowed(string? origin)
        {
            if (string.IsNullOrEmpty(origin)) return true; // Direct desktop / WebView2 internal requests

            if (origin.Equals($"http://localhost:{Port}", StringComparison.OrdinalIgnoreCase) ||
                origin.Equals($"http://127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsAllowedImageDirectory(string fullPath)
        {
            var allowedDirectories = new List<string>
            {
                Path.GetFullPath(PathUtils.GetAppDataDirectory()),
                Path.GetFullPath(PathUtils.GetWebDirectory()),
                Path.GetFullPath(PathUtils.GetDefaultMusicDirectory()),
                Path.GetFullPath(PathUtils.GetActiveArtistTrackerDirectory()),
                Path.GetFullPath(Path.GetTempPath()),
                Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
            };

            if (!string.IsNullOrWhiteSpace(SettingsStore.Current.CustomSavePath) && Directory.Exists(SettingsStore.Current.CustomSavePath))
            {
                allowedDirectories.Add(Path.GetFullPath(SettingsStore.Current.CustomSavePath));
            }

            if (!string.IsNullOrWhiteSpace(SettingsStore.Current.ArtistTrackerSavePath) && Directory.Exists(SettingsStore.Current.ArtistTrackerSavePath))
            {
                allowedDirectories.Add(Path.GetFullPath(SettingsStore.Current.ArtistTrackerSavePath));
            }

            foreach (var dir in allowedDirectories)
            {
                string dirWithSlash = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(dirWithSlash, StringComparison.OrdinalIgnoreCase) || fullPath.Equals(dir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                string? origin = ctx.Request.Headers["Origin"];

                // Validate origin to protect against Cross-Site WebSocket Hijacking and cross-origin attacks
                if (!IsOriginAllowed(origin))
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    return;
                }

                // Set secure CORS headers only for allowed origins
                if (!string.IsNullOrEmpty(origin))
                {
                    ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
                    ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                    ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept, Authorization";
                    ctx.Response.Headers["Vary"] = "Origin";
                }

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url?.AbsolutePath ?? "/";

                // 1. WebSocket Upgrade on /ws
                if (ctx.Request.IsWebSocketRequest && path.Equals("/ws", StringComparison.OrdinalIgnoreCase))
                {
                    var wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                    await LocalWebSocketServer.HandleConnectionAsync(wsContext.WebSocket);
                    return;
                }

                // 2. Shutdown endpoint
                if (path.Equals("/shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] okBytes = Encoding.UTF8.GetBytes("OK");
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.OutputStream.WriteAsync(okBytes);
                    ctx.Response.Close();

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300);
                        Environment.Exit(0);
                    });
                    return;
                }

                // 3. Local image streaming
                if (path.Equals("/local-image", StringComparison.OrdinalIgnoreCase))
                {
                    string? imagePath = ctx.Request.QueryString["path"];
                    if (!string.IsNullOrWhiteSpace(imagePath))
                    {
                        try
                        {
                            string fullImagePath = Path.GetFullPath(imagePath);
                            string ext = Path.GetExtension(fullImagePath).ToLowerInvariant();
                            bool isImageExt = ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".ico" or ".bmp" or ".svg";

                            if (isImageExt && IsAllowedImageDirectory(fullImagePath) && File.Exists(fullImagePath))
                            {
                                ctx.Response.ContentType = GetMimeType(fullImagePath);
                                ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                                using var fs = File.OpenRead(fullImagePath);
                                await fs.CopyToAsync(ctx.Response.OutputStream);
                                ctx.Response.Close();
                                return;
                            }
                        }
                        catch { }
                    }

                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                // 4. Static web assets with directory traversal protection
                string webDir = Path.GetFullPath(PathUtils.GetWebDirectory());
                string webDirWithSlash = webDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string relativePath = path.TrimStart('/');
                if (string.IsNullOrEmpty(relativePath) || relativePath.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = "index.html";
                }

                string localFile = Path.GetFullPath(Path.Combine(webDir, relativePath));
                if (localFile.StartsWith(webDirWithSlash, StringComparison.OrdinalIgnoreCase) || localFile.Equals(webDir, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(localFile))
                    {
                        ctx.Response.ContentType = GetMimeType(localFile);
                        ctx.Response.StatusCode = 200;
                        using var fs = File.OpenRead(localFile);
                        await fs.CopyToAsync(ctx.Response.OutputStream);
                        ctx.Response.Close();
                        return;
                    }
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling HTTP request: {ex.Message}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        private static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }
    }
}
