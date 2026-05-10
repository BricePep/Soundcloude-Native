using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    public class UserInfo
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
    }

    public class SoundCloudService
    {
        private readonly HttpClient _http = new();
        private string? _clientId;
        private string? _oauthToken;
        private string? _userId;

        public bool IsLoggedIn => !string.IsNullOrEmpty(_oauthToken);

        public SoundCloudService()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public void SetOAuthToken(string token)
        {
            _oauthToken = string.IsNullOrWhiteSpace(token) ? null : token;
        }

        public string? GetOAuthToken() => _oauthToken;

        private HttpClient GetAuthenticatedClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Origin", "https://soundcloud.com");
            client.DefaultRequestHeaders.Add("Referer", "https://soundcloud.com/");
            
            if (!string.IsNullOrEmpty(_oauthToken))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", _oauthToken);
            return client;
        }

        public async Task<UserInfo?> GetCurrentUserAsync()
        {
            if (string.IsNullOrEmpty(_oauthToken)) return null;

            try
            {
                var clientId = await GetClientIdAsync();
                using var client = GetAuthenticatedClient();
                var url = $"https://api-v2.soundcloud.com/me?client_id={clientId}";
                var httpResponse = await client.GetAsync(url);
                
                if (!httpResponse.IsSuccessStatusCode)
                {
                    var errorBody = await httpResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"GetCurrentUser: HTTP {(int)httpResponse.StatusCode} - {errorBody}");
                    return null;
                }
                
                var response = await httpResponse.Content.ReadAsStringAsync();
                var json = JObject.Parse(response);

                var userId = json["id"]?.ToString() ?? "";
                _userId = userId;

                return new UserInfo
                {
                    Id = userId,
                    Username = json["username"]?.ToString() ?? json["full_name"]?.ToString() ?? "Пользователь",
                    AvatarUrl = json["avatar_url"]?.ToString() ?? ""
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetCurrentUser error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private async Task<string> GetClientIdAsync()
        {
            if (!string.IsNullOrEmpty(_clientId))
                return _clientId;

            try
            {
                var html = await _http.GetStringAsync("https://soundcloud.com");
                var scriptMatches = Regex.Matches(html, @"<script[^>]+src=""(https://a-v2\.sndcdn\.com/[^""]+)""");

                foreach (Match match in scriptMatches)
                {
                    var scriptUrl = match.Groups[1].Value;
                    try
                    {
                        var scriptContent = await _http.GetStringAsync(scriptUrl);
                        var clientIdMatch = Regex.Match(scriptContent, @"client_id:\s*[""']([a-zA-Z0-9]+)[""']");
                        if (clientIdMatch.Success)
                        {
                            _clientId = clientIdMatch.Groups[1].Value;
                            return _clientId;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return "a3e059563d7fd3372b49b37f00a00bcf";
        }

        public async Task<List<Track>> SearchAsync(string query, int maxResults = 20)
        {
            var results = new List<Track>();
            try
            {
                var clientId = await GetClientIdAsync();
                var url = $"https://api-v2.soundcloud.com/search/tracks?q={Uri.EscapeDataString(query)}&client_id={clientId}&limit={maxResults}";

                var response = await _http.GetStringAsync(url);
                var json = JObject.Parse(response);
                var tracks = json["collection"] as JArray;

                if (tracks == null) return results;

                foreach (var item in tracks)
                {
                    var trackId = item["id"]?.ToString();
                    var title = item["title"]?.ToString();
                    var artist = item["user"]?["username"]?.ToString();
                    var artwork = item["artwork_url"]?.ToString();
                    var genre = item["genre"]?.ToString() ?? item["label"]?.ToString() ?? "";
                    var duration = item["duration"]?.ToObject<int>() ?? 0;

                    if (string.IsNullOrEmpty(trackId)) continue;

                    if (!string.IsNullOrEmpty(artwork))
                        artwork = artwork.Replace("-large", "-t500x500");

                    results.Add(new Track
                    {
                        VideoId = trackId,
                        Title = title ?? "Unknown",
                        ArtistName = artist ?? "Unknown",
                        ArtworkUrl = artwork ?? "",
                        Genre = genre,
                        DurationSeconds = duration / 1000
                    });

                    if (results.Count >= maxResults) break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search error: {ex.Message}");
            }
            return results;
        }

        public async Task<string?> GetAudioStreamUrlAsync(string trackId)
        {
            try
            {
                var clientId = await GetClientIdAsync();
                var url = $"https://api-v2.soundcloud.com/tracks/{trackId}?client_id={clientId}";

                var response = await _http.GetStringAsync(url);
                var json = JObject.Parse(response);

                var transcodings = json["media"]?["transcodings"] as JArray;
                if (transcodings == null) return null;

                // Сначала пробуем progressive (прямой MP3 — лучше для плеера)
                foreach (var transcoding in transcodings)
                {
                    var format = transcoding["format"]?["protocol"]?.ToString();
                    if (format == "progressive")
                    {
                        var streamUrl = transcoding["url"]?.ToString();
                        if (string.IsNullOrEmpty(streamUrl)) continue;

                        var streamResponse = await _http.GetStringAsync($"{streamUrl}?client_id={clientId}");
                        var streamJson = JObject.Parse(streamResponse);
                        var resultUrl = streamJson["url"]?.ToString();
                        if (!string.IsNullOrEmpty(resultUrl)) return resultUrl;
                    }
                }

                // Fallback: пробуем HLS (m3u8) — MediaFoundationReader поддерживает
                foreach (var transcoding in transcodings)
                {
                    var format = transcoding["format"]?["protocol"]?.ToString();
                    if (format == "hls")
                    {
                        var streamUrl = transcoding["url"]?.ToString();
                        if (string.IsNullOrEmpty(streamUrl)) continue;

                        var streamResponse = await _http.GetStringAsync($"{streamUrl}?client_id={clientId}");
                        var streamJson = JObject.Parse(streamResponse);
                        var resultUrl = streamJson["url"]?.ToString();
                        if (!string.IsNullOrEmpty(resultUrl)) return resultUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stream error: {ex.Message}");
            }
            return null;
        }

        public async Task<(List<Track> tracks, string? error)> GetUserLikesAsync()
        {
            var results = new List<Track>();
            if (string.IsNullOrEmpty(_oauthToken)) return (results, "Не выполнен вход в аккаунт");

            try
            {
                var clientId = await GetClientIdAsync();
                using var client = GetAuthenticatedClient();
                
                // Сначала получаем user_id если его нет
                if (string.IsNullOrEmpty(_userId))
                {
                    var meUrl = $"https://api-v2.soundcloud.com/me?client_id={clientId}";
                    var meResponse = await client.GetAsync(meUrl);
                    if (!meResponse.IsSuccessStatusCode)
                    {
                        var meError = await meResponse.Content.ReadAsStringAsync();
                        return (results, $"Не удалось получить профиль (HTTP {(int)meResponse.StatusCode}):\n{meError}");
                    }
                    var meJson = JObject.Parse(await meResponse.Content.ReadAsStringAsync());
                    _userId = meJson["id"]?.ToString();
                }
                
                if (string.IsNullOrEmpty(_userId))
                    return (results, "Не удалось получить ID пользователя");

                // Пробуем разные endpoints (с дефисами!)
                var endpoints = new[]
                {
                    $"/users/{_userId}/track-likes",
                    $"/me/track-likes",
                    $"/users/{_userId}/likes",
                    $"/me/likes"
                };

                var errorDetails = new System.Text.StringBuilder();

                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var url = $"https://api-v2.soundcloud.com{endpoint}?client_id={clientId}&limit=50";
                        Console.WriteLine($"[Likes] Trying GET {url}");
                        var httpResponse = await client.GetAsync(url);
                        var statusCode = (int)httpResponse.StatusCode;
                        Console.WriteLine($"[Likes] Status: {statusCode}");

                        if (!httpResponse.IsSuccessStatusCode)
                        {
                            var errorBody = await httpResponse.Content.ReadAsStringAsync();
                            errorDetails.AppendLine($"• {endpoint}: HTTP {statusCode}");
                            if (!string.IsNullOrWhiteSpace(errorBody) && errorBody.Length < 200)
                                errorDetails.AppendLine($"  {errorBody}");
                            continue;
                        }

                        var response = await httpResponse.Content.ReadAsStringAsync();
                        var json = JObject.Parse(response);
                        var collection = json["collection"] as JArray;

                        if (collection == null)
                        {
                            var keys = string.Join(", ", json.Properties().Select(p => p.Name));
                            errorDetails.AppendLine($"• {endpoint}: нет collection (ключи: {keys})");
                            continue;
                        }

                        Console.WriteLine($"[Likes] Success! {collection.Count} items from {endpoint}");

                        foreach (var item in collection)
                        {
                            var track = item["track"] ?? item;
                            if (track == null) continue;

                            var kind = track["kind"]?.ToString();
                            if (kind != null && kind != "track") continue;

                            var trackId = track["id"]?.ToString();
                            var title = track["title"]?.ToString();
                            var artist = track["user"]?["username"]?.ToString();
                            var artwork = track["artwork_url"]?.ToString();
                            var genre = track["genre"]?.ToString() ?? track["label"]?.ToString() ?? "";
                            var duration = track["duration"]?.ToObject<int>() ?? 0;

                            if (string.IsNullOrEmpty(trackId)) continue;

                            if (!string.IsNullOrEmpty(artwork))
                                artwork = artwork.Replace("-large", "-t500x500");

                            results.Add(new Track
                            {
                                VideoId = trackId,
                                Title = title ?? "Unknown",
                                ArtistName = artist ?? "Unknown",
                                ArtworkUrl = artwork ?? "",
                                Genre = genre,
                                DurationSeconds = duration / 1000
                            });
                        }

                        return (results, null);
                    }
                    catch (Exception ex)
                    {
                        errorDetails.AppendLine($"• {endpoint}: {ex.GetType().Name}");
                        errorDetails.AppendLine($"  {ex.Message}");
                    }
                }

                return (results, $"Все API endpoints вернули ошибку:\n\n{errorDetails}");
            }
            catch (Exception ex)
            {
                return (results, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public async Task<List<Track>> GetTracksByIdsAsync(List<string> trackIds)
        {
            var results = new List<Track>();
            if (trackIds.Count == 0) return results;

            try
            {
                var clientId = await GetClientIdAsync();
                using var client = GetAuthenticatedClient();

                // SoundCloud API позволяет запросить до 50 треков за раз
                for (int i = 0; i < trackIds.Count; i += 50)
                {
                    var batch = trackIds.Skip(i).Take(50);
                    var ids = string.Join(",", batch);
                    var url = $"https://api-v2.soundcloud.com/tracks?client_id={clientId}&ids={ids}";

                    var httpResponse = await client.GetAsync(url);
                    if (!httpResponse.IsSuccessStatusCode) continue;

                    var response = await httpResponse.Content.ReadAsStringAsync();
                    var json = JToken.Parse(response);

                    var tracks = json as JArray;
                    if (tracks == null) continue;

                    foreach (var item in tracks)
                    {
                        var trackId = item["id"]?.ToString();
                        var title = item["title"]?.ToString();
                        var artist = item["user"]?["username"]?.ToString();
                        var artwork = item["artwork_url"]?.ToString();
                        var genre = item["genre"]?.ToString() ?? item["label"]?.ToString() ?? "";
                        var duration = item["duration"]?.ToObject<int>() ?? 0;

                        if (string.IsNullOrEmpty(trackId)) continue;

                        if (!string.IsNullOrEmpty(artwork))
                            artwork = artwork.Replace("-large", "-t500x500");

                        results.Add(new Track
                        {
                            VideoId = trackId,
                            Title = title ?? "Unknown",
                            ArtistName = artist ?? "Unknown",
                            ArtworkUrl = artwork ?? "",
                            Genre = genre,
                            DurationSeconds = duration / 1000
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetTracksByIds error: {ex.Message}");
            }
            return results;
        }

        public async Task<(List<Playlist> playlists, string? error)> GetUserPlaylistsAsync()
        {
            var results = new List<Playlist>();
            if (string.IsNullOrEmpty(_oauthToken)) return (results, "Не выполнен вход в аккаунт");

            try
            {
                var clientId = await GetClientIdAsync();
                using var client = GetAuthenticatedClient();
                
                // Сначала получаем user_id если его нет
                if (string.IsNullOrEmpty(_userId))
                {
                    var meUrl = $"https://api-v2.soundcloud.com/me?client_id={clientId}";
                    var meResponse = await client.GetAsync(meUrl);
                    if (!meResponse.IsSuccessStatusCode)
                    {
                        var meError = await meResponse.Content.ReadAsStringAsync();
                        return (results, $"Не удалось получить профиль (HTTP {(int)meResponse.StatusCode}):\n{meError}");
                    }
                    var meJson = JObject.Parse(await meResponse.Content.ReadAsStringAsync());
                    _userId = meJson["id"]?.ToString();
                }
                
                if (string.IsNullOrEmpty(_userId))
                    return (results, "Не удалось получить ID пользователя");

                // Пробуем разные endpoints (с дефисами!)
                var endpoints = new[]
                {
                    $"/users/{_userId}/playlists",
                    $"/me/playlists",
                    $"/users/{_userId}/playlist-likes",
                    $"/me/playlist-likes"
                };

                var errorDetails = new System.Text.StringBuilder();

                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var url = $"https://api-v2.soundcloud.com{endpoint}?client_id={clientId}&limit=50";
                        Console.WriteLine($"[Playlists] Trying GET {url}");
                        var httpResponse = await client.GetAsync(url);
                        var statusCode = (int)httpResponse.StatusCode;
                        Console.WriteLine($"[Playlists] Status: {statusCode}");

                        if (!httpResponse.IsSuccessStatusCode)
                        {
                            var errorBody = await httpResponse.Content.ReadAsStringAsync();
                            errorDetails.AppendLine($"• {endpoint}: HTTP {statusCode}");
                            if (!string.IsNullOrWhiteSpace(errorBody) && errorBody.Length < 200)
                                errorDetails.AppendLine($"  {errorBody}");
                            continue;
                        }

                        var response = await httpResponse.Content.ReadAsStringAsync();
                        var json = JToken.Parse(response);
                        var collection = json["collection"] as JArray;

                        if (collection == null)
                        {
                            // Возможно API вернул массив напрямую (без обёртки)
                            if (json is JArray directArray)
                            {
                                collection = directArray;
                            }
                            else
                            {
                                var keys = string.Join(", ", ((JObject)json).Properties().Select(p => p.Name));
                                errorDetails.AppendLine($"• {endpoint}: нет collection (ключи: {keys})");
                                continue;
                            }
                        }

                        Console.WriteLine($"[Playlists] Success! {collection.Count} items from {endpoint}");

                        // Собираем все ID треков с неполными данными для дозагрузки
                        var incompleteTrackIds = new List<string>();

                        foreach (var item in collection)
                        {
                            var kind = item["kind"]?.ToString();
                            if (kind != "playlist" && kind != "system-playlist") continue;

                            var playlistId = item["id"]?.ToString();
                            var title = item["title"]?.ToString();

                            if (string.IsNullOrEmpty(playlistId)) continue;

                            var playlist = new Playlist
                            {
                                Name = title ?? "Unknown",
                                Tracks = new List<Track>()
                            };

                            var tracks = item["tracks"] as JArray;
                            if (tracks != null)
                            {
                                foreach (var trackItem in tracks)
                                {
                                    var trackId = trackItem["id"]?.ToString();
                                    var trackTitle = trackItem["title"]?.ToString();
                                    var artist = trackItem["user"]?["username"]?.ToString();

                                    if (string.IsNullOrEmpty(trackId)) continue;

                                    // Если данных неполные — запомним ID для дозагрузки
                                    if (string.IsNullOrEmpty(trackTitle) || string.IsNullOrEmpty(artist))
                                    {
                                        incompleteTrackIds.Add(trackId);
                                        playlist.Tracks.Add(new Track { VideoId = trackId });
                                    }
                                    else
                                    {
                                        var artwork = trackItem["artwork_url"]?.ToString();
                                        var duration = trackItem["duration"]?.ToObject<int>() ?? 0;

                                        if (!string.IsNullOrEmpty(artwork))
                                            artwork = artwork.Replace("-large", "-t500x500");

                                        playlist.Tracks.Add(new Track
                                        {
                                            VideoId = trackId,
                                            Title = trackTitle,
                                            ArtistName = artist,
                                            ArtworkUrl = artwork ?? "",
                                            DurationSeconds = duration / 1000
                                        });
                                    }
                                }
                            }

                            results.Add(playlist);
                        }

                        // Дозагружаем полные данные для треков с неполной информацией
                        if (incompleteTrackIds.Count > 0)
                        {
                            Console.WriteLine($"[Playlists] Fetching full data for {incompleteTrackIds.Count} incomplete tracks");
                            var fullTracks = await GetTracksByIdsAsync(incompleteTrackIds);
                            var trackMap = fullTracks.ToDictionary(t => t.VideoId, t => t);

                            foreach (var playlist in results)
                            {
                                for (int i = 0; i < playlist.Tracks.Count; i++)
                                {
                                    var track = playlist.Tracks[i];
                                    if (track.Title == "Unknown" || string.IsNullOrEmpty(track.Title))
                                    {
                                        if (trackMap.TryGetValue(track.VideoId, out var fullTrack))
                                        {
                                            playlist.Tracks[i] = fullTrack;
                                        }
                                    }
                                }
                            }
                        }

                        return (results, null);
                    }
                    catch (Exception ex)
                    {
                        errorDetails.AppendLine($"• {endpoint}: {ex.GetType().Name}");
                        errorDetails.AppendLine($"  {ex.Message}");
                    }
                }

                return (results, $"Все API endpoints вернули ошибку:\n\n{errorDetails}");
            }
            catch (Exception ex)
            {
                return (results, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public async Task<List<Track>> SearchByGenreAsync(string genre, int limit = 20)
        {
            var results = new List<Track>();
            try
            {
                var clientId = await GetClientIdAsync();
                var url = $"https://api-v2.soundcloud.com/search/tracks?q=&genre={Uri.EscapeDataString(genre)}&client_id={clientId}&limit={limit}";

                var response = await _http.GetStringAsync(url);
                var json = JObject.Parse(response);
                var tracks = json["collection"] as JArray;

                if (tracks == null) return results;

                foreach (var item in tracks)
                {
                    var trackId = item["id"]?.ToString();
                    var title = item["title"]?.ToString();
                    var artist = item["user"]?["username"]?.ToString();
                    var artwork = item["artwork_url"]?.ToString();
                    var trackGenre = item["genre"]?.ToString() ?? item["label"]?.ToString() ?? "";
                    var duration = item["duration"]?.ToObject<int>() ?? 0;

                    if (string.IsNullOrEmpty(trackId)) continue;

                    if (!string.IsNullOrEmpty(artwork))
                        artwork = artwork.Replace("-large", "-t500x500");

                    results.Add(new Track
                    {
                        VideoId = trackId,
                        Title = title ?? "Unknown",
                        ArtistName = artist ?? "Unknown",
                        ArtworkUrl = artwork ?? "",
                        Genre = trackGenre,
                        DurationSeconds = duration / 1000
                    });

                    if (results.Count >= limit) break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SearchByGenre error: {ex.Message}");
            }
            return results;
        }

        public async Task<List<Track>> GetRecommendationsAsync()
        {
            var results = new List<Track>();
            if (string.IsNullOrEmpty(_oauthToken)) return results;

            try
            {
                var clientId = await GetClientIdAsync();
                using var client = GetAuthenticatedClient();

                // Пробуем официальный эндпоинт рекомендаций
                var url = $"https://api-v2.soundcloud.com/me/recommendations?client_id={clientId}&limit=20";
                var httpResponse = await client.GetAsync(url);

                if (httpResponse.IsSuccessStatusCode)
                {
                    var response = await httpResponse.Content.ReadAsStringAsync();
                    var json = JObject.Parse(response);
                    var collection = json["collection"] as JArray;

                    if (collection != null)
                    {
                        foreach (var item in collection)
                        {
                            var track = item["track"] ?? item;
                            var trackId = track["id"]?.ToString();
                            var title = track["title"]?.ToString();
                            var artist = track["user"]?["username"]?.ToString();
                            var artwork = track["artwork_url"]?.ToString();
                            var genre = track["genre"]?.ToString() ?? track["label"]?.ToString() ?? "";
                            var duration = track["duration"]?.ToObject<int>() ?? 0;

                            if (string.IsNullOrEmpty(trackId)) continue;

                            if (!string.IsNullOrEmpty(artwork))
                                artwork = artwork.Replace("-large", "-t500x500");

                            results.Add(new Track
                            {
                                VideoId = trackId,
                                Title = title ?? "Unknown",
                                ArtistName = artist ?? "Unknown",
                                ArtworkUrl = artwork ?? "",
                                Genre = genre,
                                DurationSeconds = duration / 1000
                            });
                        }
                        return results;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRecommendations error: {ex.Message}");
            }
            return results;
        }
    }
}
