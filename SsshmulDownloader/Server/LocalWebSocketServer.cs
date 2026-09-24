using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SsshmulDownloader.ArtistTracker;
using SsshmulDownloader.Engine;
using SsshmulDownloader.Models;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.Server
{
    public class LocalWebSocketServer
    {
        public static event Action<string>? ThemeChanged;
        private static readonly ConcurrentDictionary<string, WebSocket> _clients = new();
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        static LocalWebSocketServer()
        {
            DownloadManager.Instance.MessageBroadcast += OnDownloadMessageBroadcast;
        }

        private static void OnDownloadMessageBroadcast(DownloadMessage message)
        {
            _ = BroadcastAsync(message);
        }

        public static async Task HandleConnectionAsync(WebSocket ws)
        {
            string clientId = Guid.NewGuid().ToString("N");
            _clients[clientId] = ws;

            try
            {
                // Send initial restore_state to client
                var activeStates = DownloadManager.Instance.GetActiveStates();
                var restoreMsg = DownloadMessage.RestoreState(activeStates, activeStates.Count == 1);
                await SendDirectAsync(ws, restoreMsg);

                // Also send current artist list
                var artistsMsg = new DownloadMessage("artist_list")
                {
                    Artists = ArtistStore.GetAll()
                };
                await SendDirectAsync(ws, artistsMsg);

                var buffer = new byte[8192];
                var ms = new MemoryStream();

                while (ws.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string jsonText = Encoding.UTF8.GetString(ms.ToArray());
                    await ProcessMessageAsync(ws, jsonText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebSocket error: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                try { ws.Dispose(); } catch { }
            }
        }

        public static async Task BroadcastAsync(DownloadMessage msg)
        {
            string json = JsonSerializer.Serialize(msg, _jsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var kvp in _clients)
            {
                var ws = kvp.Value;
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        private static async Task SendDirectAsync(WebSocket ws, DownloadMessage msg)
        {
            if (ws.State != WebSocketState.Open) return;
            string json = JsonSerializer.Serialize(msg, _jsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }

        private static async Task ProcessMessageAsync(WebSocket ws, string jsonText)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) return;
                string type = typeProp.GetString() ?? "";

                switch (type)
                {
                    case "shutdown_server":
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(300);
                            Environment.Exit(0);
                        });
                        break;

                    case "select_destination":
                        _ = Task.Run(() =>
                        {
                            string? selectedPath = null;
                            var thread = new Thread(() =>
                            {
                                using var dlg = new FolderBrowserDialog();
                                dlg.Description = "בחר תיקיית שמירה להורדות";
                                dlg.UseDescriptionForTitle = true;
                                if (!string.IsNullOrWhiteSpace(SettingsStore.Current.CustomSavePath) && Directory.Exists(SettingsStore.Current.CustomSavePath))
                                {
                                    dlg.InitialDirectory = SettingsStore.Current.CustomSavePath;
                                    dlg.SelectedPath = SettingsStore.Current.CustomSavePath;
                                }
                                using var form = new Form { TopMost = true, TopLevel = true };
                                if (dlg.ShowDialog(form) == DialogResult.OK)
                                {
                                    selectedPath = dlg.SelectedPath;
                                }
                            });
                            thread.SetApartmentState(ApartmentState.STA);
                            thread.Start();
                            thread.Join();

                            if (!string.IsNullOrEmpty(selectedPath))
                            {
                                SettingsStore.Update(s => s.CustomSavePath = selectedPath);
                                _ = BroadcastAsync(DownloadMessage.DestinationSelected(selectedPath));
                            }
                        });
                        break;

                    case "select_artist_tracker_destination":
                        _ = Task.Run(() =>
                        {
                            string? selectedPath = null;
                            var thread = new Thread(() =>
                            {
                                using var dlg = new FolderBrowserDialog();
                                dlg.Description = "בחר תיקיית שמירה ראשית למעקב אמנים";
                                dlg.UseDescriptionForTitle = true;
                                if (!string.IsNullOrWhiteSpace(SettingsStore.Current.ArtistTrackerSavePath) && Directory.Exists(SettingsStore.Current.ArtistTrackerSavePath))
                                {
                                    dlg.InitialDirectory = SettingsStore.Current.ArtistTrackerSavePath;
                                    dlg.SelectedPath = SettingsStore.Current.ArtistTrackerSavePath;
                                }
                                using var form = new Form { TopMost = true, TopLevel = true };
                                if (dlg.ShowDialog(form) == DialogResult.OK)
                                {
                                    selectedPath = dlg.SelectedPath;
                                }
                            });
                            thread.SetApartmentState(ApartmentState.STA);
                            thread.Start();
                            thread.Join();

                            if (!string.IsNullOrEmpty(selectedPath))
                            {
                                SettingsStore.Update(s => s.ArtistTrackerSavePath = selectedPath);
                                _ = BroadcastAsync(new DownloadMessage("artist_tracker_destination_selected") { Path = selectedPath });
                            }
                        });
                        break;

                    case "set_artist_tracker_path":
                        if (root.TryGetProperty("path", out var atpProp))
                        {
                            string? p = atpProp.GetString();
                            SettingsStore.Update(s => s.ArtistTrackerSavePath = string.IsNullOrWhiteSpace(p) ? null : p);
                        }
                        break;

                    case "set_save_path":
                        if (root.TryGetProperty("path", out var spProp))
                        {
                            string? p = spProp.GetString();
                            SettingsStore.Update(s => s.CustomSavePath = string.IsNullOrWhiteSpace(p) ? null : p);
                        }
                        break;

                    case "open_artist_folder":
                        if (root.TryGetProperty("artistName", out var nameProp))
                        {
                            string? aName = nameProp.GetString();
                            string folderPath = !string.IsNullOrWhiteSpace(aName) 
                                ? PathUtils.GetArtistDirectory(aName) 
                                : PathUtils.GetDefaultArtistTrackerDirectory();

                            if (!Directory.Exists(folderPath))
                            {
                                try { Directory.CreateDirectory(folderPath); } catch { }
                            }
                            if (Directory.Exists(folderPath))
                            {
                                Process.Start("explorer.exe", folderPath);
                            }
                        }
                        else
                        {
                            string defaultTrackerDir = !string.IsNullOrWhiteSpace(SettingsStore.Current.ArtistTrackerSavePath)
                                ? SettingsStore.Current.ArtistTrackerSavePath
                                : PathUtils.GetDefaultArtistTrackerDirectory();
                            if (!Directory.Exists(defaultTrackerDir))
                            {
                                try { Directory.CreateDirectory(defaultTrackerDir); } catch { }
                            }
                            if (Directory.Exists(defaultTrackerDir))
                            {
                                Process.Start("explorer.exe", defaultTrackerDir);
                            }
                        }
                        break;

                    case "check_for_updates":
                        _ = Task.Run(async () =>
                        {
                            var info = await UpdateManager.CheckForUpdatesAsync();
                            var msg = new DownloadMessage("update_status")
                            {
                                UpdateInfo = info,
                                CurrentVersion = UpdateManager.GetCurrentVersion()
                            };
                            await BroadcastAsync(msg);
                        });
                        break;

                    case "get_app_version":
                        _ = Task.Run(async () =>
                        {
                            var msg = new DownloadMessage("app_version")
                            {
                                CurrentVersion = UpdateManager.GetCurrentVersion()
                            };
                            await BroadcastAsync(msg);
                        });
                        break;

                    case "install_update":
                        if (root.TryGetProperty("downloadUrl", out var dlUrlProp))
                        {
                            string? dlUrl = dlUrlProp.GetString();
                            if (!string.IsNullOrWhiteSpace(dlUrl))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await UpdateManager.DownloadAndLaunchInstallerAsync(dlUrl, percent =>
                                        {
                                            _ = BroadcastAsync(new DownloadMessage("update_download_progress")
                                            {
                                                UpdateProgress = percent
                                            });
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        await BroadcastAsync(DownloadMessage.CreateError($"שגיאה בהורדת העדכון: {ex.Message}"));
                                    }
                                });
                            }
                        }
                        break;

                    case "open_folder":
                        if (root.TryGetProperty("path", out var pathProp))
                        {
                            string? folderPath = pathProp.GetString();
                            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                            {
                                Process.Start("explorer.exe", folderPath);
                            }
                        }
                        break;

                    case "open_extension_folder":
                        _ = Task.Run(() =>
                        {
                            string extDir = PathUtils.GetExtensionDirectory();
                            if (Directory.Exists(extDir))
                            {
                                // Open parent folder with 'extension' folder selected/highlighted
                                // so the user can easily drag the whole 'extension' folder directly into Chrome!
                                Process.Start("explorer.exe", $"/select,\"{extDir}\"");
                            }
                        });
                        break;

                    case "open_browser_extensions":
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                // 1. Try launching Google Chrome executable with extension url
                                string[] chromePaths = new[]
                                {
                                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                                };

                                foreach (var cp in chromePaths)
                                {
                                    if (File.Exists(cp))
                                    {
                                        Process.Start(new ProcessStartInfo(cp, "chrome://extensions") { UseShellExecute = true });
                                        return;
                                    }
                                }

                                // 2. Try Edge executable
                                string[] edgePaths = new[]
                                {
                                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
                                };

                                foreach (var ep in edgePaths)
                                {
                                    if (File.Exists(ep))
                                    {
                                        Process.Start(new ProcessStartInfo(ep, "edge://extensions") { UseShellExecute = true });
                                        return;
                                    }
                                }

                                // 3. Try Brave executable
                                string bravePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\Application\brave.exe");
                                if (File.Exists(bravePath))
                                {
                                    Process.Start(new ProcessStartInfo(bravePath, "brave://extensions") { UseShellExecute = true });
                                    return;
                                }

                                // 4. Fallback command
                                Process.Start(new ProcessStartInfo("cmd.exe", "/c start chrome chrome://extensions || start msedge edge://extensions") { CreateNoWindow = true, UseShellExecute = false });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error launching browser extensions page: {ex.Message}");
                            }
                        });
                        break;

                    case "extension_ping":
                    case "extension_connected":
                        // Broadcast extension installed & active status
                        _ = BroadcastAsync(new DownloadMessage("extension_status")
                        {
                            Message = "active"
                        });
                        break;

                    case "open_log":
                        string logPath = PathUtils.GetLogFilePath();
                        if (File.Exists(logPath))
                        {
                            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                        }
                        break;

                    case "cancel_download":
                        // Legacy single cancel
                        break;

                    case "cancel_download_advanced":
                        if (root.TryGetProperty("downloadId", out var cancelIdProp))
                        {
                            string? id = cancelIdProp.GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                DownloadManager.Instance.CancelAdvancedDownload(id);
                            }
                        }
                        break;

                    case "cancel_all_downloads_advanced":
                        DownloadManager.Instance.CancelAllAdvancedDownloads();
                        break;

                    case "pause_download_advanced":
                        if (root.TryGetProperty("downloadId", out var pauseIdProp))
                        {
                            string? id = pauseIdProp.GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                DownloadManager.Instance.PauseAdvancedDownload(id);
                            }
                        }
                        break;

                    case "resume_download_advanced":
                        if (root.TryGetProperty("downloadId", out var resumeIdProp))
                        {
                            string? id = resumeIdProp.GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                DownloadManager.Instance.ResumeAdvancedDownload(id);
                            }
                        }
                        break;

                    case "pause_all_downloads_advanced":
                        DownloadManager.Instance.PauseAllAdvancedDownloads();
                        break;

                    case "resume_all_downloads_advanced":
                        DownloadManager.Instance.ResumeAllAdvancedDownloads();
                        break;

                    case "download":
                    case "download_video":
                    case "download_advanced":
                    case "download_video_advanced":
                        ParseAndStartDownload(root, type);
                        break;

                    case "download_queue":
                        if (root.TryGetProperty("urls", out var urlsProp) && urlsProp.ValueKind == JsonValueKind.Array)
                        {
                            var urls = new List<string>();
                            foreach (var u in urlsProp.EnumerateArray())
                            {
                                string? urlStr = u.GetString();
                                if (!string.IsNullOrEmpty(urlStr)) urls.Add(urlStr);
                            }

                            string? fmtId = root.TryGetProperty("formatId", out var fProp) ? fProp.GetString() : null;
                            string? dest = root.TryGetProperty("destinationPath", out var dProp) ? dProp.GetString() : null;
                            bool isNetfree = root.TryGetProperty("isNetfreeUser", out var nfProp) && nfProp.GetBoolean();
                            string? pTitle = root.TryGetProperty("playlistTitle", out var ptProp) ? ptProp.GetString() : null;
                            string? lang = root.TryGetProperty("language", out var lProp) ? lProp.GetString() : "he";

                            _ = DownloadManager.Instance.StartQueueDownloadAsync(urls, fmtId, dest, isNetfree, pTitle, lang);
                        }
                        break;

                    // ARTIST TRACKING MESSAGES
                    case "get_artists":
                        var listMsg = new DownloadMessage("artist_list")
                        {
                            Artists = ArtistStore.GetAll()
                        };
                        await SendDirectAsync(ws, listMsg);
                        break;

                    case "add_artist":
                        if (root.TryGetProperty("query", out var qProp))
                        {
                            string? query = qProp.GetString();
                            string? artistCookies = root.TryGetProperty("cookies", out var acProp) ? acProp.GetString() : null;
                            string? preferredFmt = root.TryGetProperty("preferredFormat", out var pfProp) ? pfProp.GetString() : null;

                            if (!string.IsNullOrWhiteSpace(query))
                            {
                                _ = Task.Run(async () =>
                                {
                                    string logFile = PathUtils.GetLogFilePath();
                                    try
                                    {
                                        await BroadcastAsync(new DownloadMessage("scan_progress")
                                        {
                                            Message = $"מאתר ערוץ עבור '{query}'..."
                                        });

                                        var artist = await ArtistScanner.ResolveArtistInfoAsync(query, artistCookies);
                                        if (artist != null)
                                        {
                                            if (!string.IsNullOrWhiteSpace(preferredFmt))
                                            {
                                                artist.PreferredFormat = preferredFmt;
                                            }

                                            ArtistStore.Save(artist);
                                            await BroadcastAsync(new DownloadMessage("artist_updated")
                                            {
                                                Artist = artist,
                                                Message = $"האמן '{artist.Name}' נוסף בהצלחה למעקב!"
                                            });

                                            // Perform initial scan
                                            await BroadcastAsync(new DownloadMessage("scan_progress")
                                            {
                                                Message = $"סורק שירים עבור '{artist.Name}'..."
                                            });
                                            await ArtistScanner.ScanArtistAsync(artist, triggerDownload: artist.AutoDownload, artistCookies);
                                            await BroadcastAsync(new DownloadMessage("scan_progress")
                                            {
                                                Message = $"סריקת '{artist.Name}' הושלמה ({artist.RecentSongs.Count} שירים במעקב)."
                                            });
                                        }
                                        else
                                        {
                                            await BroadcastAsync(new DownloadMessage("error")
                                            {
                                                Error = $"לא נמצא ערוץ יוטיוב מתאים עבור '{query}'"
                                            });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        try { File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] add_artist error: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
                                        await BroadcastAsync(new DownloadMessage("error")
                                        {
                                            Error = $"שגיאה בהוספת האמן: {ex.Message}"
                                        });
                                    }
                                });
                            }
                        }
                        break;

                    case "scan_artist":
                        if (root.TryGetProperty("artistId", out var aIdProp))
                        {
                            string? aId = aIdProp.GetString();
                            if (!string.IsNullOrEmpty(aId))
                            {
                                var a = ArtistStore.GetById(aId);
                                if (a != null)
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        await ArtistScanner.ScanArtistAsync(a, triggerDownload: true);
                                    });
                                }
                            }
                        }
                        break;

                    case "scan_all_artists":
                        _ = Task.Run(async () =>
                        {
                            await ArtistScanner.ScanAllArtistsAsync(triggerDownload: true);
                        });
                        break;

                    case "delete_artist":
                        if (root.TryGetProperty("artistId", out var delIdProp))
                        {
                            string? aId = delIdProp.GetString();
                            if (!string.IsNullOrEmpty(aId))
                            {
                                var existing = ArtistStore.GetById(aId);
                                string artistName = existing?.Name ?? aId;
                                
                                // Immediately cancel all active downloads belonging to this artist
                                DownloadManager.Instance.CancelArtistDownloads(aId, existing?.KnownVideoIds);

                                ArtistStore.Delete(aId);
                                string logFile = PathUtils.GetLogFilePath();
                                try { File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Artist] Removed artist '{artistName}' (ID: {aId}) and cancelled active downloads\n"); } catch { }
                                await BroadcastAsync(new DownloadMessage("artist_deleted")
                                {
                                    DownloadId = aId,
                                    Message = $"האמן '{artistName}' הוסר מהמעקב וההורדות בוטלו",
                                    Artists = ArtistStore.GetAll()
                                });
                            }
                        }
                        break;

                    case "set_theme":
                        if (root.TryGetProperty("theme", out var themeProp))
                        {
                            string? themeVal = themeProp.GetString();
                            if (!string.IsNullOrEmpty(themeVal))
                            {
                                SettingsStore.Update(s => s.Theme = themeVal);
                                ThemeChanged?.Invoke(themeVal);
                            }
                        }
                        break;

                    case "sync_cookies":
                        if (root.TryGetProperty("cookies", out var syncCkProp))
                        {
                            string? syncCookies = syncCkProp.GetString();
                            if (!string.IsNullOrWhiteSpace(syncCookies))
                            {
                                string cleaned = syncCookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                                File.WriteAllText(PathUtils.GetSavedCookiesFilePath(), cleaned, new UTF8Encoding(false));
                                await BroadcastAsync(new DownloadMessage("cookies_updated")
                                {
                                    Message = "עוגיות יוטיוב עודכנו בהצלחה מהדפדפן!"
                                });
                            }
                        }
                        break;

                    case "clear_cookies":
                        PathUtils.ClearSavedCookies();
                        await BroadcastAsync(new DownloadMessage("cookies_cleared")
                        {
                            Message = "עוגיות יוטיוב נמחקו בהצלחה!"
                        });
                        break;

                    case "update_artist":
                        if (root.TryGetProperty("artist", out var artistElem))
                        {
                            var updatedArtist = JsonSerializer.Deserialize<TrackedArtist>(artistElem.GetRawText());
                            if (updatedArtist != null && !string.IsNullOrEmpty(updatedArtist.Id))
                            {
                                ArtistStore.Save(updatedArtist);
                                await BroadcastAsync(new DownloadMessage("artist_updated")
                                {
                                    Artist = updatedArtist
                                });
                            }
                        }
                        break;

                    case "search_youtube":
                        if (root.TryGetProperty("query", out var sqProp))
                        {
                            string query = sqProp.GetString() ?? "";
                            int count = root.TryGetProperty("count", out var cProp) && cProp.TryGetInt32(out var ci) ? ci : 20;
                            string? cookies = root.TryGetProperty("cookies", out var ckProp) ? ckProp.GetString() : null;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var items = await YtDlpProcess.SearchAsync(query, count, cookies);
                                    await SendDirectAsync(ws, new DownloadMessage("search_results")
                                    {
                                        Query = query,
                                        SearchResults = items
                                    });
                                }
                                catch (Exception sEx)
                                {
                                    Debug.WriteLine($"Search exception: {sEx.Message}");
                                    await SendDirectAsync(ws, new DownloadMessage("search_error")
                                    {
                                        Query = query,
                                        Error = sEx.Message
                                    });
                                }
                            });
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing message: {ex.Message}");
            }
        }

        private static void ParseAndStartDownload(JsonElement root, string type)
        {
            string url = root.TryGetProperty("url", out var uProp) ? (uProp.GetString() ?? "") : "";
            if (string.IsNullOrEmpty(url)) return;

            string downloadId = root.TryGetProperty("downloadId", out var idProp) && !string.IsNullOrEmpty(idProp.GetString())
                ? idProp.GetString()!
                : Guid.NewGuid().ToString("N");

            string? directUrl = root.TryGetProperty("directUrl", out var duProp) ? duProp.GetString() : null;
            string? customTitle = root.TryGetProperty("customTitle", out var ctProp) ? ctProp.GetString() : null;
            string? customThumbnail = root.TryGetProperty("customThumbnail", out var thProp) ? thProp.GetString() : null;
            string? formatId = root.TryGetProperty("formatId", out var fProp) ? fProp.GetString() : null;
            string? destinationPath = root.TryGetProperty("destinationPath", out var dpProp) ? dpProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(destinationPath) && root.TryGetProperty("customSavePath", out var cspProp))
            {
                destinationPath = cspProp.GetString();
            }
            bool isNetfree = root.TryGetProperty("isNetfreeUser", out var nfProp) && nfProp.GetBoolean();
            bool isVideo = type.Contains("video");
            string? playlistTitle = root.TryGetProperty("playlistTitle", out var ptProp) ? ptProp.GetString() : null;
            string? qualityText = root.TryGetProperty("qualityText", out var qProp) ? qProp.GetString() : null;
            string? cookies = root.TryGetProperty("cookies", out var cProp) ? cProp.GetString() : null;

            if (!string.IsNullOrWhiteSpace(cookies))
            {
                try
                {
                    string cleaned = cookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                    if (!cleaned.EndsWith("\n")) cleaned += "\n";
                    File.WriteAllText(PathUtils.GetSavedCookiesFilePath(), cleaned, new UTF8Encoding(false));
                }
                catch { }
            }

            SubsOptions? subs = null;
            if (root.TryGetProperty("downloadSubs", out var dsProp) && dsProp.GetBoolean())
            {
                string sLang = root.TryGetProperty("subsLang", out var slProp) ? (slProp.GetString() ?? "he") : "he";
                string sType = root.TryGetProperty("subsType", out var stProp) ? (stProp.GetString() ?? "separate") : "separate";
                subs = new SubsOptions(sLang, sType);
            }

            TagMappings? tagMappings = null;
            if (root.TryGetProperty("tagMappings", out var tmProp) && tmProp.ValueKind == JsonValueKind.Object)
            {
                tagMappings = JsonSerializer.Deserialize<TagMappings>(tmProp.GetRawText());
            }

            DownloadManager.Instance.StartAdvancedDownload(
                downloadId,
                url,
                directUrl,
                customTitle,
                customThumbnail,
                formatId,
                destinationPath,
                isNetfree,
                isVideo,
                playlistTitle,
                qualityText,
                cookies,
                subs,
                tagMappings
            );
        }
    }
}
