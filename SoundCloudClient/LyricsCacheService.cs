using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    /// <summary>
    /// Кеширует тексты треков на диске (%LocalAppData%/MusicBox/lyrics_cache/).
    /// Формат: один JSON-файл на трек, ключ — VideoId.
    /// </summary>
    public class LyricsCacheService
    {
        private readonly string _cacheDir;

        public LyricsCacheService()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicBox", "lyrics_cache");
        }

        /// <summary>
        /// Получить текст из кеша. Возвращает (plain, synced) или null.
        /// </summary>
        public (string? Plain, string? Synced)? Get(string trackId)
        {
            var file = GetFilePath(trackId);
            if (!File.Exists(file)) return null;

            try
            {
                var json = JObject.Parse(File.ReadAllText(file));
                var plain = json["plain"]?.ToString();
                var synced = json["synced"]?.ToString();
                if (string.IsNullOrEmpty(plain) && string.IsNullOrEmpty(synced))
                    return null;
                return (plain, synced);
            }
            catch { return null; }
        }

        /// <summary>
        /// Сохранить текст в кеш.
        /// </summary>
        public void Put(string trackId, string? plain, string? synced)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var json = new JObject
                {
                    ["plain"] = plain ?? "",
                    ["synced"] = synced ?? "",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                File.WriteAllText(GetFilePath(trackId), json.ToString(Formatting.None));
            }
            catch { }
        }

        /// <summary>
        /// Отметить что текст не найден (чтобы не искать повторно).
        /// </summary>
        public void MarkNotFound(string trackId)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var json = new JObject
                {
                    ["plain"] = "",
                    ["synced"] = "",
                    ["not_found"] = true,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                File.WriteAllText(GetFilePath(trackId), json.ToString(Formatting.None));
            }
            catch { }
        }

        /// <summary>
        /// Проверяет, был ли уже поиск для этого трека (включая not_found).
        /// </summary>
        public bool Exists(string trackId)
        {
            return File.Exists(GetFilePath(trackId));
        }

        /// <summary>
        /// Был ли текст помечен как не найденный.
        /// </summary>
        public bool IsNotFound(string trackId)
        {
            var file = GetFilePath(trackId);
            if (!File.Exists(file)) return false;
            try
            {
                var json = JObject.Parse(File.ReadAllText(file));
                return json["not_found"]?.Value<bool>() == true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Получить все кешированные тексты (для анализа профиля).
        /// </summary>
        public Dictionary<string, (string Plain, string? Synced)> GetAll()
        {
            var result = new Dictionary<string, (string, string?)>();
            try
            {
                if (!Directory.Exists(_cacheDir)) return result;
                foreach (var file in Directory.GetFiles(_cacheDir, "*.json"))
                {
                    try
                    {
                        var json = JObject.Parse(File.ReadAllText(file));
                        var plain = json["plain"]?.ToString();
                        if (string.IsNullOrEmpty(plain)) continue;
                        var trackId = Path.GetFileNameWithoutExtension(file);
                        var synced = json["synced"]?.ToString();
                        result[trackId] = (plain!, string.IsNullOrEmpty(synced) ? null : synced);
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private string GetFilePath(string trackId)
        {
            // Убираем недопустимые символы из ID
            var safe = string.Join("_", trackId.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_cacheDir, $"{safe}.json");
        }
    }
}
