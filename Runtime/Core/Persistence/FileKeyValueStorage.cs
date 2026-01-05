#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class FileKeyValueStorage : IKeyValueStorage
    {
        public string Key { get; }

        private readonly string _filePath;

        public FileKeyValueStorage(string key = "TrippleQ.RaceEvent.Save.v1")
        {
            Key = key;
            _filePath = Path.Combine(Application.persistentDataPath, $"{key}.json");
        }

        public void Clear()
        {
            try
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
            }
            catch { }
        }

        public string? LoadString()
        {
            if (!File.Exists(_filePath)) return null;

            try
            {
                var txt = File.ReadAllText(_filePath);
                return string.IsNullOrWhiteSpace(txt) ? null : txt;
            }
            catch
            {
                return null;
            }
        }

        public void SaveString(string value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"FileKeyValueStorage SaveString failed: {e.Message}");
            }
        }
    }
}
