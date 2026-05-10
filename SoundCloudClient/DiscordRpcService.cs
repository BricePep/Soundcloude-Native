using DiscordRPC;
using DiscordRPC.Logging;

namespace SoundCloudClient
{
    public class DiscordRpcService : IDisposable
    {
        private const string CLIENT_ID = "1500177770519597139";

        private DiscordRpcClient? _client;
        private string? _currentTrackKey; // уникальный ключ трека для избежания дублирующих обновлений

        public bool IsConnected => _client?.IsInitialized ?? false;

        public void Connect()
        {
            if (_client?.IsInitialized == true) return;

            _client = new DiscordRpcClient(CLIENT_ID)
            {
                Logger = new ConsoleLogger() { Level = LogLevel.Warning }
            };

            _client.OnReady += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Discord] Ready: {e.User.Username}");
            };

            _client.OnError += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Discord] Error: {e.Message}");
            };

            _client.OnClose += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Discord] Close: {e.Reason}");
            };

            _client.Initialize();
        }

        /// <summary>
        /// Установить активность "Listening" для играющего трека.
        /// Использует start/end timestamps — Discord сам рисует прогресс-бар.
        /// НЕ нужно обновлять каждую секунду!
        /// </summary>
        public void SetListening(string title, string artist, int durationSecs, int elapsedSecs,
            string? artworkUrl = null, string? trackUrl = null)
        {
            if (_client?.IsInitialized != true) return;

            var key = $"{title}|{artist}|{elapsedSecs}";
            if (key == _currentTrackKey) return; // не обновляем если ничего не поменялось
            _currentTrackKey = key;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var start = now - elapsedSecs;
            var end = start + durationSecs;

            var presence = new RichPresence()
            {
                Type = ActivityType.Listening,
                Details = Truncate(title, 128),
                State = Truncate(artist, 128),
                Timestamps = new Timestamps()
                {
                    StartUnixMilliseconds = (ulong)(start * 1000),
                    EndUnixMilliseconds = (ulong)(end * 1000)
                },
                Assets = new Assets()
                {
                    LargeImageKey = string.IsNullOrEmpty(artworkUrl) ? "soundcloud_logo" : artworkUrl,
                }
            };

            if (!string.IsNullOrEmpty(trackUrl))
            {
                presence.Buttons = new Button[]
                {
                    new Button() { Label = "Listen on SoundCloud", Url = trackUrl }
                };
            }

            _client.SetPresence(presence);
        }

        /// <summary>
        /// Обновить прогресс при перемотке (seek).
        /// Пересчитывает start/end timestamps.
        /// </summary>
        public void SeekTo(int elapsedSecs, int durationSecs)
        {
            if (_client?.IsInitialized != true) return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var start = now - elapsedSecs;
            var end = start + durationSecs;

            // Получаем текущую presence и обновляем только timestamps
            var current = _client.CurrentPresence;
            if (current == null) return;

            var presence = current.Clone();
            presence.Timestamps = new Timestamps()
            {
                StartUnixMilliseconds = (ulong)(start * 1000),
                EndUnixMilliseconds = (ulong)(end * 1000)
            };

            _currentTrackKey = null; // сбрасываем ключ чтобы обновление прошло
            _client.SetPresence(presence);
        }

        /// <summary>
        /// Пауза — убираем timestamps (прогресс-бар исчезает), меняем state на "Paused".
        /// </summary>
        public void Pause()
        {
            if (_client?.IsInitialized != true) return;

            var current = _client.CurrentPresence;
            if (current == null) return;

            var presence = current.Clone();
            presence.State = "Paused";
            presence.Timestamps = null; // без timestamps — нет прогресс-бара

            _currentTrackKey = null;
            _client.SetPresence(presence);
        }

        /// <summary>
        /// Resume — восстанавливаем timestamps с текущей позиции.
        /// </summary>
        public void Resume(int elapsedSecs, int durationSecs)
        {
            if (_client?.IsInitialized != true) return;

            var current = _client.CurrentPresence;
            if (current == null) return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var start = now - elapsedSecs;
            var end = start + durationSecs;

            var presence = current.Clone();
            presence.State = current.Details; // восстанавливаем artist в state
            presence.Timestamps = new Timestamps()
            {
                StartUnixMilliseconds = (ulong)(start * 1000),
                EndUnixMilliseconds = (ulong)(end * 1000)
            };

            _currentTrackKey = null;
            _client.SetPresence(presence);
        }

        /// <summary>
        /// Очистить активность (стоп, выход).
        /// </summary>
        public void Clear()
        {
            if (_client?.IsInitialized != true) return;
            _client.ClearPresence();
            _currentTrackKey = null;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= maxLen ? s : s.Substring(0, maxLen - 1) + "…";
        }

        public void Dispose()
        {
            _client?.ClearPresence();
            _client?.Dispose();
            _client = null;
        }
    }
}
