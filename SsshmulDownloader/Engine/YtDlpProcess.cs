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
            TagMappings? tagMappings,
            bool isNetfree = false)
        {
            var args = new List<string>
            {
                "--verbose",
                "--encoding", "utf-8",
                "--force-ipv4",
                "--progress",
                "--socket-timeout", "15",
                "--retries", "3",
                "--fragment-retries", "3",
                "--concurrent-fragments", "5",
                "--convert-thumbnails", "jpg",
                "--extractor-args", "youtube:player_client=ios,android,web_creator"
            };

            if (isNetfree)
            {
                args.Add("--no-check-certificates");
                args.Add("--prefer-insecure");
            }

            if (!string.IsNullOrEmpty(cookiesFile) && File.Exists(cookiesFile))
            {
                args.Add("--cookies");
                args.Add(cookiesFile);
            }
            else
            {
                string savedCookies = PathUtils.GetSavedCookiesFilePath();
                if (File.Exists(savedCookies))
                {
                    args.Add("--cookies");
                    args.Add(savedCookies);
                }
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

            bool isAudio = formatId == null || formatId.StartsWith("mp3") || formatId == "raw_audio" || formatId == "generic_audio";

            string outputTemplate = "%(title)s.%(ext)s";
            if (!string.IsNullOrWhiteSpace(customTitle))
            {
                string effectiveTitle = isAudio
                    ? SsshmulDownloader.ArtistTracker.DeduplicationEngine.CleanSongTitle(customTitle)
                    : customTitle;
                string safeTitle = Regex.Replace(effectiveTitle, @"[\\/:*?""<>|]", "_");
                outputTemplate = safeTitle + ".%(ext)s";
            }

            args.Add("-P");
            args.Add(tempOutputDir);

            args.Add("--output");
            args.Add(isPlaylist ? "%(playlist)s/%(playlist_index)s - " + outputTemplate : outputTemplate);
            args.Add(isPlaylist ? "--yes-playlist" : "--no-playlist");

            // Subtitle extraction flags
            if (subs != null && !string.IsNullOrWhiteSpace(subs.Lang))
            {
                string subLang = subs.Lang.Trim();
                if (subLang == "he,en") subLang = "he.*,iw.*,en.*,he-orig,iw-orig,en-orig";
                else if (subLang == "he") subLang = "he.*,iw.*,he-orig,iw-orig";
                else if (subLang == "en") subLang = "en.*,en-orig";

                args.Add("--write-subs");
                args.Add("--write-auto-subs");
                args.Add("--sub-langs");
                args.Add(subLang);
                args.Add("--convert-subs");
                args.Add("srt");
                args.Add("--no-abort-on-error");
                args.Add("--ignore-no-formats-error");
                args.Add("--extractor-args");
                args.Add("youtube:player_client=android,ios,web_creator");

                if (subs.Type == "embed" && !isAudio)
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

            args.Add("--postprocessor-args"); args.Add("ExtractAudio:-threads 0");
            args.Add("--postprocessor-args"); args.Add("VideoConvertor:-preset ultrafast -threads 0");
            args.Add("--postprocessor-args"); args.Add("VideoRemuxer:-preset ultrafast -threads 0");

            // Target URL - use "--" delimiter to prevent argument injection
            string target = !string.IsNullOrWhiteSpace(directUrl) ? directUrl : url;
            if (!string.IsNullOrWhiteSpace(target))
            {
                args.Add("--");
                args.Add(target.Trim());
            }

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
            else
            {
                string savedCookies = PathUtils.GetSavedCookiesFilePath();
                if (File.Exists(savedCookies))
                {
                    cookiesFile = savedCookies;
                }
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
                ctx.TagMappings,
                ctx.IsNetfree);

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

            onMessage(DownloadMessage.Starting(ctx.DownloadId, ctx.Title, ctx.Thumbnail));

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

                        if (!string.IsNullOrWhiteSpace(line))
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

                // Check if any media files were successfully downloaded into tempDir
                var downloadedMediaFiles = Directory.Exists(tempDir)
                    ? Directory.GetFiles(tempDir)
                        .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                                    !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                                    !Path.GetFileName(f).Equals("cookies.txt", StringComparison.OrdinalIgnoreCase) &&
                                    !Path.GetFileName(f).StartsWith("meta_", StringComparison.OrdinalIgnoreCase))
                        .ToList()
                    : new List<string>();

                bool hasMedia = downloadedMediaFiles.Any(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".mp4" || ext == ".mkv" || ext == ".webm" || ext == ".avi" ||
                           ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav" ||
                           ext == ".opus" || ext == ".ogg" || ext == ".aac";
                });

                if (process.ExitCode == 0 || hasMedia)
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

                    bool isVideo = ctx.IsVideo || (ctx.FormatId != null && !ctx.FormatId.StartsWith("mp3") && ctx.FormatId != "raw_audio" && ctx.FormatId != "generic_audio");
                    bool isEmbedSubs = ctx.Subs != null && ctx.Subs.Type == "embed";

                    if (!isVideo && isEmbedSubs)
                    {
                        try
                        {
                            var mp3Files = Directory.GetFiles(tempDir, "*.mp3");
                            var srtFiles = Directory.GetFiles(tempDir, "*.srt").Concat(Directory.GetFiles(tempDir, "*.vtt")).ToList();
                            if (mp3Files.Length > 0 && srtFiles.Count > 0)
                            {
                                foreach (var mp3 in mp3Files)
                                {
                                    await EmbedLyricsIntoMp3Async(mp3, srtFiles[0], ct);
                                }
                            }
                        }
                        catch { }
                    }

                    string finalPath = MoveFilesSafely(tempDir, targetFolder, isEmbedSubs, !isVideo);
                    try
                    {
                        string logFile = PathUtils.GetLogFilePath();
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [yt-dlp] SUCCESS: Saved to: {finalPath} (ExitCode: {process.ExitCode})\n");
                    }
                    catch { }
                    return new DownloadResult(true, null, finalPath);
                }
                else
                {
                    string fullErr = errorOutput.ToString().Trim();
                    string err = fullErr;

                    if (isNetfreeBlocked || fullErr.Contains("Blocked by NetFree", StringComparison.OrdinalIgnoreCase) || fullErr.Contains("HTTP Error 418", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "נחסם על ידי נטפרי (HTTP 418)";
                    }
                    else if (fullErr.Contains("Could not copy Chrome cookie database", StringComparison.OrdinalIgnoreCase) ||
                             (fullErr.Contains("PermissionError", StringComparison.OrdinalIgnoreCase) && fullErr.Contains("Cookies", StringComparison.OrdinalIgnoreCase)))
                    {
                        err = "שגיאת עוגיות: יש לפתוח את תוסף הדפדפן (Ssshmul), להיכנס להגדרות ⚙️ וללחוץ 'רענן עוגיות מהדפדפן', או לסגור את דפדפן Chrome לחלוטין.";
                    }
                    else if (fullErr.Contains("cookies are no longer valid", StringComparison.OrdinalIgnoreCase) ||
                             fullErr.Contains("The provided YouTube account cookies", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "שגיאת עוגיות: העוגיות פגו. יש להיכנס להגדרות ⚙️ בתוסף הדפדפן וללחוץ על 'רענן עוגיות מהדפדפן'.";
                    }
                    else if (fullErr.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) ||
                             fullErr.Contains("confirm your age", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "יוטיוב דורש אימות: פתח את תוסף הדפדפן, היכנס להגדרות ⚙️ ולחץ 'רענן עוגיות מהדפדפן' (ודא שאתה מחובר ליוטיוב בדפדפן).";
                    }
                    else if (fullErr.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase) || fullErr.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fullErr.Contains("subtitles", StringComparison.OrdinalIgnoreCase))
                        {
                            err = "הורדת הכתוביות נחסמה עקב עומס בקשות מיוטיוב (429 Too Many Requests).";
                        }
                        else
                        {
                            err = "יוטיוב חסם זמנית את הבקשות עקב עומס (שגיאה 429). מומלץ לרענן עוגיות בהגדרות או להמתין מספר דקות.";
                        }
                    }
                    else if (fullErr.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase) || fullErr.Contains("Private video", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "הסרטון אינו זמין או שהוגדר כפרטי.";
                    }
                    else if (fullErr.Contains("Members-only content", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "התוכן זמין לחברי מועדון הערוץ בלבד.";
                    }
                    else if (fullErr.Contains("This video has been removed", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "סרטון זה הוסר מיוטיוב.";
                    }
                    else if (fullErr.Contains("Premieres in", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "סרטון זה טרם שודר (שידור בכורה עתידי).";
                    }
                    else if (fullErr.Contains("Incomplete YouTube ID", StringComparison.OrdinalIgnoreCase) || fullErr.Contains("is not a valid URL", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "כתובת הקישור שהוזנה אינה תקינה.";
                    }
                    else
                    {
                        // Extract lines with ERROR: first if available, so harmless WARNINGs don't hide the real cause
                        var errLines = fullErr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var actualErrors = errLines.Where(l => l.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (actualErrors.Count > 0)
                        {
                            string firstErr = actualErrors[0].Replace("ERROR:", "").Trim();
                            if (firstErr.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase))
                            {
                                err = "יוטיוב חסם זמנית את ההורדה עקב עומס בקשות (HTTP 429). נסה שוב בעוד מספר דקות.";
                            }
                            else
                            {
                                err = $"שגיאה בהורדה: {firstErr}";
                            }
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

        public static string MoveFilesSafely(string sourceDir, string targetDir, bool isEmbedSubs = false, bool isAudio = false)
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string primaryFile = targetDir;
            if (Directory.Exists(sourceDir))
            {
                var allFiles = Directory.GetFiles(sourceDir).ToList();

                // If not embedding subs, handle subtitle deduplication first
                // e.g. if we have both .iw-orig.srt and .iw.srt, or .he-orig.srt and .he.srt, keep only one
                var srtAndVtt = allFiles.Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".sub";
                }).ToList();

                // If both Hebrew and English subtitles exist (e.g. he/iw and en), merge them into a single bilingual SRT
                if (!isEmbedSubs || isAudio)
                {
                    TryMergeBilingualSubtitles(sourceDir);
                    // Refresh file list after potential merge
                    allFiles = Directory.GetFiles(sourceDir).ToList();
                    srtAndVtt = allFiles.Where(f =>
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".sub";
                    }).ToList();
                }

                if (isEmbedSubs)
                {
                    foreach (var sub in srtAndVtt)
                    {
                        try { File.Delete(sub); } catch { }
                        allFiles.Remove(sub);
                    }
                }
                else if (srtAndVtt.Count > 1)
                {
                    // Group by language (e.g. iw vs en) and pick best one per group
                    // Prefer non-orig or orig if only one, discard the duplicate
                    var duplicateToRemove = new List<string>();
                    var origSubs = srtAndVtt.Where(f => f.Contains("-orig.", StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var orig in origSubs)
                    {
                        string nonOrigName = orig.Replace("-orig.", ".");
                        if (srtAndVtt.Any(f => string.Equals(f, nonOrigName, StringComparison.OrdinalIgnoreCase)))
                        {
                            duplicateToRemove.Add(orig);
                        }
                    }

                    foreach (var rem in duplicateToRemove)
                    {
                        try { File.Delete(rem); } catch { }
                        allFiles.Remove(rem);
                        srtAndVtt.Remove(rem);
                    }
                }

                // First find the primary media file name so subtitle can cleanly match it
                string? baseMediaCleanName = null;
                var mediaFile = allFiles.FirstOrDefault(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".mp4" || ext == ".mkv" || ext == ".webm" || ext == ".avi" ||
                           ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav" ||
                           ext == ".opus" || ext == ".ogg" || ext == ".aac";
                });

                if (mediaFile != null)
                {
                    string rawName = Path.GetFileNameWithoutExtension(mediaFile);
                    if (isAudio)
                    {
                        string cleaned = SsshmulDownloader.ArtistTracker.DeduplicationEngine.CleanSongTitle(rawName);
                        baseMediaCleanName = !string.IsNullOrWhiteSpace(cleaned) ? cleaned : rawName;
                    }
                    else
                    {
                        baseMediaCleanName = rawName;
                    }
                }

                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string sourceFilePath = file;
                    string fileName = Path.GetFileName(sourceFilePath);
                    if (fileName.Equals("cookies.txt", StringComparison.OrdinalIgnoreCase)) continue;

                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

                    bool isSubFile = ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".sub";
                    if (isSubFile)
                    {
                        // Clean subtitle name: remove trailing .iw, .he, .iw-orig, .en etc.
                        string cleanSubName = Regex.Replace(nameWithoutExt, @"\.(iw|he|en|iw-orig|he-orig|en-orig)$", "", RegexOptions.IgnoreCase);
                        if (!string.IsNullOrWhiteSpace(baseMediaCleanName))
                        {
                            // If there is only 1 subtitle file, name it exactly as the media file: "Title.ext"
                            // If multiple languages (e.g. English and Hebrew), keep language tag: "Title.he.ext"
                            if (srtAndVtt.Count == 1)
                            {
                                nameWithoutExt = baseMediaCleanName;
                            }
                            else
                            {
                                var matchLang = Regex.Match(nameWithoutExt, @"\.(iw|he|en|iw-orig|he-orig|en-orig)$", RegexOptions.IgnoreCase);
                                string langTag = matchLang.Success ? matchLang.Value : "";
                                nameWithoutExt = baseMediaCleanName + langTag;
                            }
                        }
                        else
                        {
                            nameWithoutExt = cleanSubName;
                        }

                        // For audio downloads, save as .lrc (synchronized lyrics) instead of .srt
                        if (isAudio)
                        {
                            ext = ".lrc";
                            try
                            {
                                string originalSubContent = File.ReadAllText(sourceFilePath, Encoding.UTF8);
                                string lrcContent = ConvertSrtToLrc(originalSubContent);
                                string lrcTempFile = Path.ChangeExtension(sourceFilePath, ".lrc");
                                File.WriteAllText(lrcTempFile, lrcContent, new UTF8Encoding(false));
                                try { File.Delete(sourceFilePath); } catch { }
                                sourceFilePath = lrcTempFile;
                            }
                            catch { }
                        }
                    }
                    else if (isAudio && (ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav" || ext == ".opus" || ext == ".ogg" || ext == ".aac"))
                    {
                        if (!string.IsNullOrWhiteSpace(baseMediaCleanName))
                        {
                            nameWithoutExt = baseMediaCleanName;
                        }
                    }

                    string targetPath = Path.Combine(targetDir, nameWithoutExt + ext);

                    int counter = 1;
                    while (File.Exists(targetPath))
                    {
                        targetPath = Path.Combine(targetDir, $"{nameWithoutExt} ({counter}){ext}");
                        counter++;
                    }

                    try
                    {
                        File.Move(sourceFilePath, targetPath, overwrite: true);
                        if (!targetPath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) &&
                            !targetPath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) &&
                            !targetPath.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
                        {
                            primaryFile = targetPath;
                        }

                        try
                        {
                            string logFile = PathUtils.GetLogFilePath();
                            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [MoveFiles] Moved: {fileName} -> {Path.GetFileName(targetPath)}\n");
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            string logFile = PathUtils.GetLogFilePath();
                            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [MoveFiles] ERROR moving {fileName}: {ex.Message}\n");
                        }
                        catch { }
                    }
                }

                try
                {
                    Directory.Delete(sourceDir, recursive: true);
                }
                catch { }
            }

            return primaryFile;
        }

        private static string ExtractCleanLyricsFromSrt(string srtContent)
        {
            var lines = srtContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            string? lastLine = null;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (int.TryParse(line, out _)) continue;
                if (line.Contains("-->")) continue;
                line = Regex.Replace(line, @"<[^>]+>", "").Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line == lastLine) continue;
                sb.AppendLine(line);
                lastLine = line;
            }
            return sb.ToString().Trim();
        }

        private static async Task EmbedLyricsIntoMp3Async(string mp3Path, string srtPath, CancellationToken ct)
        {
            try
            {
                string ffmpegPath = BinaryResolver.FFmpegPath;
                if (!File.Exists(ffmpegPath) || !File.Exists(mp3Path) || !File.Exists(srtPath)) return;

                string srtText = await File.ReadAllTextAsync(srtPath, Encoding.UTF8, ct);
                string lyrics = ExtractCleanLyricsFromSrt(srtText);
                if (string.IsNullOrWhiteSpace(lyrics)) return;

                string dir = Path.GetDirectoryName(mp3Path) ?? "";
                string tempOut = Path.Combine(dir, "lyrics_" + Path.GetFileName(mp3Path));
                string metaFile = Path.Combine(dir, "meta_" + Guid.NewGuid().ToString("N") + ".txt");

                // Escape metadata in FFmetadata1 format
                string escapedLyrics = lyrics.Replace("\\", "\\\\")
                                             .Replace("=", "\\=")
                                             .Replace(";", "\\;")
                                             .Replace("#", "\\#")
                                             .Replace("\r\n", "\\\n")
                                             .Replace("\n", "\\\n");

                string metaContent = $";FFMETADATA1\nlyrics={escapedLyrics}\nunsyncedlyrics={escapedLyrics}\nUSLT={escapedLyrics}\n";
                await File.WriteAllTextAsync(metaFile, metaContent, new UTF8Encoding(false), ct);

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-nostdin");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(mp3Path);
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(metaFile);
                psi.ArgumentList.Add("-map_metadata");
                psi.ArgumentList.Add("0");
                psi.ArgumentList.Add("-map_metadata");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-id3v2_version");
                psi.ArgumentList.Add("3");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("copy");
                psi.ArgumentList.Add(tempOut);

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var readErr = proc.StandardError.ReadToEndAsync(ct);
                    var readOut = proc.StandardOutput.ReadToEndAsync(ct);
                    await Task.WhenAll(readErr, readOut);
                    await proc.WaitForExitAsync(ct);

                    if (proc.ExitCode == 0 && File.Exists(tempOut) && new FileInfo(tempOut).Length > 0)
                    {
                        File.Delete(mp3Path);
                        File.Move(tempOut, mp3Path);
                        try
                        {
                            File.AppendAllText(PathUtils.GetLogFilePath(), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [EmbedLyrics] Successfully embedded lyrics into {Path.GetFileName(mp3Path)}\n");
                        }
                        catch { }
                    }
                    else
                    {
                        string errStr = await readErr;
                        try
                        {
                            File.AppendAllText(PathUtils.GetLogFilePath(), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [EmbedLyrics] Failed to embed lyrics. ExitCode={proc.ExitCode}, Error: {errStr}\n");
                        }
                        catch { }
                        if (File.Exists(tempOut)) File.Delete(tempOut);
                    }
                }

                if (File.Exists(metaFile))
                {
                    File.Delete(metaFile);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(PathUtils.GetLogFilePath(), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [EmbedLyrics] ERROR: {ex.Message}\n");
                }
                catch { }
            }
        }

        public static async Task<List<SearchResultItem>> SearchAsync(string query, int count, string? cookies = null, string sortBy = "latest", CancellationToken ct = default)
        {
            var results = new List<SearchResultItem>();
            string ytdlpPath = BinaryResolver.YtDlpPath;
            if (string.IsNullOrWhiteSpace(query)) return results;

            int fetchCount = count <= 0 ? 500 : Math.Min(count, 1000);
            string logFile = PathUtils.GetLogFilePath();

            // STEP 1: If query might be an artist, resolve their official YouTube channel and pull catalog sorted accordingly
            try
            {
                var artist = await ArtistTracker.ArtistScanner.ResolveArtistInfoAsync(query);
                if (artist != null && !string.IsNullOrWhiteSpace(artist.ChannelUrl) && (artist.ChannelUrl.StartsWith("http://") || artist.ChannelUrl.StartsWith("https://")))
                {
                    string targetUrl = artist.ChannelUrl;
                    if (!targetUrl.EndsWith("/videos")) targetUrl += "/videos";
                    if (sortBy == "popular")
                    {
                        targetUrl += "?sort=p";
                    }
                    else if (sortBy == "latest" || sortBy == "newest")
                    {
                        targetUrl += "?sort=dd";
                    }

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
                    psiChannel.ArgumentList.Add("--force-ipv4");
                    psiChannel.ArgumentList.Add("--socket-timeout");
                    psiChannel.ArgumentList.Add("15");
                    psiChannel.ArgumentList.Add("--extractor-args");
                    psiChannel.ArgumentList.Add("youtube:player_client=ios,android,web_creator");
                    psiChannel.ArgumentList.Add("--user-agent");
                    psiChannel.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

                    string qjsPath = BinaryResolver.QjsPath;
                    if (File.Exists(qjsPath))
                    {
                        psiChannel.ArgumentList.Add("--js-runtimes");
                        psiChannel.ArgumentList.Add($"quickjs:{qjsPath}");
                    }

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
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Search] Resolved via channel '{artist.Name}' ({sortBy}): {results.Count} songs\n");
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
            if (sortBy == "popular")
            {
                searchTarget = $"ytsearch{fetchCount}:{query} popular";
            }
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
            psi.ArgumentList.Add("--force-ipv4");
            psi.ArgumentList.Add("--socket-timeout");
            psi.ArgumentList.Add("15");

            string qjs = BinaryResolver.QjsPath;
            if (File.Exists(qjs))
            {
                psi.ArgumentList.Add("--js-runtimes");
                psi.ArgumentList.Add($"quickjs:{qjs}");
            }

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
            else
            {
                string savedCookies = PathUtils.GetSavedCookiesFilePath();
                if (File.Exists(savedCookies))
                {
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(savedCookies);
                }
            }

            psi.ArgumentList.Add("--");
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

        private class SrtEntry
        {
            public TimeSpan Start { get; set; }
            public TimeSpan End { get; set; }
            public string Text { get; set; } = string.Empty;
        }

        private static List<SrtEntry> ParseSrtContent(string content)
        {
            var entries = new List<SrtEntry>();
            var blocks = Regex.Split(content.Trim(), @"(?:\r?\n){2,}");
            foreach (var block in blocks)
            {
                var lines = block.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2) continue;

                int timeIdx = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("-->"))
                    {
                        timeIdx = i;
                        break;
                    }
                }
                if (timeIdx == -1) continue;

                var timeMatch = Regex.Match(lines[timeIdx], @"(\d{2}:\d{2}:\d{2}[,\.]\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}[,\.]\d{3})");
                if (!timeMatch.Success) continue;

                if (!TimeSpan.TryParse(timeMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var startTs)) continue;
                if (!TimeSpan.TryParse(timeMatch.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var endTs)) continue;

                var textLines = lines.Skip(timeIdx + 1).Select(l => Regex.Replace(l, @"<[^>]+>", "").Trim()).Where(l => !string.IsNullOrWhiteSpace(l));
                string text = string.Join("\n", textLines).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                entries.Add(new SrtEntry { Start = startTs, End = endTs, Text = text });
            }
            return entries;
        }

        private static void TryMergeBilingualSubtitles(string dir)
        {
            try
            {
                var srtFiles = Directory.GetFiles(dir, "*.srt");
                if (srtFiles.Length < 2) return;

                // Identify Hebrew file (iw or he) and English file (en)
                // Supports yt-dlp generated names like .en.srt, .en-en-nP7.srt, .iw-en-nP7.srt, etc.
                string? heFile = srtFiles.FirstOrDefault(f =>
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    return Regex.IsMatch(name, @"\b(iw|he)\b|\.(iw|he)(-[a-z0-9]+)*\.srt$");
                });

                string? enFile = srtFiles.FirstOrDefault(f =>
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    return f != heFile && (Regex.IsMatch(name, @"\ben\b|\.en(-[a-z0-9]+)*\.srt$") && !name.StartsWith("iw-") && !name.Contains(".iw-"));
                });

                if (heFile == null || enFile == null || heFile == enFile) return;

                string heText = File.ReadAllText(heFile, Encoding.UTF8);
                string enText = File.ReadAllText(enFile, Encoding.UTF8);

                var heEntries = ParseSrtContent(heText);
                var enEntries = ParseSrtContent(enText);

                if (heEntries.Count == 0 || enEntries.Count == 0) return;

                // Monotonic alignment: YouTube translations can have slight shifts or different segment counts.
                // We advance sequentially through both tracks so no Hebrew line is repeated or attached to wrong English lines.
                var mergedSb = new StringBuilder();
                int counter = 1;
                int heIndex = 0;

                foreach (var en in enEntries)
                {
                    // Find the best matching Hebrew entry at or after heIndex
                    int bestHeIdx = -1;
                    double bestScore = double.MaxValue;

                    for (int i = heIndex; i < heEntries.Count; i++)
                    {
                        var h = heEntries[i];
                        // If Hebrew entry starts more than 5 seconds after English ends, stop scanning
                        if ((h.Start - en.End).TotalSeconds > 5) break;

                        // Calculate time difference between starts
                        double diff = Math.Abs((h.Start - en.Start).TotalMilliseconds);
                        bool overlaps = (h.Start <= en.End && h.End >= en.Start);

                        if (overlaps || diff < 2000)
                        {
                            if (diff < bestScore)
                            {
                                bestScore = diff;
                                bestHeIdx = i;
                            }
                        }
                    }

                    string heLine = "";
                    if (bestHeIdx != -1)
                    {
                        heLine = heEntries[bestHeIdx].Text;
                        // Advance heIndex so subsequent English lines don't reuse the same Hebrew line
                        heIndex = bestHeIdx + 1;
                    }

                    string enLine = en.Text;

                    string combinedText;
                    if (!string.IsNullOrWhiteSpace(heLine) && !string.Equals(heLine, enLine, StringComparison.OrdinalIgnoreCase))
                    {
                        // User requirement: English on TOP, Hebrew underneath
                        combinedText = $"{enLine}\n{heLine}";
                    }
                    else
                    {
                        combinedText = enLine;
                    }

                    string startTime = $"{en.Start:hh\\:mm\\:ss\\,fff}";
                    string endTime = $"{en.End:hh\\:mm\\:ss\\,fff}";

                    mergedSb.AppendLine(counter.ToString());
                    mergedSb.AppendLine($"{startTime} --> {endTime}");
                    mergedSb.AppendLine(combinedText);
                    mergedSb.AppendLine();

                    counter++;
                }

                if (mergedSb.Length > 0)
                {
                    // Determine base clean name
                    string firstMediaFile = Directory.GetFiles(dir)
                        .FirstOrDefault(f => !f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) &&
                                             !f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) &&
                                             !f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase) &&
                                             !Path.GetFileName(f).Equals("cookies.txt", StringComparison.OrdinalIgnoreCase));

                    string baseName = firstMediaFile != null
                        ? Path.GetFileNameWithoutExtension(firstMediaFile)
                        : Path.GetFileNameWithoutExtension(heFile);

                    baseName = Regex.Replace(baseName, @"\.(iw|he|en|iw-orig|he-orig|en-orig)(-[a-z0-9]+)*$", "", RegexOptions.IgnoreCase);

                    string mergedFilePath = Path.Combine(dir, $"{baseName}.srt");

                    // Delete ALL other srt files in temp dir so ONLY the merged file remains!
                    foreach (var f in srtFiles)
                    {
                        try { File.Delete(f); } catch { }
                    }

                    File.WriteAllText(mergedFilePath, mergedSb.ToString().Trim() + "\n", new UTF8Encoding(false));

                    try
                    {
                        string logFile = PathUtils.GetLogFilePath();
                        File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [BilingualMerge] Successfully created dual English(top)+Hebrew(bottom) SRT: {Path.GetFileName(mergedFilePath)}\n");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string logFile = PathUtils.GetLogFilePath();
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [BilingualMerge] Exception: {ex.Message}\n");
                }
                catch { }
            }
        }

        private static List<string> SplitIntoWordChunks(string text, int maxWords = 5)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords) return new List<string> { text };

            var chunks = new List<string>();
            for (int i = 0; i < words.Length; i += maxWords)
            {
                int count = Math.Min(maxWords, words.Length - i);
                chunks.Add(string.Join(" ", words.Skip(i).Take(count)));
            }
            return chunks;
        }

        private static string ConvertSrtToLrc(string srtContent)
        {
            var entries = ParseSrtContent(srtContent);
            if (entries.Count == 0) return srtContent;

            var sb = new StringBuilder();

            foreach (var entry in entries)
            {
                var rawLines = entry.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(l => l.Trim())
                                         .Where(l => !string.IsNullOrWhiteSpace(l))
                                         .ToList();

                if (rawLines.Count == 0) continue;

                int mm = (int)entry.Start.TotalMinutes;
                int ss = entry.Start.Seconds;
                int cs = entry.Start.Milliseconds / 10;
                string timeTag = $"[{mm:D2}:{ss:D2}.{cs:D2}]";

                // Check for bilingual block (English on top, Hebrew on bottom, or vice versa)
                bool isBilingual = rawLines.Count >= 2 &&
                    rawLines.Any(l => Regex.IsMatch(l, @"[A-Za-z]")) &&
                    rawLines.Any(l => Regex.IsMatch(l, @"[\u0590-\u05FF]"));

                if (isBilingual)
                {
                    var enParts = rawLines.Where(l => Regex.IsMatch(l, @"[A-Za-z]")).ToList();
                    var heParts = rawLines.Where(l => Regex.IsMatch(l, @"[\u0590-\u05FF]")).ToList();

                    string enText = string.Join(" ", enParts);
                    string heText = string.Join(" ", heParts);

                    // English on top, Hebrew underneath, both with the exact same timestamp
                    sb.AppendLine($"{timeTag}{enText}");
                    sb.AppendLine($"{timeTag}{heText}");
                }
                else
                {
                    foreach (var line in rawLines)
                    {
                        sb.AppendLine($"{timeTag}{line}");
                    }
                }
            }

            return sb.ToString().Trim();
        }
    }
}
