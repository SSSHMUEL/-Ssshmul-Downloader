using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SsshmulDownloader.Models;
using SsshmulDownloader.Storage;

namespace SsshmulDownloader.Engine
{
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? FinalFilePath { get; set; }

        public DownloadResult(bool success, string? error = null, string? path = null)
        {
            Success = success;
            ErrorMessage = error;
            FinalFilePath = path;
        }
    }

    public static class YtDlpProcess
    {
        private static readonly Regex ProgressRegex = new(@"\[download\]\s+([0-9.]+)%\s+of\s+.*?\s+at\s+(.*?\/s)", RegexOptions.Compiled);
        private static readonly Regex PlaylistProgressRegex = new(@"\[download\] Downloading item (\d+) of (\d+)", RegexOptions.Compiled);
        private static readonly Regex DestinationRegex = new(@"\[(?:download|ExtractAudio|Merger|ffmpeg)\] Destination:\s*(.*)", RegexOptions.Compiled);

        public static List<string> BuildArguments(
            string url,
            string? directUrl,
            string? customTitle,
            bool isPlaylist,
            string? formatId,
            string tempOutputDir,
            string? cookiesFile,
            SubsOptions? subs,
            TagMappings? tagMappings)
        {
            var args = new List<string>
            {
                "--verbose",
                "--encoding", "utf-8",
                "--no-check-certificates",
                "--prefer-insecure",
                "--progress",
                "--socket-timeout", "20",
                "--retries", "3",
                "--fragment-retries", "3",
                "--extractor-args", "youtube:player_client=android,ios,web"
            };

            if (!string.IsNullOrEmpty(cookiesFile) && File.Exists(cookiesFile))
            {
                args.Add("--cookies");
                args.Add(cookiesFile);
            }

            string ffmpegPath = BinaryResolver.FFmpegPath;
            if (File.Exists(ffmpegPath))
            {
                args.Add("--ffmpeg-location");
                args.Add(ffmpegPath);
            }

            string qjsPath = BinaryResolver.QjsPath;
            if (File.Exists(qjsPath))
            {
                args.Add("--js-runtimes");
                args.Add($"quickjs:{qjsPath}");
            }

            args.Add("--print");
            args.Add("before_dl:MPS_METADATA:%(title)s|%(thumbnail)s");

            string outputTemplate = "%(title)s.%(ext)s";
            if (!string.IsNullOrWhiteSpace(customTitle))
            {
                string safeTitle = Regex.Replace(customTitle, @"[\\/:*?""<>|]", "_");
                outputTemplate = safeTitle + ".%(ext)s";
            }

            args.Add("-P");
            args.Add(tempOutputDir);

            args.Add("--output");
            args.Add(isPlaylist ? "%(playlist)s/%(playlist_index)s - " + outputTemplate : outputTemplate);
            args.Add(isPlaylist ? "--yes-playlist" : "--no-playlist");

            // Subtitle extraction flags - only for video formats!
            bool isAudio = formatId == "mp3_high" || formatId == "mp3_medium" || formatId == "raw_audio" || formatId == "generic_audio";
            if (subs != null && !string.IsNullOrWhiteSpace(subs.Lang) && !isAudio)
            {
                string subLang = subs.Lang.Trim();
                if (subLang == "he,en") subLang = "he.*,iw.*,en.*";
                else if (subLang == "he") subLang = "he.*,iw.*";
                else if (subLang == "en") subLang = "en.*";

                args.Add("--write-subs");
                args.Add("--write-auto-subs");
                args.Add("--sub-langs");
                args.Add(subLang);
                args.Add("--convert-subs");
                args.Add("srt");
                args.Add("--sub-format");
                args.Add("srt/vtt/best");
                args.Add("--ignore-no-formats-error");
                args.Add("--no-abort-on-error");
                args.Add("--compat-options");
                args.Add("no-abort-on-error");

                if (subs.Type == "embed")
                {
                    args.Add("--embed-subs");
                }
            }

            // Format selection
            string fmt = formatId ?? "mp3_high";
            if (fmt.StartsWith("fallback_"))
            {
                string height = fmt.Replace("fallback_", "").Replace("p", "");
                args.Add("-f");
                args.Add("best/bestvideo+bestaudio");
                args.Add("--recode-video");
                args.Add("mp4");
                args.Add("--postprocessor-args");
                args.Add($"VideoConvertor:-vf scale=-2:{height} -preset ultrafast");
            }
            else
            {
                switch (fmt)
                {
                    case "mp3_high":
                        args.Add("-f"); args.Add("bestaudio/best");
                        args.Add("--extract-audio");
                        args.Add("--audio-format"); args.Add("mp3");
                        args.Add("--audio-quality"); args.Add("0");
                        args.Add("--embed-thumbnail");
                        args.Add("--embed-metadata");
                        args.Add("--add-metadata");
                        break;
                    case "mp3_medium":
                        args.Add("-f"); args.Add("bestaudio/best");
                        args.Add("--extract-audio");
                        args.Add("--audio-format"); args.Add("mp3");
                        args.Add("--audio-quality"); args.Add("5");
                        args.Add("--embed-thumbnail");
                        args.Add("--embed-metadata");
                        args.Add("--add-metadata");
                        break;
                    case "raw_audio":
                        args.Add("-f"); args.Add("bestaudio/best");
                        args.Add("--embed-metadata");
                        args.Add("--add-metadata");
                        break;
                    case "generic_audio":
                        args.Add("-f"); args.Add("bestaudio/best");
                        args.Add("--extract-audio");
                        args.Add("--audio-format"); args.Add("mp3");
                        args.Add("--audio-quality"); args.Add("0");
                        args.Add("--embed-metadata");
                        args.Add("--add-metadata");
                        break;
                    case "generic_video":
                        args.Add("-f"); args.Add("bestvideo+bestaudio/best");
                        args.Add("--merge-output-format"); args.Add("mp4");
                        break;
                    default:
                        args.Add("-f"); args.Add(fmt);
                        args.Add("--embed-thumbnail");
                        args.Add("--embed-metadata");
                        args.Add("--add-metadata");
                        break;
                }
            }

            // Tag mappings via --parse-metadata
            if (tagMappings != null)
            {
                if (tagMappings.Artist == "channel") { args.Add("--parse-metadata"); args.Add("%(channel)s:%(meta_artist)s"); }
                else if (tagMappings.Artist == "none") { args.Add("--parse-metadata"); args.Add(":%(meta_artist)s"); }

                if (tagMappings.Album == "channel") { args.Add("--parse-metadata"); args.Add("%(channel)s:%(meta_album)s"); }
                else if (tagMappings.Album == "title") { args.Add("--parse-metadata"); args.Add("%(title)s:%(meta_album)s"); }
                else if (tagMappings.Album == "none") { args.Add("--parse-metadata"); args.Add(":%(meta_album)s"); }

                if (tagMappings.Year == "upload_year") { args.Add("--parse-metadata"); args.Add("%(upload_date>%Y)s:%(meta_date)s"); }
                else if (tagMappings.Year == "none") { args.Add("--parse-metadata"); args.Add(":%(meta_date)s"); }

                if (tagMappings.Comment == "description") { args.Add("--parse-metadata"); args.Add("%(description)s:%(meta_comment)s"); }
                else if (tagMappings.Comment == "url") { args.Add("--parse-metadata"); args.Add("%(webpage_url)s:%(meta_comment)s"); }
                else if (tagMappings.Comment == "none") { args.Add("--parse-metadata"); args.Add(":%(meta_comment)s"); }
            }

            args.Add("--postprocessor-args"); args.Add("VideoConvertor:-preset ultrafast");
            args.Add("--postprocessor-args"); args.Add("VideoRemuxer:-preset ultrafast");

            // Target URL
            args.Add(!string.IsNullOrWhiteSpace(directUrl) ? directUrl : url);

            return args;
        }

        public static async Task<DownloadResult> ExecuteDownloadAsync(
            DownloadContext ctx,
            Action<DownloadMessage> onMessage,
            CancellationToken ct)
        {
            string ytdlpPath = BinaryResolver.YtDlpPath;
            if (!File.Exists(ytdlpPath))
            {
                return new DownloadResult(false, "yt-dlp.exe not found");
            }

            string tempDir = ctx.TempDir ?? Path.Combine(Path.GetTempPath(), $"Ssshmul_DL_{ctx.DownloadId}");
            ctx.TempDir = tempDir;
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string? cookiesFile = null;
            if (!string.IsNullOrWhiteSpace(ctx.Cookies))
            {
                try
                {
                    string cleanedCookies = ctx.Cookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                    if (!cleanedCookies.EndsWith("\n"))
                    {
                        cleanedCookies += "\n";
                    }
                    cookiesFile = Path.Combine(tempDir, "cookies.txt");
                    var utf8WithoutBom = new UTF8Encoding(false);
                    await File.WriteAllTextAsync(cookiesFile, cleanedCookies, utf8WithoutBom, ct);
                }
                catch { }
            }

            var args = BuildArguments(
                ctx.Url,
                ctx.DirectUrl,
                ctx.CustomTitle,
                false,
                ctx.FormatId,
                tempDir,
                cookiesFile,
                ctx.Subs,
                ctx.TagMappings);

            var psi = new ProcessStartInfo
            {
                FileName = ytdlpPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            string binDir = Path.GetDirectoryName(ytdlpPath) ?? "";
            string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = binDir + ";" + existingPath;

            try
            {
                string logFile = PathUtils.GetLogFilePath();
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [yt-dlp] Starting download for: {ctx.Url} (Format: {ctx.FormatId})\nCommand: \"{ytdlpPath}\" {string.Join(" ", args)}\n");
            }
            catch { }

            var process = new Process { StartInfo = psi };
            ctx.Process = process;

            var errorOutput = new StringBuilder();
            string? finalFileName = null;
            bool isNetfreeBlocked = false;

            onMessage(DownloadMessage.Starting(ctx.DownloadId));

            try
            {
                process.Start();

                var outTask = Task.Run(async () =>
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        string? line = await process.StandardOutput.ReadLineAsync(ct);
                        if (line == null) break;

                        if (line.StartsWith("MPS_METADATA:"))
                        {
                            try
                            {
                                string data = line.Substring("MPS_METADATA:".Length);
                                int split = data.LastIndexOf('|');
                                if (split != -1)
                                {
                                    string title = data.Substring(0, split).Trim();
                                    string thumb = data.Substring(split + 1).Trim();
                                    ctx.Title = title;
                                    ctx.Thumbnail = thumb;
                                    onMessage(DownloadMessage.Metadata(title, thumb, ctx.DownloadId));
                                }
                            }
                            catch { }
                            continue;
                        }

                        var destMatch = DestinationRegex.Match(line);
                        if (destMatch.Success)
                        {
                            finalFileName = Path.GetFileName(destMatch.Groups[1].Value.Trim());
                        }

                        var progMatch = ProgressRegex.Match(line);
                        if (progMatch.Success)
                        {
                            string percent = progMatch.Groups[1].Value;
                            string speed = progMatch.Groups[2].Value.Trim();
                            ctx.LastPercent = percent;
                            ctx.LastSpeed = speed;
                            ctx.CurrentState = "downloading";
                            onMessage(DownloadMessage.Progress(percent, speed, ctx.DownloadId));
                        }

                        if (line.Contains("[Merger] Merging formats"))
                        {
                            ctx.CurrentState = "merging";
                            onMessage(DownloadMessage.Merging(ctx.DownloadId));
                        }
                        else if (line.StartsWith("[ExtractAudio]") || line.StartsWith("[ffmpeg]") || line.StartsWith("[Metadata]") || line.StartsWith("[ThumbnailsConvertor]"))
                        {
                            ctx.CurrentState = "processing";
                            onMessage(DownloadMessage.Processing(ctx.DownloadId));
                        }
                    }
                }, ct);

                var errTask = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        string? line = await process.StandardError.ReadLineAsync(ct);
                        if (line == null) break;

                        if (line.Contains("HTTP Error 418: Blocked by NetFree"))
                        {
                            isNetfreeBlocked = true;
                            onMessage(DownloadMessage.NetfreeBlocked(ctx.DownloadId));
                        }

                        if (line.Contains("ERROR:") || line.Contains("WARNING:"))
                        {
                            errorOutput.AppendLine(line);
                        }
                    }
                }, ct);

                await Task.WhenAll(outTask, errTask);
                await process.WaitForExitAsync(ct);

                if (ctx.IsCancelled)
                {
                    return new DownloadResult(false, "Cancelled");
                }

                if (process.ExitCode == 0)
                {
                    // Move downloaded files to target directory
                    string targetFolder = !string.IsNullOrWhiteSpace(ctx.DestinationPath)
                        ? ctx.DestinationPath
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

                    if (!string.IsNullOrWhiteSpace(ctx.PlaylistTitle))
                    {
                        string safePlaylist = Regex.Replace(ctx.PlaylistTitle, @"[\\/:*?""<>|]", "_");
                        targetFolder = Path.Combine(targetFolder, "Playlist - " + safePlaylist);
                    }

                    string finalPath = MoveFilesSafely(tempDir, targetFolder);
                    try
                    {
                        string logFile = PathUtils.GetLogFilePath();
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [yt-dlp] SUCCESS: Saved to: {finalPath}\n");
                    }
                    catch { }
                    return new DownloadResult(true, null, finalPath);
                }
                else
                {
                    string fullErr = errorOutput.ToString().Trim();
                    string err = fullErr;
                    
                    if (isNetfreeBlocked)
                    {
                        err = "נחסם על ידי נטפרי (HTTP 418)";
                    }
                    else if (fullErr.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) ||
                             fullErr.Contains("confirm your age", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "יוטיוב דורש אימות משתמש (הגנת בוטים/גיל). חבר קובץ עוגיות/התחבר לדפדפן";
                    }
                    else
                    {
                        // Extract lines with ERROR: first if available, so harmless WARNINGs don't hide the real cause
                        var errLines = fullErr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var actualErrors = errLines.Where(l => l.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (actualErrors.Count > 0)
                        {
                            err = string.Join("\n", actualErrors);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(err)) err = $"תהליך ההורדה נכשל עם קוד {process.ExitCode}";

                    try
                    {
                        string logFile = PathUtils.GetLogFilePath();
                        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [yt-dlp] ERROR: Download failed (Exit code {process.ExitCode}) for URL: {ctx.Url}\nArguments: {string.Join(" ", args)}\nError details:\n{fullErr}\n----------------------------------------\n";
                        File.AppendAllText(logFile, logEntry);
                    }
                    catch { }

                    return new DownloadResult(false, err);
                }
            }
            catch (OperationCanceledException)
            {
                return new DownloadResult(false, "Cancelled");
            }
            catch (Exception ex)
            {
                try
                {
                    string logFile = PathUtils.GetLogFilePath();
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [yt-dlp] EXCEPTION: {ex}\n");
                }
                catch { }
                return new DownloadResult(false, ex.Message);
            }
            finally
            {
                ProcessTreeHelper.KillProcessTree(process);
                ctx.Process = null;
            }
        }

        public static string MoveFilesSafely(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string primaryFile = targetDir;
            if (Directory.Exists(sourceDir))
            {
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals("cookies.txt", StringComparison.OrdinalIgnoreCase)) continue;

                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    string targetPath = Path.Combine(targetDir, fileName);

                    int counter = 1;
                    while (File.Exists(targetPath))
                    {
                        targetPath = Path.Combine(targetDir, $"{nameWithoutExt} ({counter}){ext}");
                        counter++;
                    }

                    try
                    {
                        File.Move(file, targetPath, overwrite: true);
                        if (!targetPath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) &&
                            !targetPath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                        {
                            primaryFile = targetPath;
                        }
                    }
                    catch { }
                }

                try
                {
                    Directory.Delete(sourceDir, recursive: true);
                }
                catch { }
            }

            return primaryFile;
        }

        public static async Task<List<SearchResultItem>> SearchAsync(string query, int count, string? cookies = null, CancellationToken ct = default)
        {
            var results = new List<SearchResultItem>();
            string ytdlpPath = BinaryResolver.YtDlpPath;
            if (string.IsNullOrWhiteSpace(query)) return results;

            int fetchCount = count <= 0 ? 500 : Math.Min(count, 1000);
            string logFile = PathUtils.GetLogFilePath();

            // STEP 1: If query might be an artist, resolve their official YouTube channel and pull full catalog
            try
            {
                var artist = await ArtistTracker.ArtistScanner.ResolveArtistInfoAsync(query);
                if (artist != null && !string.IsNullOrWhiteSpace(artist.ChannelUrl) && (artist.ChannelUrl.StartsWith("http://") || artist.ChannelUrl.StartsWith("https://")))
                {
                    string targetUrl = artist.ChannelUrl;
                    if (!targetUrl.EndsWith("/videos")) targetUrl += "/videos";

                    var psiChannel = new ProcessStartInfo
                    {
                        FileName = ytdlpPath,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    psiChannel.ArgumentList.Add("--encoding");
                    psiChannel.ArgumentList.Add("utf-8");
                    psiChannel.ArgumentList.Add("--flat-playlist");
                    psiChannel.ArgumentList.Add("--dump-single-json");
                    psiChannel.ArgumentList.Add("--no-check-certificates");
                    psiChannel.ArgumentList.Add("--prefer-insecure");
                    psiChannel.ArgumentList.Add("--socket-timeout");
                    psiChannel.ArgumentList.Add("15");
                    psiChannel.ArgumentList.Add("--playlist-items");
                    psiChannel.ArgumentList.Add($"1-{fetchCount}");
                    psiChannel.ArgumentList.Add(targetUrl);

                    string binDir = Path.GetDirectoryName(ytdlpPath) ?? "";
                    string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    psiChannel.Environment["PATH"] = binDir + ";" + existingPath;

                    using var pChan = new Process { StartInfo = psiChannel };
                    pChan.Start();

                    var outTask = pChan.StandardOutput.ReadToEndAsync();
                    await pChan.WaitForExitAsync(ct);
                    string cOutput = await outTask;

                    if (!string.IsNullOrWhiteSpace(cOutput))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(cOutput);
                        var root = doc.RootElement;
                        var entries = root.TryGetProperty("entries", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Array ? e : root;

                        if (entries.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var entry in entries.EnumerateArray())
                            {
                                string id = entry.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "";
                                if (string.IsNullOrEmpty(id)) continue;

                                string title = entry.TryGetProperty("title", out var tProp) ? (tProp.GetString() ?? "ללא כותרת") : "ללא כותרת";
                                string channel = artist.Name;
                                string thumbnail = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";

                                string durationStr = "";
                                if (entry.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out var durSec) && durSec > 0)
                                {
                                    int total = (int)durSec;
                                    int m = total / 60;
                                    int s = total % 60;
                                    durationStr = $"{m}:{s:D2}";
                                }

                                results.Add(new SearchResultItem
                                {
                                    Id = id,
                                    Url = $"https://www.youtube.com/watch?v={id}",
                                    Title = title,
                                    Channel = channel,
                                    Thumbnail = thumbnail,
                                    Duration = durationStr
                                });

                                if (results.Count >= fetchCount) break;
                            }
                        }
                    }

                    if (results.Count > 0)
                    {
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Search] Resolved via channel '{artist.Name}': {results.Count} songs\n");
                        return results;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Channel resolution search error: {ex.Message}");
            }

            // STEP 2: Direct ytsearch attempt
            string searchTarget = $"ytsearch{fetchCount}:{query}";
            var psi = new ProcessStartInfo
            {
                FileName = ytdlpPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("--encoding");
            psi.ArgumentList.Add("utf-8");
            psi.ArgumentList.Add("--flat-playlist");
            psi.ArgumentList.Add("--dump-single-json");
            psi.ArgumentList.Add("--no-check-certificates");
            psi.ArgumentList.Add("--prefer-insecure");
            psi.ArgumentList.Add("--socket-timeout");
            psi.ArgumentList.Add("15");

            string? tempCookies = null;
            if (!string.IsNullOrWhiteSpace(cookies))
            {
                try
                {
                    string cleanedCookies = cookies.Trim('\uFEFF', '\u200B', ' ', '\r', '\n');
                    if (!cleanedCookies.EndsWith("\n")) cleanedCookies += "\n";
                    tempCookies = Path.Combine(Path.GetTempPath(), $"sshmul_search_cookies_{Guid.NewGuid():N}.txt");
                    await File.WriteAllTextAsync(tempCookies, cleanedCookies, new UTF8Encoding(false), ct);
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(tempCookies);
                }
                catch { }
            }

            psi.ArgumentList.Add(searchTarget);

            string ytdlpDir = Path.GetDirectoryName(ytdlpPath) ?? "";
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = ytdlpDir + ";" + currentPath;

            try
            {
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Search] Query: '{query}' -> Target: '{searchTarget}'\n");

                using var process = new Process { StartInfo = psi };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using (ct.Register(() => { try { ProcessTreeHelper.KillProcessTree(process); } catch { } }))
                {
                    await process.WaitForExitAsync(ct);
                }

                string output = await outputTask;
                string error = await errorTask;

                if (!string.IsNullOrWhiteSpace(output))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(output);
                    var root = doc.RootElement;
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var entries = root.TryGetProperty("entries", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Array ? e : root;

                        if (entries.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var entry in entries.EnumerateArray())
                            {
                                if (entry.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                                string id = entry.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "";
                                if (string.IsNullOrEmpty(id)) continue;

                                string title = entry.TryGetProperty("title", out var tProp) ? (tProp.GetString() ?? "ללא כותרת") : "ללא כותרת";
                                string channel = entry.TryGetProperty("channel", out var chProp) ? (chProp.GetString() ?? "") : "";
                                if (string.IsNullOrEmpty(channel) && entry.TryGetProperty("uploader", out var upProp))
                                {
                                    channel = upProp.GetString() ?? "";
                                }
                                if (string.IsNullOrEmpty(channel)) channel = "YouTube";

                                string thumbnail = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";

                                string durationStr = "";
                                if (entry.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out var durSec) && durSec > 0)
                                {
                                    int total = (int)durSec;
                                    int m = total / 60;
                                    int s = total % 60;
                                    durationStr = $"{m}:{s:D2}";
                                }

                                string url = entry.TryGetProperty("url", out var urlProp) ? (urlProp.GetString() ?? "") : "";
                                if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
                                {
                                    url = $"https://www.youtube.com/watch?v={id}";
                                }

                                results.Add(new SearchResultItem
                                {
                                    Id = id,
                                    Url = url,
                                    Title = title,
                                    Channel = channel,
                                    Thumbnail = thumbnail,
                                    Duration = durationStr
                                });

                                if (results.Count >= fetchCount) break;
                            }
                        }
                    }
                }

                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Search] Finished with {results.Count} results\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchAsync error: {ex.Message}");
                try
                {
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Search] EXCEPTION: {ex}\n");
                }
                catch { }
            }
            finally
            {
                if (tempCookies != null && File.Exists(tempCookies))
                {
                    try { File.Delete(tempCookies); } catch { }
                }
            }

            return results;
        }
    }
}
