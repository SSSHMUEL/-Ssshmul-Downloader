using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SsshmulDownloader.ArtistTracker;
using SsshmulDownloader.Models;

namespace SsshmulDownloader.Storage
{
    public static class ArtistStore
    {
        private static readonly ConcurrentDictionary<string, TrackedArtist> _artistsMap = new();
        private static readonly object _lock = new();
        private static volatile bool _isLoaded = false;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public static void Init()
        {
            if (_isLoaded) return;
            lock (_lock)
            {
                if (_isLoaded) return;
                Load();
                _isLoaded = true;
            }
        }

        public static List<TrackedArtist> GetAll()
        {
            Init();
            return new List<TrackedArtist>(_artistsMap.Values);
        }

        public static TrackedArtist? GetById(string id)
        {
            Init();
            _artistsMap.TryGetValue(id, out var artist);
            return artist;
        }

        public static void Save(TrackedArtist artist)
        {
            Init();
            if (string.IsNullOrEmpty(artist.Id)) return;
            if (string.IsNullOrWhiteSpace(artist.DownloadFolder) && !string.IsNullOrWhiteSpace(artist.Name))
            {
                artist.DownloadFolder = PathUtils.GetArtistDirectory(artist.Name);
            }
            _artistsMap[artist.Id] = artist;
            Persist();
        }

        public static void Delete(string id)
        {
            Init();
            if (!string.IsNullOrEmpty(id) && _artistsMap.TryRemove(id, out _))
            {
                Persist();
            }
        }

        public static void Persist()
        {
            lock (_lock)
            {
                try
                {
                    string filePath = PathUtils.GetArtistsFilePath();
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string tempPath = filePath + ".tmp";
                    string json = JsonSerializer.Serialize(new List<TrackedArtist>(_artistsMap.Values), _jsonOptions);
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to persist artists: {ex.Message}");
                }
            }
        }

        private static void Load()
        {
            lock (_lock)
            {
                string filePath = PathUtils.GetArtistsFilePath();
                if (!File.Exists(filePath)) return;

                try
                {
                    string json = File.ReadAllText(filePath);
                    if (string.IsNullOrWhiteSpace(json)) return;

                    var list = JsonSerializer.Deserialize<List<TrackedArtist>>(json);
                    if (list != null)
                    {
                        _artistsMap.Clear();
                        foreach (var a in list)
                        {
                            if (!string.IsNullOrEmpty(a.Id))
                            {
                                string artistFolder = !string.IsNullOrWhiteSpace(a.DownloadFolder)
                                    ? a.DownloadFolder
                                    : PathUtils.GetArtistDirectory(a.Name);

                                var onDiskFiles = Directory.Exists(artistFolder)
                                    ? new HashSet<string>(Directory.GetFiles(artistFolder).Select(f => Path.GetFileName(f) ?? ""), StringComparer.OrdinalIgnoreCase)
                                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                                var downloadsFiles = Directory.Exists(downloadsDir)
                                    ? new HashSet<string>(Directory.GetFiles(downloadsDir).Select(f => Path.GetFileName(f) ?? ""), StringComparer.OrdinalIgnoreCase)
                                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                string cleanName = DeduplicationEngine.CleanArtistName(a.Name);
                                foreach (var song in a.RecentSongs)
                                {
                                    bool fileExists = !string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath);
                                    if (!fileExists)
                                    {
                                        // Check if file exists in artist directory or downloads directory
                                        string matchInArtist = onDiskFiles.FirstOrDefault(f => DeduplicationEngine.IsFileMatchingSong(Path.GetFileNameWithoutExtension(f), song.Title, cleanName)) ?? "";
                                        if (!string.IsNullOrEmpty(matchInArtist))
                                        {
                                            song.FilePath = Path.Combine(artistFolder, matchInArtist);
                                            song.IsDownloaded = true;
                                            fileExists = true;
                                        }
                                        else
                                        {
                                            string matchInDl = downloadsFiles.FirstOrDefault(f => DeduplicationEngine.IsFileMatchingSong(Path.GetFileNameWithoutExtension(f), song.Title, cleanName)) ?? "";
                                            if (!string.IsNullOrEmpty(matchInDl))
                                            {
                                                song.FilePath = Path.Combine(downloadsDir, matchInDl);
                                                song.IsDownloaded = true;
                                                fileExists = true;
                                            }
                                        }
                                    }

                                    if (!fileExists && song.IsDownloaded)
                                    {
                                        song.IsDownloaded = false;
                                        song.FilePath = null;
                                    }
                                }

                                a.NewSongsCount = a.RecentSongs.Count(s => !s.IsDownloaded);
                                _artistsMap[a.Id] = a;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load artists from {filePath}: {ex.Message}");
                }
            }
        }
    }
}
