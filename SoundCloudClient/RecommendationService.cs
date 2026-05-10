using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    public class RecommendationService
    {
        private readonly SoundCloudService _soundcloud;
        private readonly GroqService _groq;
        private readonly LyricsService _lyricsService;
        private readonly LyricsCacheService _lyricsCache;
        private List<string> _topGenres = new();
        private HashSet<string> _likedTrackIds = new();
        private List<Track> _cachedLikedTracks = new();
        private UserMusicProfile? _userProfile;
        private bool _profileAnalyzed;

        public RecommendationService(SoundCloudService soundcloud, GroqService groq)
        {
            _soundcloud = soundcloud;
            _groq = groq;
            _lyricsService = new LyricsService();
            _lyricsCache = new LyricsCacheService();
        }

        /// <summary>
        /// Анализирует лайки пользователя и определяет топ-жанры.
        /// Вызывать после загрузки лайков.
        /// </summary>
        public void AnalyzeGenres(List<Track> likedTracks)
        {
            _likedTrackIds = likedTracks.Select(t => t.VideoId).ToHashSet();
            _cachedLikedTracks = likedTracks;

            var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in likedTracks)
            {
                var genre = track.Genre?.Trim();
                if (string.IsNullOrEmpty(genre)) continue;

                // Нормализация: lowercase, убираем лишние пробелы
                var normalized = string.Join(" ", genre.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

                // Группируем похожие жанры
                var key = NormalizeGenre(normalized);
                if (string.IsNullOrEmpty(key)) continue;

                if (!genreCounts.ContainsKey(key))
                    genreCounts[key] = 0;
                genreCounts[key]++;
            }

            _topGenres = genreCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Возвращает топ-жанры пользователя (для отображения и кеша).
        /// </summary>
        public List<string> GetTopGenres() => _topGenres.ToList();

        /// <summary>
        /// Устанавливает топ-жанры из кеша.
        /// </summary>
        public void SetTopGenres(List<string> genres) => _topGenres = genres;

        /// <summary>
        /// Возвращает профиль пользователя (из кеша или после анализа).
        /// </summary>
        public UserMusicProfile? GetUserProfile() => _userProfile;

        /// <summary>
        /// Устанавливает профиль из кеша.
        /// </summary>
        public void SetUserProfile(UserMusicProfile profile)
        {
            _userProfile = profile;
            _profileAnalyzed = true;
        }

        /// <summary>
        /// Анализирует тексты лайкнутых треков и строит профиль пользователя.
        /// Фетчит тексты из кеша или через LyricsService, затем отправляет в Groq.
        /// </summary>
        public async Task AnalyzeLyricsAsync()
        {
            if (!_groq.IsConfigured || _cachedLikedTracks.Count == 0 || _profileAnalyzed)
                return;

            try
            {
                // Берём до 20 треков для анализа текстов
                var sampleTracks = _cachedLikedTracks.Count > 20
                    ? _cachedLikedTracks.GetRange(0, 20)
                    : _cachedLikedTracks;

                var trackLyrics = new Dictionary<string, string>();

                foreach (var track in sampleTracks)
                {
                    try
                    {
                        // Сначала проверяем кеш
                        if (_lyricsCache.Exists(track.VideoId))
                        {
                            if (_lyricsCache.IsNotFound(track.VideoId))
                                continue;

                            var cached = _lyricsCache.Get(track.VideoId);
                            if (cached != null && !string.IsNullOrEmpty(cached.Value.Plain))
                            {
                                trackLyrics[$"{track.ArtistName} - {track.Title}"] = cached.Value.Plain;
                                continue;
                            }
                        }

                        // Фетчим из API
                        var (plain, synced) = await _lyricsService.SearchAsync(track.ArtistName, track.Title);

                        if (!string.IsNullOrEmpty(plain))
                        {
                            _lyricsCache.Put(track.VideoId, plain, synced);
                            trackLyrics[$"{track.ArtistName} - {track.Title}"] = plain;
                        }
                        else
                        {
                            _lyricsCache.MarkNotFound(track.VideoId);
                        }
                    }
                    catch { }
                }

                if (trackLyrics.Count == 0)
                {
                    Console.WriteLine("No lyrics found for any liked tracks, skipping profile analysis");
                    _profileAnalyzed = true;
                    return;
                }

                Console.WriteLine($"Analyzing {trackLyrics.Count} track lyrics for user profile...");

                _userProfile = await _groq.AnalyzeLyricsAsync(trackLyrics);
                _profileAnalyzed = true;

                if (_userProfile != null && _userProfile.Moods.Count > 0)
                {
                    Console.WriteLine($"User profile: moods=[{string.Join(", ", _userProfile.Moods)}], " +
                                      $"languages=[{string.Join(", ", _userProfile.Languages)}], " +
                                      $"energy={_userProfile.Energy}, " +
                                      $"themes=[{string.Join(", ", _userProfile.Themes)}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lyrics analysis error: {ex.Message}");
                _profileAnalyzed = true; // Не повторяем при ошибке
            }
        }

        /// <summary>
        /// Загружает рекомендации: Groq AI (с профилем) → SoundCloud /me/recommendations → поиск по жанрам.
        /// </summary>
        public async Task<List<Track>> GetRecommendationsAsync()
        {
            // Способ 1: Groq AI — анализирует лайки + профиль и предлагает похожие треки
            if (_groq.IsConfigured && _cachedLikedTracks.Count > 0)
            {
                var aiRecs = await GetAiRecommendationsAsync();
                if (aiRecs.Count > 0)
                    return aiRecs;
            }

            // Способ 2: Официальные рекомендации SoundCloud
            if (_soundcloud.IsLoggedIn)
            {
                var officialRecs = await _soundcloud.GetRecommendationsAsync();
                if (officialRecs.Count > 0)
                {
                    return FilterOutLiked(officialRecs);
                }
            }

            // Способ 3: Поиск по топ-жанрам
            if (_topGenres.Count > 0)
            {
                return await GetGenreRecommendationsAsync();
            }

            return new List<Track>();
        }

        /// <summary>
        /// Получает рекомендации от Groq AI и ищет предложенные треки на SoundCloud.
        /// Передаёт профиль настроений/языков если доступен.
        /// </summary>
        private async Task<List<Track>> GetAiRecommendationsAsync()
        {
            var results = new List<Track>();
            var seenIds = new HashSet<string>();

            try
            {
                var recommendations = await _groq.GetRecommendationsAsync(_cachedLikedTracks, _userProfile);
                if (recommendations.Count == 0) return results;

                // Ищем каждый рекомендованный трек на SoundCloud
                foreach (var rec in recommendations)
                {
                    try
                    {
                        var query = $"{rec.Artist} {rec.Title}";
                        var found = await _soundcloud.SearchAsync(query, 5);

                        foreach (var track in found)
                        {
                            if (seenIds.Contains(track.VideoId)) continue;
                            if (_likedTrackIds.Contains(track.VideoId)) continue;

                            seenIds.Add(track.VideoId);
                            // Добавляем причину рекомендации в жанр
                            if (!string.IsNullOrEmpty(rec.Reason))
                                track.Genre = rec.Reason;

                            results.Add(track);
                            break; // Берём первый найденный результат для каждого запроса
                        }
                    }
                    catch { }

                    if (results.Count >= 30) break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AI recommendations error: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Ищет треки по топ-жанрам пользователя.
        /// </summary>
        public async Task<List<Track>> GetGenreRecommendationsAsync()
        {
            var results = new List<Track>();
            var seenIds = new HashSet<string>();

            // Ищем по каждому топ-жанру, берём по несколько треков
            var perGenre = Math.Max(6, 30 / _topGenres.Count);

            foreach (var genre in _topGenres)
            {
                try
                {
                    var tracks = await _soundcloud.SearchByGenreAsync(genre, perGenre + 5);
                    foreach (var track in tracks)
                    {
                        if (seenIds.Contains(track.VideoId)) continue;
                        if (_likedTrackIds.Contains(track.VideoId)) continue;

                        seenIds.Add(track.VideoId);
                        results.Add(track);

                        if (results.Count >= 30) break;
                    }
                }
                catch { }

                if (results.Count >= 30) break;
            }

            // Перемешиваем для разнообразия
            Shuffle(results);
            return results;
        }

        /// <summary>
        /// Фильтрует уже лайкнутые треки.
        /// </summary>
        private List<Track> FilterOutLiked(List<Track> tracks)
        {
            return tracks.Where(t => !_likedTrackIds.Contains(t.VideoId)).ToList();
        }

        /// <summary>
        /// Нормализация названия жанра — группирует похожие.
        /// </summary>
        private static string NormalizeGenre(string genre)
        {
            if (string.IsNullOrEmpty(genre)) return "";

            // Маппинг поджанров к основным жанрам
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Electronic
                { "techno", "Techno" },
                { "deep techno", "Techno" },
                { "minimal techno", "Techno" },
                { "detroit techno", "Techno" },
                { "hard techno", "Techno" },
                { "tech house", "Tech House" },
                { "deep house", "Deep House" },
                { "house", "House" },
                { "progressive house", "House" },
                { "future house", "House" },
                { "bass house", "House" },
                { "afro house", "House" },
                { "melodic techno", "Melodic Techno" },
                { "melodic house", "Melodic Techno" },
                { "trance", "Trance" },
                { "psytrance", "Trance" },
                { "progressive trance", "Trance" },
                { "dubstep", "Dubstep" },
                { "riddim", "Dubstep" },
                { "drum and bass", "Drum & Bass" },
                { "dnb", "Drum & Bass" },
                { "d&b", "Drum & Bass" },
                { "liquid drum and bass", "Drum & Bass" },
                { "edm", "EDM" },
                { "electronic", "Electronic" },
                { "electro", "Electronic" },
                { "idm", "Electronic" },
                { "ambient", "Ambient" },
                { "downtempo", "Ambient" },
                // Hip-Hop
                { "hip-hop", "Hip-Hop" },
                { "hip hop", "Hip-Hop" },
                { "rap", "Hip-Hop" },
                { "trap", "Trap" },
                { "drill", "Trap" },
                { "lo-fi hip hop", "Lo-Fi" },
                { "lofi hip hop", "Lo-Fi" },
                { "lo-fi", "Lo-Fi" },
                { "lofi", "Lo-Fi" },
                // Pop
                { "pop", "Pop" },
                { "indie pop", "Indie Pop" },
                { "synthpop", "Pop" },
                { "synth pop", "Pop" },
                // Rock
                { "rock", "Rock" },
                { "indie rock", "Indie Rock" },
                { "alternative", "Alternative" },
                { "alt rock", "Alternative" },
                { "post-punk", "Alternative" },
                { "metal", "Metal" },
                // R&B
                { "r&b", "R&B" },
                { "rnb", "R&B" },
                { "soul", "R&B" },
                { "neo soul", "R&B" },
                // Latin
                { "reggaeton", "Latin" },
                { "latin", "Latin" },
                { "latin trap", "Latin" },
                { "bachata", "Latin" },
                // Other
                { "jazz", "Jazz" },
                { "classical", "Classical" },
                { "folk", "Folk" },
                { "acoustic", "Acoustic" },
                { "reggae", "Reggae" },
                { "dancehall", "Reggae" },
            };

            // Сначала проверяем точное совпадение
            if (mappings.TryGetValue(genre, out var mapped))
                return mapped;

            // Потом проверяем содержит ли название жанра ключ из маппинга
            foreach (var kvp in mappings)
            {
                if (genre.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            // Если не нашли — возвращаем с заглавной буквы
            if (genre.Length > 0)
                return char.ToUpper(genre[0]) + genre[1..];

            return genre;
        }

        private static void Shuffle<T>(List<T> list)
        {
            var rng = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
