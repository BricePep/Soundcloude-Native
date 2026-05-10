using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    public class LyricsService
    {
        private static readonly HttpClient _http = new();

        static LyricsService()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "SoundCloudNative/1.0");
        }

        /// <summary>
        /// Очищает название трека от мусора SoundCloud для лучшего поиска текстов.
        /// Убирает: (Original Mix), (Radio Edit), feat. Artist, - Remix, и т.д.
        /// </summary>
        private static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;

            var cleaned = title;

            // Убираем содержимое скобок: (Original Mix), (Radio Edit), (Remix), (Deluxe), etc.
            cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)", "", RegexOptions.IgnoreCase);

            // Убираем содержимое квадратных скобок: [Remix], [Radio Edit], etc.
            cleaned = Regex.Replace(cleaned, @"\s*\[[^\]]*\]", "", RegexOptions.IgnoreCase);

            // Убираем feat./ft./featuring и всё после него
            cleaned = Regex.Replace(cleaned, @"\s+(?:feat\.?|ft\.?|featuring)\s+.*$", "", RegexOptions.IgnoreCase);

            // Убираем " - Remix", " - Radio Edit", " - Original Mix" и подобные
            cleaned = Regex.Replace(cleaned, @"\s+-\s+(?:Original\s+Mix|Radio\s+Edit|Remix|Deluxe|Explicit|Clean|Extended|Club\s+Mix|Vocal\s+Mix|Instrumental|Dub\s+Mix).*$", "", RegexOptions.IgnoreCase);

            // Убираем "Remix" в конце если осталось
            cleaned = Regex.Replace(cleaned, @"\s+Remix$", "", RegexOptions.IgnoreCase);

            // Убираем лишние пробелы
            cleaned = cleaned.Trim();

            return cleaned;
        }

        /// <summary>
        /// Очищает имя артиста от мусора.
        /// </summary>
        private static string CleanArtist(string artist)
        {
            if (string.IsNullOrEmpty(artist)) return artist;

            var cleaned = artist;

            // Убираем "Official", "VEVO" и подобное
            cleaned = Regex.Replace(cleaned, @"\s*(?:Official|VEVO|Music|Records|Rec\.)$", "", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }

        /// <summary>
        /// Ищет текст трека через LRCLIB API.
        /// Возвращает (plainLyrics, syncedLyrics) или (null, null) если не найдено.
        /// </summary>
        public async Task<(string? Plain, string? Synced)> SearchAsync(string artist, string title)
        {
            try
            {
                var cleanArtist = CleanArtist(artist);
                var cleanTitle = CleanTitle(title);

                // 1. Точный поиск с очищенными данными
                var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(cleanArtist)}&track_name={Uri.EscapeDataString(cleanTitle)}";
                var resp = await _http.GetAsync(url);

                if (resp.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    var plain = json["plainLyrics"]?.ToString();
                    var synced = json["syncedLyrics"]?.ToString();
                    if (!string.IsNullOrEmpty(plain))
                        return (plain, synced);
                }

                // 2. Поиск через search API с очищенными данными
                var result = await SearchQueryAsync($"{cleanArtist} {cleanTitle}");
                if (result != null) return result.Value;

                // 3. Если не нашли — пробуем только название трека (без артиста)
                result = await SearchQueryAsync(cleanTitle);
                if (result != null) return result.Value;

                // 4. Последняя попытка — оригинальные данные без очистки
                if (cleanArtist != artist || cleanTitle != title)
                {
                    result = await SearchQueryAsync($"{artist} {title}");
                    if (result != null) return result.Value;
                }

                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        private async Task<(string? Plain, string? Synced)?> SearchQueryAsync(string query)
        {
            try
            {
                var searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";
                var searchResp = await _http.GetAsync(searchUrl);

                if (!searchResp.IsSuccessStatusCode) return null;

                var arr = JArray.Parse(await searchResp.Content.ReadAsStringAsync());
                foreach (JObject item in arr)
                {
                    var plain = item["plainLyrics"]?.ToString();
                    if (!string.IsNullOrEmpty(plain))
                    {
                        var synced = item["syncedLyrics"]?.ToString();
                        return (plain, synced);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
