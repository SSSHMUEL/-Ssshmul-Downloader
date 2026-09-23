using System;
using System.IO;
using System.Text.Json;
using SsshmulDownloader.Models;

namespace SsshmulDownloader.Storage
{
    public static class SettingsStore
    {
        private static AppSettings _settings = new();
        private static readonly object _lock = new();
        private static volatile bool _isLoaded = false;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public static AppSettings Current
        {
            get
            {
                Init();
                return _settings;
            }
        }

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

        public static void Update(Action<AppSettings> action)
        {
            Init();
            lock (_lock)
            {
                action(_settings);
                Save();
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    string filePath = PathUtils.GetSettingsFilePath();
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string json = JsonSerializer.Serialize(_settings, _jsonOptions);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                }
            }
        }

        private static void Load()
        {
            lock (_lock)
            {
                string filePath = PathUtils.GetSettingsFilePath();
                if (!File.Exists(filePath)) return;

                try
                {
                    string json = File.ReadAllText(filePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var s = JsonSerializer.Deserialize<AppSettings>(json);
                        if (s != null) _settings = s;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                }
            }
        }
    }
}
