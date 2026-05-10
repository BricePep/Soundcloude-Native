using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    public class GroqService
    {
        private readonly HttpClient _http = new();
        private string? _apiKey = null;

        public void SetApiKey(string? key) => _apiKey = string.IsNullOrWhiteSpace(key) ? null : key;
        public string? GetApiKey() => _apiKey;
        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

        public GroqService()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "SoundCloudNative/1.0");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Анализирует тексты треков и возвращает профиль настроений/языков/энергии пользователя.
        /// Отправляет до 10 текстов за один вызов (чтобы не превысить лимит токенов).
        /// </summary>
        public async Task<UserMusicProfile> AnalyzeLyricsAsync(Dictionary<string, string> trackLyrics)
        {
            if (!IsConfigured || trackLyrics.Count == 0)
                return new UserMusicProfile();

            try
            {
                // Берём до 10 текстов, обрезаем каждый до 500 символов
                var sample = trackLyrics.Take(10).ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Length > 500 ? kvp.Value[..500] : kvp.Value);

                var lyricsBlock = new StringBuilder();
                foreach (var kvp in sample)
                {
                    lyricsBlock.AppendLine($"--- Track: {kvp.Key} ---");
                    lyricsBlock.AppendLine(kvp.Value);
                    lyricsBlock.AppendLine();
                }

                var prompt = $@"Analyze these song lyrics and create a user's music taste profile.

Lyrics:
{lyricsBlock}

Return ONLY a valid JSON object with these fields:
- ""moods"": array of 3-5 mood descriptors (e.g. ""melancholic"", ""energetic"", ""romantic"", ""aggressive"", ""chill"", ""nostalgic"", ""euphoric"", ""dark"", ""dreamy"", ""upbeat"")
- ""languages"": array of detected languages (e.g. ""English"", ""Russian"", ""Spanish"", ""French"", ""Portuguese"", ""Japanese"")
- ""energy"": overall energy level — one of: ""low"", ""medium"", ""high"", ""mixed""
- ""themes"": array of 3-5 recurring themes (e.g. ""love"", ""party"", ""rebellion"", ""nostalgia"", ""self-reflection"", ""social commentary"", ""heartbreak"", ""celebration"")
- ""summary"": one sentence describing the user's music taste based on lyrics

Example:
{{""moods"":[""melancholic"",""dreamy"",""nostalgic""],""languages"":[""English"",""Russian""],""energy"":""mixed"",""themes"":[""love"",""heartbreak"",""self-reflection""],""summary"":""User prefers introspective, emotionally deep music in English and Russian with moderate energy.""}}

Be specific and accurate. Base analysis ONLY on the lyrics content, not on genre tags.";

                var requestBody = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = new object[]
                    {
                        new { role = "system", content = "You are a music and linguistics expert. Return only valid JSON. No markdown, no code blocks, no extra text." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    max_tokens = 1000
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await _http.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Groq lyrics analysis error: {(int)response.StatusCode} - {responseBody}");
                    return new UserMusicProfile();
                }

                var responseJson = JObject.Parse(responseBody);
                var messageContent = responseJson["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(messageContent))
                    return new UserMusicProfile();

                Console.WriteLine($"Groq lyrics analysis: {messageContent}");

                return ParseUserProfile(messageContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Groq lyrics analysis error: {ex.Message}");
                return new UserMusicProfile();
            }
        }

        private UserMusicProfile ParseUserProfile(string content)
        {
            try
            {
                var cleaned = content.Trim();
                if (cleaned.StartsWith("```"))
                {
                    var firstLine = cleaned.IndexOf('\n');
                    if (firstLine >= 0) cleaned = cleaned[(firstLine + 1)..];
                    if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
                    cleaned = cleaned.Trim();
                }

                var json = JObject.Parse(cleaned);

                return new UserMusicProfile
                {
                    Moods = (json["moods"]?.Values<string>()?.ToList() ?? new List<string>()).Where(m => m != null).Select(m => m!).ToList(),
                    Languages = (json["languages"]?.Values<string>()?.ToList() ?? new List<string>()).Where(l => l != null).Select(l => l!).ToList(),
                    Energy = json["energy"]?.ToString() ?? "mixed",
                    Themes = (json["themes"]?.Values<string>()?.ToList() ?? new List<string>()).Where(t => t != null).Select(t => t!).ToList(),
                    Summary = json["summary"]?.ToString() ?? ""
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Parse user profile error: {ex.Message}");
                return new UserMusicProfile();
            }
        }

        /// <summary>
        /// Отправляет список лайкнутых треков + профиль в Groq и получает рекомендации.
        /// Возвращает список поисковых запросов (артист + трек) для SoundCloud.
        /// </summary>
        public async Task<List<RecommendationItem>> GetRecommendationsAsync(List<Track> likedTracks, UserMusicProfile? profile = null)
        {
            if (!IsConfigured || likedTracks.Count == 0)
                return new List<RecommendationItem>();

            try
            {
                // Берём до 30 последних лайков для контекста
                var sampleTracks = likedTracks.Count > 30
                    ? likedTracks.GetRange(0, 30)
                    : likedTracks;

                var trackList = new StringBuilder();
                for (int i = 0; i < sampleTracks.Count; i++)
                {
                    var t = sampleTracks[i];
                    var genreInfo = !string.IsNullOrEmpty(t.Genre) ? $" [{t.Genre}]" : "";
                    trackList.AppendLine($"{i + 1}. \"{t.Title}\" by {t.ArtistName}{genreInfo}");
                }

                // Собираем известные жанры
                var knownGenres = sampleTracks
                    .Select(t => t.Genre?.Trim())
                    .Where(g => !string.IsNullOrEmpty(g))
                    .Distinct()
                    .ToList();

                var genreHint = knownGenres.Count > 0
                    ? $"\n\nDetected genres from likes: {string.Join(", ", knownGenres)}. Suggest tracks in these genres or closely related ones."
                    : "\n\nAnalyze the artist names and track titles to determine the user's preferred genres, then suggest tracks in those genres.";

                // Строим блок профиля на основе анализа текстов
                var profileBlock = new StringBuilder();
                if (profile != null && (profile.Moods.Count > 0 || profile.Languages.Count > 0))
                {
                    profileBlock.AppendLine($"\n\nUser's music taste profile (based on lyrics analysis):");
                    if (profile.Moods.Count > 0)
                        profileBlock.AppendLine($"- Preferred moods: {string.Join(", ", profile.Moods)}");
                    if (profile.Languages.Count > 0)
                        profileBlock.AppendLine($"- Preferred languages: {string.Join(", ", profile.Languages)}");
                    profileBlock.AppendLine($"- Energy level: {profile.Energy}");
                    if (profile.Themes.Count > 0)
                        profileBlock.AppendLine($"- Lyrical themes: {string.Join(", ", profile.Themes)}");
                    if (!string.IsNullOrEmpty(profile.Summary))
                        profileBlock.AppendLine($"- Summary: {profile.Summary}");
                    profileBlock.AppendLine("\nIMPORTANT: Prioritize tracks that match the user's mood preferences, lyrical themes, and language preferences. Suggest tracks with similar emotional tone and themes.");
                }

                var prompt = $@"Based on the user's liked tracks, suggest 30 similar tracks they would enjoy but haven't heard yet.
{genreHint}{profileBlock}
Liked tracks:
{trackList}

Return ONLY a valid JSON object with a ""recommendations"" array. Each item must have:
- ""artist"": artist name (exact spelling)
- ""title"": track title (exact spelling)
- ""reason"": brief reason (2-4 words)

Example:
{{""recommendations"":[{{""artist"":""Bicep"",""title"":""Glue"",""reason"":""Similar melodic electronic""}}]}}

Do not suggest tracks already in the likes. Be specific with real track names and artists. Focus on the same genres, moods and themes.";

                var requestBody = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = new object[]
                    {
                        new { role = "system", content = "You are a music expert. Return only valid JSON. No markdown, no code blocks, no extra text." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.8,
                    max_tokens = 4000
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await _http.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Groq API error: {(int)response.StatusCode} - {responseBody}");
                    return new List<RecommendationItem>();
                }

                var responseJson = JObject.Parse(responseBody);
                var messageContent = responseJson["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(messageContent))
                    return new List<RecommendationItem>();

                Console.WriteLine($"Groq response: {messageContent}");

                return ParseRecommendations(messageContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Groq recommendation error: {ex.Message}");
                return new List<RecommendationItem>();
            }
        }

        private List<RecommendationItem> ParseRecommendations(string content)
        {
            var results = new List<RecommendationItem>();

            try
            {
                // Убираем markdown-обёртки если LLM их добавил
                var cleaned = content.Trim();
                if (cleaned.StartsWith("```"))
                {
                    var firstLine = cleaned.IndexOf('\n');
                    if (firstLine >= 0) cleaned = cleaned[(firstLine + 1)..];
                    if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
                    cleaned = cleaned.Trim();
                }

                var json = JToken.Parse(cleaned);
                JArray? items = null;

                if (json is JArray arr)
                {
                    items = arr;
                }
                else if (json is JObject obj)
                {
                    // Ищем массив в любом поле
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value is JArray propArr && propArr.Count > 0)
                        {
                            items = propArr;
                            break;
                        }
                    }
                }

                if (items == null) return results;

                foreach (var item in items)
                {
                    var artist = item["artist"]?.ToString()?.Trim();
                    var title = item["title"]?.ToString()?.Trim();
                    var reason = item["reason"]?.ToString()?.Trim() ?? "";

                    if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
                        continue;

                    results.Add(new RecommendationItem
                    {
                        Artist = artist,
                        Title = title,
                        Reason = reason
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Parse recommendations error: {ex.Message}");
                Console.WriteLine($"Raw content: {content}");
            }

            return results;
        }
    }

    public class RecommendationItem
    {
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Профиль музыкальных предпочтений пользователя на основе анализа текстов.
    /// </summary>
    public class UserMusicProfile
    {
        public List<string> Moods { get; set; } = new();
        public List<string> Languages { get; set; } = new();
        public string Energy { get; set; } = "mixed";
        public List<string> Themes { get; set; } = new();
        public string Summary { get; set; } = "";
    }
}
