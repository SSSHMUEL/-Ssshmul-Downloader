using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
