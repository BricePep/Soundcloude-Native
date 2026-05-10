using NAudio.Wave;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Interop;

namespace SoundCloudClient
{
    // ═══ DWM Mica / Acrylic P/Invoke ═══
    internal static class DwmApi
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Windows 11 22H2+ (preferred)
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        // Windows 11 21H2 (fallback)
        private const int DWMWA_MICA_EFFECT = 1029;
        // Dark mode for title bar
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private const int SB_AUTO = 0;    // Let DWM decide
        private const int SB_NONE = 1;    // No backdrop
        private const int SB_MICA = 2;    // Mica
        private const int SB_ACRYLIC = 3;  // Acrylic
        private const int SB_TABBED = 4;  // Tabbed (Mica Alt)

        public static bool EnableMica(IntPtr hwnd, bool darkMode = true)
        {
            if (hwnd == IntPtr.Zero) return false;

            // Tell DWM this is a dark window (so Mica tint is dark)
            int dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            // Try Windows 11 22H2+ API first
            int backdrop = SB_MICA;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            if (hr == 0) return true;

            // Fallback: Windows 11 21H2
            int mica = 1;
            hr = DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref mica, sizeof(int));
            return hr == 0;
        }

        public static bool EnableAcrylic(IntPtr hwnd, bool darkMode = true)
        {
            if (hwnd == IntPtr.Zero) return false;

            int dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int backdrop = SB_ACRYLIC;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            return hr == 0;
        }
    }

    // Конвертер инверсии bool для XAML
    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : true;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : false;
    }

    // Конвертер для оранжевой полоски прогресса слайдера: (Value, Maximum, ActualWidth) -> Width
    public class SliderProgressWidthConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return 0.0;
            if (values[0] is not double val || values[1] is not double max || values[2] is not double width) return 0.0;
            if (max <= 0 || width <= 0 || double.IsNaN(width)) return 0.0;
            var w = width * (val / max);
            if (w < 0) w = 0;
            if (w > width) w = width;
            return w;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // Конвертер URL -> BitmapImage с кэшированием
    public class ArtworkConverter : IValueConverter
    {
        private static readonly HttpClient _http = new();
        private static readonly Dictionary<string, BitmapImage> _cache = new();

        static ArtworkConverter()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var url = value as string;
            if (string.IsNullOrEmpty(url)) return null;

            // Проверяем кэш
            if (_cache.TryGetValue(url, out var cached))
                return cached;

            // Загружаем асинхронно
            _ = LoadAndSetAsync(url);
            return null; // Пока грузится — ничего не показываем
        }

        private async Task LoadAndSetAsync(string url)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                _cache[url] = bmp;

                // Уведомляем Binding об изменении
                BindingOperations.EnableCollectionSynchronization(_cache, _cache);
            }
            catch { }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
    public static class StringExtensions
    {
        public static string ReplaceInvalidFileNameChars(this string name, char replacement = '_')
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return string.Join(replacement, name.Split(invalid));
        }
    }

    public class Track : INotifyPropertyChanged
    {
        public string VideoId { get; set; } = "";
        public string Title { get; set; } = "";
        public string ArtistName { get; set; } = "";
        public string ArtworkUrl { get; set; } = "";
        public string Genre { get; set; } = "";
        public int DurationSeconds { get; set; }
        public bool IsPlaylist { get; set; }
        public bool IsSectionHeader { get; set; } // заголовок секции (разделитель)
        public string? LocalFilePath { get; set; } // путь к локальному файлу

        [Newtonsoft.Json.JsonIgnore]
        public string DurationFormatted => DurationSeconds >= 3600
            ? TimeSpan.FromSeconds(DurationSeconds).ToString(@"h\:mm\:ss")
            : TimeSpan.FromSeconds(DurationSeconds).ToString(@"m\:ss");

        [Newtonsoft.Json.JsonIgnore]
        private BitmapImage? _artwork;

        [Newtonsoft.Json.JsonIgnore]
        public BitmapImage? Artwork
        {
            get => _artwork;
            set { _artwork = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Artwork))); }
        }

        [Newtonsoft.Json.JsonIgnore]
        private bool _isLiked;
        [Newtonsoft.Json.JsonIgnore]
        public bool IsLiked
        {
            get => _isLiked;
            set { _isLiked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLiked))); }
        }

        [Newtonsoft.Json.JsonIgnore]
        private bool _isCurrentlyPlaying;
        [Newtonsoft.Json.JsonIgnore]
        public bool IsCurrentlyPlaying
        {
            get => _isCurrentlyPlaying;
            set { _isCurrentlyPlaying = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrentlyPlaying))); }
        }

        [Newtonsoft.Json.JsonIgnore]
        private bool _isInPlaylist;
        [Newtonsoft.Json.JsonIgnore]
        public bool IsInPlaylist
        {
            get => _isInPlaylist;
            set { _isInPlaylist = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInPlaylist))); }
        }

        [Newtonsoft.Json.JsonIgnore]
        private bool _isPaused;
        [Newtonsoft.Json.JsonIgnore]
        public bool IsPaused
        {
            get => _isPaused;
            set { _isPaused = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        // P/Invoke для получения рабочей области текущего монитора
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        /// <summary>
        /// Возвращает рабочую область монитора, на котором находится окно (учитывая панель задач).
        /// В отличие от SystemParameters.WorkArea, работает для любого монитора.
        /// </summary>
        private Rect GetCurrentMonitorWorkArea()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(monitor, ref info);
            return new Rect(info.WorkArea.Left, info.WorkArea.Top,
                info.WorkArea.Right - info.WorkArea.Left,
                info.WorkArea.Bottom - info.WorkArea.Top);
        }

        // Команды для кнопок панели задач
        public static readonly RoutedCommand TaskbarPrevCommand = new();
        public static readonly RoutedCommand TaskbarPlayPauseCommand = new();
        public static readonly RoutedCommand TaskbarNextCommand = new();

        private readonly SoundCloudService _soundcloud = new();
        private readonly LocalLibrary _library = new();
        private readonly GroqService _groqService = new();
        private readonly RecommendationService _recommendationService;
        private readonly DiscordRpcService _discord = new();
        private readonly ObservableCollection<Track> _tracks = new();
        private static readonly HttpClient _imageClient = new();
        private IWavePlayer? _wavePlayer;
        private MediaFoundationReader? _audioReader;
        private EqSampleProvider? _eqProvider;
        private DispatcherTimer _progressTimer;
        private Track? _currentTrack;
        private bool _isPlaying = false;
        private bool _isDragging = false;
        private DispatcherTimer? _searchTimer;
        private List<Playlist> _cloudPlaylists = new();
        private string _currentNavSection = "home";
        private string? _currentPlaylistName = null; // имя открытого плейлиста (null = не внутри плейлиста)
        private List<Track> _playbackQueue = new(); // очередь воспроизведения (копия треков на момент старта)
        private int _playbackQueueIndex = -1; // индекс текущего трека в очереди
        private bool _isAutoNext = false; // флаг: автопереход из очереди, не обновлять очередь в PlayTrack
        private int _playTrackId = 0; // ID текущего вызова PlayTrack, чтобы отменять старые
        private bool _menuClickSuppressPlay = false; // флаг: клик по ⋯ не должен запускать трек
        private bool _suppressSelectionPlay = false; // флаг: восстановление из настроек не должно запускать трек
        private List<Track> _cachedLikedTracksForHome = new();
        private string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox", "settings.json");
        private bool _sidebarCollapsed = false;
        private float[] _eqGains = new float[10]; // настройки эквалайзера (-12..+12 dB)
        private bool _discordEnabled = true;
        private string? _settingsBgImagePath = null;
        private bool _settingsRemoveBg = false;
        private bool _repeatEnabled = false; // повтор текущего трека
        private bool _shuffleEnabled = false; // перемешивание очереди
        private bool _animationsEnabled = true; // анимации в UI
        private int _navLoadId = 0; // ID загрузки навигации (для отмены при быстром переключении)

        // Тексты (встроенная панель)
        private readonly LyricsService _lyricsService = new();
        private readonly LyricsCacheService _lyricsCache = new();
        private List<(TimeSpan Time, string Text)> _lyricsLines = new();
        private List<TextBlock> _lyricsBlocks = new();
        private int _lyricsCurrentLine = -1;
        private bool _lyricsSynced = false;
        private DispatcherTimer? _lyricsSyncTimer;

        public MainWindow()
        {
            InitializeComponent();
            _recommendationService = new RecommendationService(_soundcloud, _groqService);
            TracksList.ItemsSource = _tracks;

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _progressTimer.Tick += ProgressTimer_Tick;

            // Команды для кнопок панели задач
            CommandBindings.Add(new CommandBinding(TaskbarPrevCommand, (s, e) => PrevButton_Click(s, null)));
            CommandBindings.Add(new CommandBinding(TaskbarPlayPauseCommand, (s, e) => PlayPauseButton_Click(s, null)));
            CommandBindings.Add(new CommandBinding(TaskbarNextCommand, (s, e) => NextButton_Click(s, null)));

            // Иконки для кнопок панели задач (DrawingImage из геометрий)
            var iconBrush = new SolidColorBrush(Colors.White);
            TaskbarPrev.ImageSource = new DrawingImage(new GeometryDrawing(iconBrush, null, Geometry.Parse("M6,4 L6,20 M6,12 L18,4 L18,20 Z")));
            TaskbarPlayPause.ImageSource = new DrawingImage(new GeometryDrawing(iconBrush, null, Geometry.Parse("M6,4 L6,20 L20,12 Z"))); // Play
            TaskbarNext.ImageSource = new DrawingImage(new GeometryDrawing(iconBrush, null, Geometry.Parse("M18,4 L18,20 M18,12 L6,4 L6,20 Z")));

            UpdateGreeting();
            UpdateNavHighlight();
            LoadSettings();
            LoadBackgroundImage();

            try { _discord.Connect(); } catch { _discordEnabled = false; }

            // Включаем Mica-фон (Windows 11)
            Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                DwmApi.EnableMica(hwnd, true);

                // Анимация появления приветствия
                AnimateFadeIn(GreetingText, 0, 300);
                AnimateFadeIn(GreetingSub, 100, 300);
            };
        }

        static MainWindow()
        {
            _imageClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        private async Task<BitmapImage?> LoadImageAsync(string url)
        {
            try
            {
                var bytes = await _imageClient.GetByteArrayAsync(url);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private void AddTrack(Track track)
        {
            // Обновляем статус лайка
            if (!track.IsSectionHeader && !track.IsPlaylist)
            {
                try { track.IsLiked = _library.IsLiked(track.VideoId); } catch { }
                // Подсветка играющего трека при повторном входе в плейлист
                if (_currentTrack != null && track.VideoId == _currentTrack.VideoId)
                    track.IsCurrentlyPlaying = true;
            }
            _tracks.Add(track);
            LoadTrackArtwork(track);
        }

        private void AddTracks(IEnumerable<Track> tracks)
        {
            foreach (var track in tracks)
                AddTrack(track);
        }

        private void LoadTrackArtwork(Track track)
        {
            if (string.IsNullOrEmpty(track.ArtworkUrl)) return;
            _ = Task.Run(async () =>
            {
                var bmp = await LoadImageAsync(track.ArtworkUrl);
                if (bmp != null)
                {
                    await Dispatcher.InvokeAsync(() => track.Artwork = bmp);
                }
            });
        }

        private void UpdateGreeting()
        {
            var hour = DateTime.Now.Hour;
            var greeting = hour switch
            {
                >= 5 and < 12 => "Доброе утро",
                >= 12 and < 18 => "Добрый день",
                >= 18 and < 23 => "Добрый вечер",
                _ => "Доброй ночи"
            };
            GreetingText.Text = greeting;

            try
            {
                var ru = new System.Globalization.CultureInfo("ru-RU");
                var dateStr = DateTime.Now.ToString("dddd, d MMMM", ru);
                if (dateStr.Length > 0) dateStr = char.ToUpper(dateStr[0], ru) + dateStr.Substring(1);
                GreetingSub.Text = dateStr;
            }
            catch { GreetingSub.Text = string.Empty; }
        }

        // Анимация плавного появления элемента (fade-in + slide-up)
        private void AnimateFadeIn(FrameworkElement element, int delayMs, int durationMs)
        {
            if (!_animationsEnabled) return;
            element.Opacity = 0;
            var trans = new TranslateTransform { Y = 8 };
            element.RenderTransform = trans;

            var sb = new Storyboard();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, element);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slide, trans);
            Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.YProperty));
            sb.Children.Add(slide);

            sb.Begin();
        }

        private static string FormatPlaylistDuration(int totalSeconds)
        {
            if (totalSeconds <= 0) return "";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }

        private void UpdateNavHighlight()
        {
            var navButtons = new[] { NavHome, NavSearch, NavLikes, NavLibrary, NavSettings };
            string activeSection = _currentNavSection;

            foreach (var btn in navButtons)
            {
                bool isActive = (btn == NavHome && activeSection == "home") ||
                                (btn == NavSearch && activeSection == "search") ||
                                (btn == NavLikes && activeSection == "likes") ||
                                (btn == NavLibrary && activeSection == "library") ||
                                (btn == NavSettings && activeSection == "settings");

                btn.Tag = isActive ? "Active" : null;
            }

            // Кнопка "Создать плейлист" и "Добавить музыку" — только на странице Библиотеки
            if (CreatePlaylistTopBtn != null)
                CreatePlaylistTopBtn.Visibility = activeSection == "library" ? Visibility.Visible : Visibility.Collapsed;
            if (AddLocalMusicBtn != null)
                AddLocalMusicBtn.Visibility = activeSection == "library" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NavHome_Click(object sender, RoutedEventArgs e)
        {
            HideLyricsPanel();
            HideSettingsPanel();
            _currentNavSection = "home";
            _currentPlaylistName = null;
            UpdateNavHighlight();
            _ = LoadHomeAsync(++_navLoadId);
        }

        private async Task LoadHomeAsync(int loadId)
        {
            _tracks.Clear();
            if (loadId != _navLoadId) return; // пользователь уже ушёл на другую вкладку

            // Собираем все треки (лайки + плейлисты) для анализа
            var allTracks = new List<Track>();

            if (_soundcloud.IsLoggedIn)
            {
                GreetingText.Text = "Загружаю рекомендации...";

                // Облачные лайки
                if (_cachedLikedTracksForHome.Count == 0)
                {
                    var (likes, _) = await _soundcloud.GetUserLikesAsync();
                    if (loadId != _navLoadId) return;
                    if (likes.Count > 0)
                        _cachedLikedTracksForHome = likes;
                }

                allTracks.AddRange(_cachedLikedTracksForHome);

                // Треки из облачных плейлистов
                if (_cloudPlaylists.Count == 0)
                {
                    var (playlists, _) = await _soundcloud.GetUserPlaylistsAsync();
                    if (loadId != _navLoadId) return;
                    if (playlists.Count > 0)
                        _cloudPlaylists = playlists;
                }

                foreach (var pl in _cloudPlaylists)
                    allTracks.AddRange(pl.Tracks);
            }

            // Локальные лайки
            var localLikes = _library.GetLikes();
            if (localLikes.Count > 0)
            {
                var existingIds = new HashSet<string>(allTracks.Select(t => t.VideoId));
                foreach (var t in localLikes)
                    if (!existingIds.Contains(t.VideoId))
                        allTracks.Add(t);
            }

            // Треки из локальных плейлистов
            var localPlaylists = _library.GetPlaylists();
            foreach (var pl in localPlaylists)
            {
                var existingIds = new HashSet<string>(allTracks.Select(t => t.VideoId));
                foreach (var t in pl.Tracks)
                    if (!existingIds.Contains(t.VideoId))
                        allTracks.Add(t);
            }

            if (allTracks.Count > 0)
            {
                _recommendationService.AnalyzeGenres(allTracks);
                SaveSettings();

                // Анализ текстов в фоне (не блокирует загрузку рекомендаций)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _recommendationService.AnalyzeLyricsAsync();
                        // Сохраняем профиль после анализа
                        Dispatcher.Invoke(() => SaveSettings());
                    }
                    catch { }
                });

                var recommendations = await _recommendationService.GetRecommendationsAsync();
                if (loadId != _navLoadId) return;

                // Восстанавливаем приветствие
                UpdateGreeting();

                if (recommendations.Count > 0)
                {
                    var genres = _recommendationService.GetTopGenres();
                    if (genres.Count > 0)
                        AddTrack(new Track { IsSectionHeader = true, Title = "Ваши жанры: " + string.Join(" • ", genres) });

                    foreach (var track in recommendations)
                        AddTrack(track);
                }
            }

            // SoundCloud миксы (официальные рекомендации)
            if (_soundcloud.IsLoggedIn)
            {
                try
                {
                    var scRecs = await _soundcloud.GetRecommendationsAsync();
                    if (loadId != _navLoadId) return;

                    if (scRecs.Count > 0)
                    {
                        AddTrack(new Track { IsSectionHeader = true, Title = "SoundCloud миксы для вас" });
                        foreach (var track in scRecs.Take(20))
                            AddTrack(track);
                    }
                }
                catch { /* не критично */ }
            }

            if (_tracks.Count == 0)
            {
                UpdateGreeting();
                if (_soundcloud.IsLoggedIn || localLikes.Count > 0)
                    AddTrack(new Track { IsSectionHeader = true, Title = "Не удалось подобрать рекомендации. Попробуйте позже." });
                else
                    AddTrack(new Track { IsSectionHeader = true, Title = "Войдите в аккаунт для персональных рекомендаций" });
            }
            else
            {
                UpdateGreeting();
            }
        }

        private void NavSearch_Click(object sender, RoutedEventArgs e)
        {
            HideLyricsPanel();
            HideSettingsPanel();
            _currentNavSection = "search";
            _currentPlaylistName = null;
            UpdateNavHighlight();
            SearchBox.Focus();
        }

        private void NavLikes_Click(object sender, RoutedEventArgs e)
        {
            HideLyricsPanel();
            HideSettingsPanel();
            _currentNavSection = "likes";
            _currentPlaylistName = null;
            UpdateNavHighlight();
            GreetingText.Text = "Лайки";

            if (_soundcloud.IsLoggedIn)
            {
                // Показываем облачные лайки
                _ = LoadCloudLikesAsync(++_navLoadId);
            }
            else
            {
                LoadLikes();
            }
        }

        private async Task LoadCloudLikesAsync(int loadId)
        {
            _tracks.Clear();
            var (likes, error) = await _soundcloud.GetUserLikesAsync();
            if (loadId != _navLoadId) return;
            if (!string.IsNullOrEmpty(error))
            {
                // Если ошибка — показываем только локальные
                LoadLikes();
                return;
            }

            // Облачные лайки
            foreach (var track in likes)
                AddTrack(track);

            // Анализируем жанры из облачных лайков для рекомендаций
            if (likes.Count > 0)
            {
                _cachedLikedTracksForHome = likes;
                _recommendationService.AnalyzeGenres(likes);
                SaveSettings();

                // Анализ текстов в фоне
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _recommendationService.AnalyzeLyricsAsync();
                        Dispatcher.Invoke(() => SaveSettings());
                    }
                    catch { }
                });
            }

            // Локальные лайки — после разделителя
            var localLikes = _library.GetLikes();
            if (localLikes.Count > 0)
            {
                // Убираем дубли (уже есть в облачных)
                var cloudIds = new HashSet<string>(likes.Select(t => t.VideoId));
                var uniqueLocal = localLikes.Where(t => !cloudIds.Contains(t.VideoId)).ToList();

                if (uniqueLocal.Count > 0)
                {
                    AddTrack(new Track
                    {
                        VideoId = "__section_local_likes__",
                        Title = "ЛОКАЛЬНЫЕ",
                        IsSectionHeader = true
                    });
                    foreach (var track in uniqueLocal)
                        AddTrack(track);
                }
            }
        }

        private void NavLibrary_Click(object sender, RoutedEventArgs e)
        {
            HideLyricsPanel();
            HideSettingsPanel();
            _currentNavSection = "library";
            _currentPlaylistName = null;
            UpdateNavHighlight();
            GreetingText.Text = "Библиотека";

            if (_soundcloud.IsLoggedIn)
            {
                // Показываем облачные плейлисты
                _ = LoadCloudPlaylistsAsync(++_navLoadId);
            }
            else
            {
                LoadPlaylists();
            }
        }

        private async Task LoadCloudPlaylistsAsync(int loadId)
        {
            _tracks.Clear();
            var (playlists, error) = await _soundcloud.GetUserPlaylistsAsync();
            if (loadId != _navLoadId) return;
            if (!string.IsNullOrEmpty(error))
            {
                LoadPlaylists();
                return;
            }
            if (playlists.Count == 0)
            {
                LoadPlaylists();
                return;
            }

            _cloudPlaylists = playlists;

            foreach (var playlist in playlists)
            {
                var totalSec = playlist.Tracks.Sum(t => t.DurationSeconds);
                AddTrack(new Track
                {
                    VideoId = playlist.Name,
                    Title = playlist.Name,
                    ArtistName = $"{playlist.Tracks.Count} треков • {FormatPlaylistDuration(totalSec)}",
                    ArtworkUrl = playlist.Tracks.FirstOrDefault()?.ArtworkUrl ?? "",
                    DurationSeconds = totalSec,
                    IsPlaylist = true
                });
            }

            // Добавляем локальные плейлисты тоже
            var localPlaylists = _library.GetPlaylists();
            if (localPlaylists.Count > 0)
            {
                // Разделитель между облачными и локальными
                AddTrack(new Track
                {
                    VideoId = "__section_local__",
                    Title = "ЛОКАЛЬНЫЕ",
                    IsSectionHeader = true
                });

                foreach (var playlist in localPlaylists)
                {
                    // Не дублируем если уже есть с таким именем
                    if (!_tracks.Any(t => t.Title == playlist.Name))
                    {
                        var totalSec = playlist.Tracks.Sum(t => t.DurationSeconds);
                        AddTrack(new Track
                        {
                            VideoId = playlist.Name,
                            Title = playlist.Name,
                            ArtistName = $"{playlist.Tracks.Count} треков (локальный) • {FormatPlaylistDuration(totalSec)}",
                            ArtworkUrl = playlist.Tracks.FirstOrDefault()?.ArtworkUrl ?? "",
                            DurationSeconds = totalSec,
                            IsPlaylist = true
                        });
                    }
                }
            }
        }

        // Сворачивание сайдбара
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarCollapsed = !_sidebarCollapsed;

            ToggleSidebarButton.ApplyTemplate();
            var icon = ToggleSidebarButton.Template.FindName("ToggleIcon", ToggleSidebarButton) as MaterialDesignThemes.Wpf.PackIcon;

            double targetWidth;
            if (_sidebarCollapsed)
            {
                targetWidth = 76;
                if (icon != null) icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ChevronRight;

                // Скрываем текст, оставляем только иконки
                UserDisplayName.Visibility = Visibility.Collapsed;
                UserNameText.Visibility = Visibility.Collapsed;
                SetSidebarTextVisibility(Visibility.Collapsed);
            }
            else
            {
                targetWidth = 220;
                if (icon != null) icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ChevronLeft;

                // Показываем текст обратно
                UserDisplayName.Visibility = Visibility.Visible;
                if (_soundcloud.IsLoggedIn) UserNameText.Visibility = Visibility.Visible;
                SetSidebarTextVisibility(Visibility.Visible);
            }

            if (_animationsEnabled)
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = SidebarBorder.ActualWidth,
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
                };
                SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
            }
            else
            {
                SidebarBorder.Width = targetWidth;
            }
        }

        private void SetSidebarTextVisibility(Visibility visibility)
        {
            bool collapsed = visibility == Visibility.Collapsed;

            // Заголовки секций — скрываем при сворачивании
            MenuHeader.Visibility = visibility;
            // Bug report — текст и подпись кнопки прячем
            if (BugReportTextWrap != null) BugReportTextWrap.Visibility = visibility;
            if (BugReportButton != null)
            {
                BugReportButton.ApplyTemplate();
                var lbl = BugReportButton.Template.FindName("BugReportLabel", BugReportButton) as TextBlock;
                var icn = BugReportButton.Template.FindName("BugReportIcon", BugReportButton) as MaterialDesignThemes.Wpf.PackIcon;
                if (lbl != null) lbl.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                if (icn != null) icn.Margin = collapsed ? new Thickness(0) : new Thickness(0, 0, 5, 0);
            }

            // Имя пользователя
            UserDisplayName.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

            // Кнопка переключения сайдбара — при свёрнутом состоянии перенесём её под аватар
            // (она находится в третьей колонке Grid профиля)
            // Аватар центрируем
            if (collapsed)
            {
                // Уменьшаем отступы профиля чтобы и аватар и кнопка-стрелка влезли в 76px
                AvatarBorder.Visibility = Visibility.Visible;
                AvatarBorder.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleSidebarButton.HorizontalAlignment = HorizontalAlignment.Right;
                ProfileGrid.Margin = new Thickness(6, 18, 6, 12);
            }
            else
            {
                AvatarBorder.Visibility = Visibility.Visible;
                AvatarBorder.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleSidebarButton.HorizontalAlignment = HorizontalAlignment.Right;
                ProfileGrid.Margin = new Thickness(14, 18, 14, 12);
            }

            // Все навигационные кнопки: меняем содержимое
            // При collapsed — Grid с одной колонкой *, иконка по центру
            // При развёрнутом — Grid с колонками 28/*, иконка слева, текст справа
            var allNavButtons = new (Button btn, string text)[]
            {
                (NavHome, "Главная"),
                (NavSearch, "Поиск"),
                (NavLikes, "Лайки"),
                (NavLibrary, "Библиотека"),
                (NavSettings, "Настройки"),
            };
            foreach (var (btn, text) in allNavButtons)
            {
                if (btn.Content is Grid grid)
                {
                    if (collapsed)
                    {
                        grid.Margin = new Thickness(0);
                        if (grid.ColumnDefinitions.Count >= 2)
                        {
                            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                            grid.ColumnDefinitions[1].Width = new GridLength(0);
                        }
                        foreach (var child in grid.Children)
                        {
                            if (child is TextBlock tb) tb.Visibility = Visibility.Collapsed;
                            if (child is MaterialDesignThemes.Wpf.PackIcon icon)
                            {
                                icon.HorizontalAlignment = HorizontalAlignment.Center;
                            }
                        }
                    }
                    else
                    {
                        grid.Margin = new Thickness(14, 0, 0, 0);
                        if (grid.ColumnDefinitions.Count >= 2)
                        {
                            grid.ColumnDefinitions[0].Width = new GridLength(28);
                            grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                        }
                        foreach (var child in grid.Children)
                        {
                            if (child is TextBlock tb) tb.Visibility = Visibility.Visible;
                            if (child is MaterialDesignThemes.Wpf.PackIcon icon)
                            {
                                icon.HorizontalAlignment = HorizontalAlignment.Left;
                            }
                        }
                    }
                }
            }

        }

        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = new JObject();
                json["oauth_token"] = _soundcloud.IsLoggedIn ? _soundcloud.GetOAuthToken() : "";
                json["groq_api_key"] = _groqService.GetApiKey() ?? "";

                if (GradientOverlay?.Background is LinearGradientBrush brush)
                {
                    json["color1"] = brush.GradientStops.Count > 0 ? brush.GradientStops[0].Color.ToString() : "";
                    json["color2"] = brush.GradientStops.Count > 1 ? brush.GradientStops[1].Color.ToString() : "";
                    json["color3"] = brush.GradientStops.Count > 2 ? brush.GradientStops[2].Color.ToString() : "";
                }

                // Громкость
                json["volume"] = VolumeSlider.Value;

                // Последний трек
                if (_currentTrack != null)
                {
                    var trackObj = new JObject();
                    trackObj["video_id"] = _currentTrack.VideoId;
                    trackObj["title"] = _currentTrack.Title;
                    trackObj["artist"] = _currentTrack.ArtistName;
                    trackObj["artwork_url"] = _currentTrack.ArtworkUrl;
                    trackObj["duration"] = _currentTrack.DurationSeconds;
                    json["last_track"] = trackObj;

                    // Контекст: где играл трек (секция навигации, плейлист, индекс в списке)
                    json["nav_section"] = _currentNavSection;
                    if (!string.IsNullOrEmpty(_currentPlaylistName))
                        json["playlist_name"] = _currentPlaylistName;

                    var trackIndex = _tracks.IndexOf(_currentTrack);
                    if (trackIndex >= 0)
                        json["track_index"] = trackIndex;
                }

                // Эквалайзер
                var eqArr = new JArray();
                foreach (var g in _eqGains)
                    eqArr.Add(Math.Round(g, 1));
                json["eq_gains"] = eqArr;

                // Топ-жанры для рекомендаций
                var topGenres = _recommendationService.GetTopGenres();
                if (topGenres.Count > 0)
                {
                    var genresArr = new JArray();
                    foreach (var g in topGenres)
                        genresArr.Add(g);
                    json["top_genres"] = genresArr;
                }

                // Discord RPC
                json["discord_enabled"] = _discordEnabled;

                // Акцент темы и параметры фона
                json["accent"] = _pendingAccent;
                json["accent_light"] = _pendingAccentLight;
                json["bg_blur"] = _bgBlurRadius;
                json["bg_darken"] = _bgDarken;

                // Repeat / Shuffle
                json["repeat_enabled"] = _repeatEnabled;
                json["shuffle_enabled"] = _shuffleEnabled;

                // Анимации
                json["animations_enabled"] = _animationsEnabled;

                // Профиль пользователя (анализ текстов)
                var profile = _recommendationService.GetUserProfile();
                if (profile != null && profile.Moods.Count > 0)
                {
                    var profileObj = new JObject();
                    profileObj["moods"] = new JArray(profile.Moods);
                    profileObj["languages"] = new JArray(profile.Languages);
                    profileObj["energy"] = profile.Energy;
                    profileObj["themes"] = new JArray(profile.Themes);
                    profileObj["summary"] = profile.Summary;
                    json["user_profile"] = profileObj;
                }

                File.WriteAllText(_settingsPath, json.ToString());
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                // Первый запуск — спрашиваем про анимации
                if (!File.Exists(_settingsPath))
                {
                    var result = MessageBox.Show(
                        "Включить анимации интерфейса?\n\n" +
                        "Анимации делают интерфейс плавнее (переходы, пульсации, эффекты).\n" +
                        "Можно изменить позже в Настройках.\n\n" +
                        "Да — включить анимации\nНет — статичный интерфейс",
                        "SC Native — Первый запуск",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    _animationsEnabled = (result == MessageBoxResult.Yes);
                    SaveSettings(); // создаём settings.json
                    return;
                }

                var json = JObject.Parse(File.ReadAllText(_settingsPath));

                // Восстанавливаем токен
                var token = json["oauth_token"]?.ToString();
                if (!string.IsNullOrEmpty(token))
                {
                    _soundcloud.SetOAuthToken(token);
                    UserNameText.Text = "SoundCloud";
                    UserNameText.Visibility = Visibility.Visible;

                    // Загружаем профиль в фоне
                    _ = LoadUserProfileAsync();
                }
                UpdateLoginButtonAppearance();

                // Восстанавливаем Groq API ключ
                var groqKey = json["groq_api_key"]?.ToString();
                if (!string.IsNullOrEmpty(groqKey))
                    _groqService.SetApiKey(groqKey);

                // Восстанавливаем цветовую тему
                var c1 = json["color1"]?.ToString();
                var c2 = json["color2"]?.ToString();
                var c3 = json["color3"]?.ToString();

                if (!string.IsNullOrEmpty(c1) && !string.IsNullOrEmpty(c2) && !string.IsNullOrEmpty(c3))
                {
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new System.Windows.Point(0, 0);
                    brush.EndPoint = new System.Windows.Point(1, 1);
                    brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c1), 0));
                    brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c2), 0.5));
                    brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c3), 1));
                    GradientOverlay.Background = brush;
                }

                // Восстанавливаем громкость
                var volume = json["volume"]?.Value<double>();
                if (volume.HasValue)
                    VolumeSlider.Value = volume.Value;

                // Восстанавливаем контекст навигации (секция + плейлист) — асинхронно
                var navSection = json["nav_section"]?.ToString();
                var playlistName = json["playlist_name"]?.ToString();
                var savedTrackIndex = json["track_index"]?.Value<int>() ?? -1;

                if (!string.IsNullOrEmpty(navSection))
                {
                    _currentNavSection = navSection;
                    _currentPlaylistName = !string.IsNullOrEmpty(playlistName) ? playlistName : null;
                    UpdateNavHighlight();

                    // Загружаем список треков для этой секции (асинхронно, с ожиданием)
                    _ = RestoreNavContextAsync(navSection, playlistName, savedTrackIndex, json);
                }
                else
                {
                    // Нет контекста — просто восстанавливаем трек в плеере
                    RestoreLastTrack(json, -1);
                }

                // Восстанавливаем эквалайзер
                var eqArr = json["eq_gains"] as JArray;
                if (eqArr != null && eqArr.Count == EqSampleProvider.BandCount)
                {
                    var sliders = GetEqSliders();
                    for (int i = 0; i < EqSampleProvider.BandCount; i++)
                    {
                        _eqGains[i] = (float)eqArr[i].Value<double>();
                        sliders[i].Value = -_eqGains[i]; // Инверсия: положительный gain = слайдер вверх
                    }
                }

                // Восстанавливаем топ-жанры для рекомендаций
                var genresArr = json["top_genres"] as JArray;
                if (genresArr != null && genresArr.Count > 0)
                {
                    _recommendationService.SetTopGenres(genresArr.Select(g => g.ToString()).ToList());
                }

                // Акцент темы
                var accent = json["accent"]?.ToString();
                var accentLight = json["accent_light"]?.ToString();
                if (!string.IsNullOrEmpty(accent) && !string.IsNullOrEmpty(accentLight))
                {
                    _pendingAccent = accent;
                    _pendingAccentLight = accentLight;
                    ApplyAccentColors(accent, accentLight);
                }

                // Параметры фона
                var bgBlur = json["bg_blur"]?.Value<double>();
                if (bgBlur.HasValue)
                {
                    _bgBlurRadius = bgBlur.Value;
                    if (BgBlurSlider != null) BgBlurSlider.Value = bgBlur.Value;
                }
                var bgDarken = json["bg_darken"]?.Value<double>();
                if (bgDarken.HasValue)
                {
                    _bgDarken = bgDarken.Value;
                    if (BgDarkenSlider != null) BgDarkenSlider.Value = bgDarken.Value * 100.0;
                }

                // Discord RPC
                var discordEnabled = json["discord_enabled"]?.Value<bool>();
                if (discordEnabled.HasValue)
                {
                    _discordEnabled = discordEnabled.Value;
                    if (!_discordEnabled)
                    {
                        _discord.Dispose();
                    }
                }

                // Repeat / Shuffle
                var repeatEnabled = json["repeat_enabled"]?.Value<bool>();
                if (repeatEnabled.HasValue) _repeatEnabled = repeatEnabled.Value;

                var shuffleEnabled = json["shuffle_enabled"]?.Value<bool>();
                if (shuffleEnabled.HasValue)
                {
                    _shuffleEnabled = shuffleEnabled.Value;
                    Dispatcher.InvokeAsync(() => UpdateShuffleIcon(), DispatcherPriority.Loaded);
                }

                // Анимации
                var animEnabled = json["animations_enabled"]?.Value<bool>();
                if (animEnabled.HasValue) _animationsEnabled = animEnabled.Value;

                // Профиль пользователя (анализ текстов)
                var profileObj = json["user_profile"] as JObject;
                if (profileObj != null)
                {
                    var moods = (profileObj["moods"]?.Values<string>()?.ToList() ?? new List<string>()).Where(m => m != null).Select(m => m!).ToList();
                    var languages = (profileObj["languages"]?.Values<string>()?.ToList() ?? new List<string>()).Where(l => l != null).Select(l => l!).ToList();
                    var energy = profileObj["energy"]?.ToString() ?? "mixed";
                    var themes = (profileObj["themes"]?.Values<string>()?.ToList() ?? new List<string>()).Where(t => t != null).Select(t => t!).ToList();
                    var summary = profileObj["summary"]?.ToString() ?? "";

                    if (moods.Count > 0)
                    {
                        _recommendationService.SetUserProfile(new UserMusicProfile
                        {
                            Moods = moods,
                            Languages = languages,
                            Energy = energy,
                            Themes = themes,
                            Summary = summary
                        });
                    }
                }
            }
            catch { }
        }

        private async Task RestoreNavContextAsync(string navSection, string? playlistName, int savedTrackIndex, JObject json)
        {
            try
            {
                // Сначала загружаем список для секции (с ожиданием!)
                var loadId = ++_navLoadId;
                switch (navSection)
                {
                    case "home":
                        await LoadHomeAsync(loadId);
                        break;
                    case "likes":
                        GreetingText.Text = "Лайки";
                        if (_soundcloud.IsLoggedIn)
                            await LoadCloudLikesAsync(loadId);
                        else
                            LoadLikes();
                        break;
                    case "library":
                        GreetingText.Text = "Библиотека";
                        if (_soundcloud.IsLoggedIn)
                            await LoadCloudPlaylistsAsync(loadId);
                        else
                            LoadPlaylists();
                        break;
                }

                // Если были внутри плейлиста — загружаем его треки (теперь _cloudPlaylists заполнен)
                if (!string.IsNullOrEmpty(playlistName))
                {
                    LoadPlaylistTracks(playlistName);
                }

                // Восстанавливаем трек в плеере и выделяем в списке
                RestoreLastTrack(json, savedTrackIndex);
            }
            catch { }
        }

        private void RestoreLastTrack(JObject json, int savedTrackIndex)
        {
            var lastTrackObj = json["last_track"] as JObject;
            if (lastTrackObj == null) return;

            var lastTrack = new Track
            {
                VideoId = lastTrackObj["video_id"]?.ToString() ?? "",
                Title = lastTrackObj["title"]?.ToString() ?? "",
                ArtistName = lastTrackObj["artist"]?.ToString() ?? "",
                ArtworkUrl = lastTrackObj["artwork_url"]?.ToString() ?? "",
                DurationSeconds = lastTrackObj["duration"]?.Value<int>() ?? 0
            };

            PlayerTitle.Text = lastTrack.Title;
            PlayerArtist.Text = lastTrack.ArtistName;
            _currentTrack = lastTrack;
            LyricsButton.Visibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(lastTrack.ArtworkUrl))
            {
                _ = LoadImageAsync(lastTrack.ArtworkUrl).ContinueWith(t =>
                {
                    if (t.Result != null)
                    {
                        Dispatcher.InvokeAsync(() => PlayerArtwork.Source = t.Result);
                    }
                });
            }

            // Выделяем трек в списке (без автоплея)
            _suppressSelectionPlay = true;
            if (savedTrackIndex >= 0 && savedTrackIndex < _tracks.Count)
            {
                TracksList.SelectedIndex = savedTrackIndex;
            }
            else
            {
                bool found = false;
                for (int i = 0; i < _tracks.Count; i++)
                {
                    if (_tracks[i].VideoId == lastTrack.VideoId)
                    {
                        TracksList.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found)
                    _suppressSelectionPlay = false;
            }
        }

        private async Task LoadUserProfileAsync()
        {
            try
            {
                var userInfo = await _soundcloud.GetCurrentUserAsync();
                if (userInfo != null)
                {
                    UserDisplayName.Text = userInfo.Username;
                    if (!string.IsNullOrEmpty(userInfo.AvatarUrl))
                    {
                        var avatarUrl = userInfo.AvatarUrl.Replace("-large", "-t200x200");
                        var bmp = await LoadImageAsync(avatarUrl);
                        if (bmp != null)
                        {
                            UserAvatarImage.Source = bmp;
                            UserAvatarImage.Visibility = Visibility.Visible;
                            UserAvatarEmoji.Visibility = Visibility.Collapsed;
                        }
                    }
                    UpdateLoginButtonAppearance();
                }
            }
            catch { }
        }

        private void LoadBackgroundImage()
        {
            var bgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox", "background.jpg");
            if (File.Exists(bgPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(bgPath);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    BackgroundImage.Source = bmp;
                    // Размытие фона (BlurEffect)
                    BackgroundImage.Effect = _bgBlurRadius > 0
                        ? new System.Windows.Media.Effects.BlurEffect { Radius = _bgBlurRadius, KernelType = System.Windows.Media.Effects.KernelType.Gaussian }
                        : null;
                    // Градиент-затемнение поверх фото (0..1)
                    GradientOverlay.Opacity = _bgDarken;
                    // Чёрная накладка для усиления затемнения
                    if (BackgroundDarken != null)
                        BackgroundDarken.Opacity = _bgDarken;
                }
                catch { }
            }
            else
            {
                BackgroundImage.Source = null;
                BackgroundImage.Effect = null;
                GradientOverlay.Opacity = 1.0;
                if (BackgroundDarken != null) BackgroundDarken.Opacity = 0;
            }
        }

        // Titlebar
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            _discord.Dispose();
            Application.Current.Shutdown();
        }

        // Вход в аккаунт
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // Если уже вошли — показываем меню выхода
            if (_soundcloud.IsLoggedIn)
            {
                var result = MessageBox.Show("Выйти из аккаунта?", "Аккаунт", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _soundcloud.SetOAuthToken("");
                    UserNameText.Text = "";
                    UserNameText.Visibility = Visibility.Collapsed;
                    UserDisplayName.Text = "SC Native";
                    UserAvatarEmoji.Visibility = Visibility.Visible;
                    UserAvatarImage.Source = null;
                    UserAvatarImage.Visibility = Visibility.Collapsed;
                    UpdateLoginButtonAppearance();
                    SaveSettings();
                }
                return;
            }

            var loginWindow = new WebView2LoginWindow();
            if (loginWindow.ShowDialog() == true && !string.IsNullOrEmpty(loginWindow.OAuthToken))
            {
                _soundcloud.SetOAuthToken(loginWindow.OAuthToken);
                UserNameText.Text = "Загрузка профиля...";
                UserNameText.Visibility = Visibility.Visible;

                // Загружаем информацию о пользователе
                var userInfo = await _soundcloud.GetCurrentUserAsync();
                if (userInfo != null)
                {
                    UserDisplayName.Text = userInfo.Username;
                    UserNameText.Text = "SoundCloud";

                    if (!string.IsNullOrEmpty(userInfo.AvatarUrl))
                    {
                        var avatarUrl = userInfo.AvatarUrl.Replace("-large", "-t200x200");
                        var bmp = await LoadImageAsync(avatarUrl);
                        if (bmp != null)
                        {
                            UserAvatarImage.Source = bmp;
                            UserAvatarImage.Visibility = Visibility.Visible;
                            UserAvatarEmoji.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else
                {
                    UserDisplayName.Text = "Аккаунт";
                    UserNameText.Text = "Подключён";
                }

                UpdateLoginButtonAppearance();
                SaveSettings();
                MessageBox.Show("Вход выполнен! Теперь можешь загрузить свои плейлисты и лайки.");
            }
        }

        // Обновляет вид кнопки входа/выхода в настройках
        private void UpdateLoginButtonAppearance()
        {
            LoginButton.ApplyTemplate();
            var loginText = LoginButton.Template.FindName("LoginText", LoginButton) as TextBlock;
            var loginIcon = LoginButton.Template.FindName("LoginIcon", LoginButton) as MaterialDesignThemes.Wpf.PackIcon;

            if (_soundcloud.IsLoggedIn)
            {
                if (loginText != null) loginText.Text = "Выйти из аккаунта";
                if (loginIcon != null) loginIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.LogoutVariant;
                LoginButton.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)); // красный
                if (SettingsAccountStatus != null)
                    SettingsAccountStatus.Text = $"Вы вошли как {UserDisplayName.Text}";
            }
            else
            {
                if (loginText != null) loginText.Text = "Войти в SoundCloud";
                if (loginIcon != null) loginIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.LoginVariant;
                LoginButton.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x00)); // оранжевый
                if (SettingsAccountStatus != null)
                    SettingsAccountStatus.Text = "Вы не вошли в SoundCloud";
            }
        }

        // Облачные лайки
        private async void CloudLikesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_soundcloud.IsLoggedIn)
            {
                MessageBox.Show("Сначала войди в аккаунт");
                return;
            }

            _tracks.Clear();
            var (likes, error) = await _soundcloud.GetUserLikesAsync();
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show($"Ошибка загрузки лайков:\n\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (likes.Count == 0)
            {
                MessageBox.Show("Лайки не найдены (список пуст)");
                return;
            }
            foreach (var track in likes)
                AddTrack(track);
        }

        // Облачные плейлисты
        private async void CloudPlaylistsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_soundcloud.IsLoggedIn)
            {
                MessageBox.Show("Сначала войди в аккаунт");
                return;
            }

            _tracks.Clear();
            var (playlists, error) = await _soundcloud.GetUserPlaylistsAsync();
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show($"Ошибка загрузки плейлистов:\n\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (playlists.Count == 0)
            {
                MessageBox.Show("Плейлисты не найдены (список пуст)");
                return;
            }
            
            _cloudPlaylists = playlists;
            
            foreach (var playlist in playlists)
            {
                var totalSec = playlist.Tracks.Sum(t => t.DurationSeconds);
                AddTrack(new Track
                {
                    VideoId = playlist.Name,
                    Title = playlist.Name,
                    ArtistName = $"{playlist.Tracks.Count} треков • {FormatPlaylistDuration(totalSec)}",
                    ArtworkUrl = playlist.Tracks.FirstOrDefault()?.ArtworkUrl ?? "",
                    DurationSeconds = totalSec,
                    IsPlaylist = true
                });
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_animationsEnabled)
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
                };
                anim.Completed += (s, args) =>
                {
                    WindowState = WindowState.Minimized;
                    // Очищаем анимацию чтобы не блокировать Opacity при восстановлении
                    BeginAnimation(OpacityProperty, null);
                };
                BeginAnimation(OpacityProperty, anim);
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private bool _isMaximized = false; // наш флаг максимизации (не WindowState.Maximized)
        private bool _suppressStateChange = false; // защита от рекурсии в OnStateChanged

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (_suppressStateChange) return;

            if (WindowState == WindowState.Maximized)
            {
                // WindowStyle=None + Maximized = за панелью задач. Перехватываем.
                _suppressStateChange = true;
                WindowState = WindowState.Normal;
                _suppressStateChange = false;

                var workArea = GetCurrentMonitorWorkArea();
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
                _isMaximized = true;
                MaximizeIcon.Visibility = Visibility.Collapsed;
                RestoreIcon.Visibility = Visibility.Visible;
            }
            else if (WindowState == WindowState.Normal)
            {
                if (!_isMaximized)
                {
                    // Сначала убеждаемся что Opacity не заблокирован старой анимацией
                    Opacity = 1;
                    BeginAnimation(OpacityProperty, null);
                }
            }
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMaximized)
            {
                // Восстанавливаем нормальный размер
                _isMaximized = false;
                MaximizeIcon.Visibility = Visibility.Visible;
                RestoreIcon.Visibility = Visibility.Collapsed;
                // Восстанавливаем размер по умолчанию на текущем мониторе
                var workArea = GetCurrentMonitorWorkArea();
                Width = 1200;
                Height = 750;
                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Top + (workArea.Height - Height) / 2;
            }
            else
            {
                // Разворачиваем на рабочую область текущего монитора (без панели задач)
                var workArea = GetCurrentMonitorWorkArea();
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
                _isMaximized = true;
                MaximizeIcon.Visibility = Visibility.Collapsed;
                RestoreIcon.Visibility = Visibility.Visible;
            }
        }

        // Лайки
        private void LikesButton_Click(object sender, RoutedEventArgs e)
        {
            LoadLikes();
        }

        private void LoadLikes()
        {
            _tracks.Clear();
            var likes = _library.GetLikes();
            foreach (var track in likes)
                AddTrack(track);
        }

        // Плейлисты
        private void PlaylistsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPlaylists();
        }

        private void LoadPlaylists()
        {
            _tracks.Clear();
            var playlists = _library.GetPlaylists();
            foreach (var playlist in playlists)
            {
                var totalSec = playlist.Tracks.Sum(t => t.DurationSeconds);
                AddTrack(new Track
                {
                    VideoId = playlist.Name,
                    Title = playlist.Name,
                    ArtistName = $"{playlist.Tracks.Count} треков • {FormatPlaylistDuration(totalSec)}",
                    ArtworkUrl = playlist.Tracks.FirstOrDefault()?.ArtworkUrl ?? "",
                    DurationSeconds = totalSec,
                    IsPlaylist = true
                });
            }
        }
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            // Bounce-анимация кнопки
            if (_animationsEnabled)
            {
                var sb = new Storyboard();
                var scale = new DoubleAnimationUsingKeyFrames();
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, TimeSpan.FromMilliseconds(60)));
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, TimeSpan.FromMilliseconds(150)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, TimeSpan.FromMilliseconds(250)));
                Storyboard.SetTarget(scale, PlayPauseButton);
                Storyboard.SetTargetProperty(scale, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                sb.Children.Add(scale);

                var scaleY = new DoubleAnimationUsingKeyFrames();
                scaleY.KeyFrames = scale.KeyFrames.Clone();
                Storyboard.SetTarget(scaleY, PlayPauseButton);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                sb.Children.Add(scaleY);
                sb.Begin();
            }

            // Если плеер не инициализирован, но есть последний трек — запускаем его
            if (_wavePlayer == null && _currentTrack != null)
            {
                _ = PlayTrack(_currentTrack);
                return;
            }

            if (_wavePlayer == null) return;

            var template = PlayPauseButton.Template;
            var playIcon = (System.Windows.Shapes.Path)template.FindName("PlayIcon", PlayPauseButton);
            var pauseIcon = (System.Windows.Controls.StackPanel)template.FindName("PauseIcon", PlayPauseButton);

            if (_isPlaying)
            {
                _wavePlayer.Pause();
                if (playIcon != null) playIcon.Visibility = Visibility.Visible;
                if (pauseIcon != null) pauseIcon.Visibility = Visibility.Collapsed;

                if (_discordEnabled) _discord.Pause();

                // Обновляем индикатор паузы в списке
                UpdatePauseState(true);
            }
            else
            {
                _wavePlayer.Play();
                if (playIcon != null) playIcon.Visibility = Visibility.Collapsed;
                if (pauseIcon != null) pauseIcon.Visibility = Visibility.Visible;

                if (_discordEnabled && _audioReader != null)
                    _discord.Resume((int)_audioReader.CurrentTime.TotalSeconds, (int)_audioReader.TotalTime.TotalSeconds);

                // Обновляем индикатор паузы в списке
                UpdatePauseState(false);
            }
            _isPlaying = !_isPlaying;
            UpdateLyricsPlayPause();
            UpdateTaskbarPlayPause(_isPlaying);
        }

        private void UpdatePauseState(bool isPaused)
        {
            if (_currentTrack == null) return;
            // Обновляем в отображаемом списке
            var displayed = _tracks.FirstOrDefault(t => t.VideoId == _currentTrack.VideoId && !t.IsSectionHeader && !t.IsPlaylist);
            if (displayed != null) displayed.IsPaused = isPaused;
            // И в очереди
            var queued = _playbackQueue.FirstOrDefault(t => t.VideoId == _currentTrack.VideoId);
            if (queued != null) queued.IsPaused = isPaused;
        }

        // Поиск
        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _searchTimer.Tick += async (s, args) =>
            {
                _searchTimer.Stop();
                await SearchTracks(SearchBox.Text);
            };
            _searchTimer.Start();
        }

        private async Task SearchTracks(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            try
            {
                var results = await _soundcloud.SearchAsync(query, 20);
                _tracks.Clear();
                foreach (var track in results)
                    AddTrack(track);
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка поиска: {ex.Message}"); }
        }

        // Плеер
        private async void TracksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_menuClickSuppressPlay)
            {
                _menuClickSuppressPlay = false;
                return;
            }

            if (_suppressSelectionPlay)
            {
                _suppressSelectionPlay = false;
                return;
            }

            if (TracksList.SelectedItem is Track track)
            {
                if (track.IsSectionHeader) return;
                if (track.IsPlaylist)
                    LoadPlaylistTracks(track.VideoId);
                else
                    await PlayTrack(track);
            }
        }

        private void LoadPlaylistTracks(string playlistName)
        {
            _currentPlaylistName = playlistName;

            // Сначала ищем в облачных плейлистах
            var cloudPlaylist = _cloudPlaylists.Find(p => p.Name == playlistName);
            if (cloudPlaylist != null)
            {
                _tracks.Clear();
                foreach (var track in cloudPlaylist.Tracks)
                {
                    track.IsInPlaylist = true;
                    AddTrack(track);
                }
                return;
            }

            // Потом в локальных
            var playlists = _library.GetPlaylists();
            var playlist = playlists.Find(p => p.Name == playlistName);
            if (playlist == null) return;

            _tracks.Clear();
            foreach (var track in playlist.Tracks)
            {
                track.IsInPlaylist = true;
                AddTrack(track);
            }
        }

        private async Task PlayTrack(Track track)
        {
            // Каждому вызову PlayTrack — уникальный ID. Если пока мы ждали async,
            // кто-то другой вызвал PlayTrack — наш ID устареет и мы прервёмся
            var myId = ++_playTrackId;

            try
            {
                // СНАЧАЛА останавливаем текущее воспроизведение, потом запускаем новое
                // Это предотвращает одновременное воспроизведение двух треков
                // Важно: _isPlaying = false ДО StopPlayback, чтобы PlaybackStopped не вызвал автопереход
                _isPlaying = false;
                StopPlayback();

                _currentTrack = track;
                // Подсвечиваем играющий трек и в очереди, и в отображаемом списке
                track.IsCurrentlyPlaying = true;
                // Также подсвечиваем трек в _tracks (может быть другой объект при навигации)
                var displayedTrack = _tracks.FirstOrDefault(t => t.VideoId == track.VideoId && !t.IsSectionHeader && !t.IsPlaylist);
                if (displayedTrack != null && displayedTrack != track)
                    displayedTrack.IsCurrentlyPlaying = true;

                PlayerTitle.Text = track.Title;
                PlayerArtist.Text = track.ArtistName;
                LyricsButton.Visibility = Visibility.Visible;

                // Обновляем очередь воспроизведения только при ручном выборе трека
                // При автопереходе (_isAutoNext) очередь уже содержит правильный список
                if (!_isAutoNext)
                {
                    _playbackQueue = _tracks.Where(t => !t.IsSectionHeader && !t.IsPlaylist).ToList();
                    if (_shuffleEnabled && _playbackQueue.Count > 1)
                    {
                        // Перемешиваем, но текущий трек оставляем первым
                        var current = _playbackQueue.FirstOrDefault(t => t.VideoId == track.VideoId);
                        _playbackQueue.Remove(current);
                        var rng = new Random();
                        int n = _playbackQueue.Count;
                        for (int i = n - 1; i > 0; i--)
                        {
                            int j = rng.Next(i + 1);
                            (_playbackQueue[i], _playbackQueue[j]) = (_playbackQueue[j], _playbackQueue[i]);
                        }
                        _playbackQueue.Insert(0, current!);
                        _playbackQueueIndex = 0;
                    }
                    else
                    {
                        _playbackQueueIndex = _playbackQueue.FindIndex(t => t.VideoId == track.VideoId);
                    }
                }
                _isAutoNext = false;

                if (!string.IsNullOrEmpty(track.ArtworkUrl))
                {
                    _ = LoadImageAsync(track.ArtworkUrl).ContinueWith(t =>
                    {
                        if (t.Result != null)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                PlayerArtwork.Source = t.Result;
                                if (LyricsPanel.Visibility == Visibility.Visible)
                                    LyricsArtwork.Source = t.Result;
                            });
                        }
                    });
                }

                // Показываем индикатор загрузки
                PlayerTitle.Text = $"{track.Title} (загрузка...)";

                string? streamUrl;
                if (!string.IsNullOrEmpty(track.LocalFilePath) && track.VideoId.StartsWith("local_"))
                {
                    // Локальный файл — используем путь напрямую
                    streamUrl = track.LocalFilePath;
                }
                else
                {
                    streamUrl = await _soundcloud.GetAudioStreamUrlAsync(track.VideoId);
                }

                // Если пока мы ждали URL — кто-то другой вызвал PlayTrack, отменяемся
                if (myId != _playTrackId) return;

                if (string.IsNullOrEmpty(streamUrl))
                {
                    MessageBox.Show("Трек недоступен");
                    PlayerTitle.Text = track.Title;
                    return;
                }

                // Загружаем аудио в фоновом потоке чтобы не зависал UI
                await Task.Run(() =>
                {
                    _audioReader = new MediaFoundationReader(streamUrl);
                });

                // Проверяем что нас не отменили пока мы ждали
                if (myId != _playTrackId) return;

                PlayerTitle.Text = track.Title;

                _wavePlayer = new WaveOutEvent();

                // Вставляем EQ в аудио-цепочку:
                // Reader → Pcm16BitToSampleProvider (PCM→float) → EQ → SampleToWaveProvider → WaveOut
                var sampleProvider = new NAudio.Wave.SampleProviders.Pcm16BitToSampleProvider(_audioReader);
                _eqProvider = new EqSampleProvider(sampleProvider);
                // Применяем сохранённые настройки EQ
                for (int i = 0; i < EqSampleProvider.BandCount; i++)
                    _eqProvider.UpdateBand(i, _eqGains[i]);

                var waveProvider = new NAudio.Wave.SampleProviders.SampleToWaveProvider(_eqProvider);
                _wavePlayer.Init(waveProvider);
                _wavePlayer.Volume = (float)(VolumeSlider.Value / 100);

                // Инициализируем прогресс-бар
                var totalSeconds = _audioReader.TotalTime.TotalSeconds;
                if (totalSeconds > 0)
                {
                    ProgressSlider.Maximum = totalSeconds;
                    LyricsProgress.Maximum = totalSeconds;
                    TotalTimeText.Text = _audioReader.TotalTime.ToString(@"m\:ss");
                }
                else
                {
                    // Для потокового контента TotalTime может быть неизвестен в начале
                    ProgressSlider.Maximum = 0;
                    LyricsProgress.Maximum = 0;
                    TotalTimeText.Text = "--:--";
                }
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "0:00";
                _wavePlayer.PlaybackStopped += async (s, args) =>
                {
                    if (args.Exception == null && _isPlaying)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _isPlaying = false; // предотвращаем рекурсию

                            // Повтор трека
                            if (_repeatEnabled && _currentTrack != null)
                            {
                                _ = PlayTrack(_currentTrack);
                                return;
                            }

                            // Используем очередь воспроизведения (а не текущий список треков)
                            if (_playbackQueueIndex >= 0 && _playbackQueueIndex < _playbackQueue.Count - 1)
                            {
                                _playbackQueueIndex++;
                                _isAutoNext = true;
                                var nextTrack = _playbackQueue[_playbackQueueIndex];
                                // Всегда вызываем PlayTrack напрямую — SelectionChanged может не сработать
                                // если индекс не изменился
                                _ = PlayTrack(nextTrack);
                            }
                        });
                    }
                };
                _wavePlayer.Play();
                _isPlaying = true;
                

                PlayPauseButton.ApplyTemplate();
                var playIcon = PlayPauseButton.Template.FindName("PlayIcon", PlayPauseButton) as System.Windows.Shapes.Path;
                var pauseIcon = PlayPauseButton.Template.FindName("PauseIcon", PlayPauseButton) as System.Windows.Controls.StackPanel;
                if (playIcon != null) playIcon.Visibility = Visibility.Collapsed;
                if (pauseIcon != null) pauseIcon.Visibility = Visibility.Visible;

                UpdateTaskbarPlayPause(true);

                _progressTimer.Start();

                // Discord Rich Presence
                if (_discordEnabled)
                {
                    _discord.SetListening(
                        track.Title,
                        track.ArtistName,
                        (int)_audioReader.TotalTime.TotalSeconds,
                        0,
                        track.ArtworkUrl,
                        $"https://soundcloud.com/{track.VideoId}"
                    );
                }

                // Если панель текстов открыта — обновляем для нового трека
                if (LyricsPanel.Visibility == Visibility.Visible)
                    ShowLyricsPanel();
            }
            catch (Exception ex) 
            { 
                PlayerTitle.Text = _currentTrack?.Title ?? "Ошибка";
                var msg = ex.Message;
                if (msg.Contains("0x80072EFD") || msg.Contains("0x80072EE2") || msg.Contains("0x80072EE7"))
                    MessageBox.Show("Нет подключения к интернету. Проверьте сеть и попробуйте снова.");
                else
                    MessageBox.Show($"Ошибка воспроизведения: {msg}"); 
            }
        }

        private void StopPlayback()
        {
            // Снимаем подсветку с предыдущего трека
            if (_currentTrack != null) _currentTrack.IsCurrentlyPlaying = false;

            _progressTimer.Stop();
            _wavePlayer?.Stop();
            _wavePlayer?.Dispose();
            _audioReader?.Dispose();
            _wavePlayer = null;
            _audioReader = null;
            _eqProvider = null;
            _isPlaying = false;

            if (_discordEnabled) _discord.Clear();

            UpdateTaskbarPlayPause(false);
        }

        private void UpdateTaskbarPlayPause(bool isPlaying)
        {
            var iconBrush = new SolidColorBrush(Colors.White);
            if (isPlaying)
            {
                // Пауза — две вертикальные полосы
                TaskbarPlayPause.ImageSource = new DrawingImage(new GeometryDrawing(iconBrush, null,
                    Geometry.Parse("M4,2 L4,22 M12,2 L12,22")));
                TaskbarPlayPause.Description = "Пауза";
            }
            else
            {
                // Play — треугольник
                TaskbarPlayPause.ImageSource = new DrawingImage(new GeometryDrawing(iconBrush, null,
                    Geometry.Parse("M4,2 L4,22 L20,12 Z")));
                TaskbarPlayPause.Description = "Воспроизвести";
            }
        }



        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            // Используем очередь воспроизведения
            if (_playbackQueueIndex > 0)
            {
                _playbackQueueIndex--;
                _isAutoNext = true;
                var prevTrack = _playbackQueue[_playbackQueueIndex];
                var idx = _tracks.IndexOf(_tracks.FirstOrDefault(t => t.VideoId == prevTrack.VideoId));
                if (idx >= 0)
                    TracksList.SelectedIndex = idx;
                else
                    _ = PlayTrack(prevTrack);
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // Используем очередь воспроизведения
            if (_playbackQueueIndex >= 0 && _playbackQueueIndex < _playbackQueue.Count - 1)
            {
                _playbackQueueIndex++;
                _isAutoNext = true;
                var nextTrack = _playbackQueue[_playbackQueueIndex];
                var idx = _tracks.IndexOf(_tracks.FirstOrDefault(t => t.VideoId == nextTrack.VideoId));
                if (idx >= 0)
                    TracksList.SelectedIndex = idx;
                else
                    _ = PlayTrack(nextTrack);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_wavePlayer != null)
                _wavePlayer.Volume = (float)(e.NewValue / 100);

            // Меняем иконку динамика в зависимости от громкости с анимацией
            var vol = e.NewValue;
            var newKind = vol == 0 ? MaterialDesignThemes.Wpf.PackIconKind.VolumeOff
                : vol < 33 ? MaterialDesignThemes.Wpf.PackIconKind.VolumeLow
                : vol < 66 ? MaterialDesignThemes.Wpf.PackIconKind.VolumeMedium
                : MaterialDesignThemes.Wpf.PackIconKind.VolumeHigh;

            if (VolumeIcon.Kind != newKind)
            {
                VolumeIcon.Kind = newKind;
                if (_animationsEnabled)
                    AnimateVolumeIcon();
            }
        }

        private void AnimateVolumeIcon()
        {
            var sb = new Storyboard();
            var scaleUp = new DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(80));
            var scaleDown = new DoubleAnimation(1.25, 1, TimeSpan.FromMilliseconds(100));
            scaleDown.BeginTime = TimeSpan.FromMilliseconds(80);
            Storyboard.SetTarget(scaleUp, VolumeIcon);
            Storyboard.SetTargetProperty(scaleUp, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            Storyboard.SetTarget(scaleDown, VolumeIcon);
            Storyboard.SetTargetProperty(scaleDown, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            sb.Children.Add(scaleUp);
            sb.Children.Add(scaleDown);

            var scaleUpY = new DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(80));
            var scaleDownY = new DoubleAnimation(1.25, 1, TimeSpan.FromMilliseconds(100));
            scaleDownY.BeginTime = TimeSpan.FromMilliseconds(80);
            Storyboard.SetTarget(scaleUpY, VolumeIcon);
            Storyboard.SetTargetProperty(scaleUpY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            Storyboard.SetTarget(scaleDownY, VolumeIcon);
            Storyboard.SetTargetProperty(scaleDownY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            sb.Children.Add(scaleUpY);
            sb.Children.Add(scaleDownY);

            sb.Begin();
        }

        private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
        }

        private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            if (_audioReader != null)
            {
                _audioReader.CurrentTime = TimeSpan.FromSeconds(ProgressSlider.Value);
                if (_discordEnabled) _discord.SeekTo((int)_audioReader.CurrentTime.TotalSeconds, (int)_audioReader.TotalTime.TotalSeconds);
            }
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (_audioReader != null && _wavePlayer != null && _isPlaying && !_isDragging)
                {
                    var current = _audioReader.CurrentTime;
                    var total = _audioReader.TotalTime;

                    // Обновляем Maximum если он был неизвестен при старте (потоковый контент)
                    if (total.TotalSeconds > 0 && ProgressSlider.Maximum != total.TotalSeconds)
                    {
                        ProgressSlider.Maximum = total.TotalSeconds;
                        LyricsProgress.Maximum = total.TotalSeconds;
                    }

                    if (total.TotalSeconds > 0)
                    {
                        ProgressSlider.Value = current.TotalSeconds;
                        CurrentTimeText.Text = current.ToString(@"m\:ss");
                        TotalTimeText.Text = total.ToString(@"m\:ss");

                        // Обновляем мини-прогресс в панели текстов
                        if (LyricsPanel.Visibility == Visibility.Visible)
                        {
                            LyricsProgress.Value = current.TotalSeconds;
                            LyricsCurrentTime.Text = current.ToString(@"m\:ss");
                            LyricsTotalTime.Text = total.ToString(@"m\:ss");
                        }
                    }
                    else
                    {
                        // Потоковый контент без TotalTime — показываем только текущее время
                        CurrentTimeText.Text = current.ToString(@"m\:ss");
                    }
                }
            }
            catch { /* _audioReader может быть disposed между проверкой и доступом */ }
        }

        private void TrackMenuButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Button btn) return;
            var track = btn.DataContext as Track;
            if (track == null || track.IsSectionHeader) return;

            // Выделяем трек без воспроизведения
            _menuClickSuppressPlay = true;
            TracksList.SelectedItem = track;

            try
            {
                var menu = new ContextMenu();
                menu.Style = FindResource("MaterialDesignContextMenu") as Style;

                if (track.IsPlaylist)
                {
                    var deleteItem = new MenuItem { Header = "Удалить плейлист" };
                    deleteItem.Click += DeletePlaylist_Click;
                    menu.Items.Add(deleteItem);
                }
                else
                {
                    // Скачать трек
                    var downloadItem = new MenuItem { Header = "Скачать трек" };
                    downloadItem.Click += DownloadTrack_Click;
                    menu.Items.Add(downloadItem);

                    // Убрать из плейлиста — только если мы в плейлисте
                    if (!string.IsNullOrEmpty(_currentPlaylistName))
                    {
                        var removePlItem = new MenuItem { Header = "Убрать из плейлиста" };
                        removePlItem.Click += RemoveFromPlaylist_Click;
                        menu.Items.Add(removePlItem);
                    }
                }

                // Показываем меню в позиции курсора
                menu.PlacementTarget = this;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка меню: {ex.Message}");
            }
        }

        private async void DownloadTrack_Click(object? sender, RoutedEventArgs e)
        {
            var track = TracksList.SelectedItem as Track;
            if (track == null || track.IsSectionHeader || track.IsPlaylist) return;

            try
            {
                var streamUrl = await _soundcloud.GetAudioStreamUrlAsync(track.VideoId);
                if (string.IsNullOrEmpty(streamUrl))
                {
                    MessageBox.Show("Не удалось получить ссылку для скачивания", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"{track.ArtistName} - {track.Title}".ReplaceInvalidFileNameChars(),
                    Filter = "MP3 файлы|*.mp3|Все файлы|*.*",
                    DefaultExt = ".mp3"
                };

                if (dialog.ShowDialog() == true)
                {
                    using var client = new HttpClient();
                    var bytes = await client.GetByteArrayAsync(streamUrl);
                    await File.WriteAllBytesAsync(dialog.FileName, bytes);
                    MessageBox.Show("Трек скачан!", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка скачивания: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Heart_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn) return;
            var track = btn.DataContext as Track;
            if (track == null || track.IsSectionHeader || track.IsPlaylist) return;

            if (track.IsLiked)
            {
                _library.RemoveLike(track.VideoId);
                track.IsLiked = false;
            }
            else
            {
                _library.AddLike(track);
                track.IsLiked = true;

                // Анимация пульса при лайке
                if (_animationsEnabled)
                    AnimateHeartPulse(btn);
            }
        }

        private void AnimateHeartPulse(Button btn)
        {
            try
            {
                var border = (btn.Template?.FindName("Bd", btn) as Border);
                if (border?.RenderTransform is ScaleTransform scale)
                {
                    var grow = new DoubleAnimation(1, 1.35, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var shrink = new DoubleAnimation(1.35, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };
                    grow.Completed += (s, _) => scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);

                    var growY = new DoubleAnimation(1, 1.35, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var shrinkY = new DoubleAnimation(1.35, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };
                    growY.Completed += (s, _) => scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, growY);
                }
            }
            catch { }
        }

        private void AddToPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn) return;
            var track = btn.DataContext as Track;
            if (track == null || track.IsSectionHeader || track.IsPlaylist) return;

            var playlists = _library.GetPlaylists();
            if (playlists.Count == 0)
            {
                MessageBox.Show("Сначала создайте плейлист");
                return;
            }

            var dialog = new PlaylistPickerDialog(playlists);
            if (dialog.ShowDialog() != true) return;

            var selectedPlaylist = dialog.SelectedPlaylistName;
            _library.AddToPlaylist(selectedPlaylist, track);
        }

        private async void LikeTrack_Click(object sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedItem is not Track track || track.IsPlaylist) return;

            if (track.IsLiked)
            {
                _library.RemoveLike(track.VideoId);
                track.IsLiked = false;
            }
            else
            {
                _library.AddLike(track);
                track.IsLiked = true;
            }
        }

        private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedItem is not Track track || track.IsPlaylist) return;

            var playlists = _library.GetPlaylists();
            if (playlists.Count == 0)
            {
                MessageBox.Show("Сначала создайте плейлист");
                return;
            }

            var dialog = new PlaylistPickerDialog(playlists);
            if (dialog.ShowDialog() != true) return;

            var selectedPlaylist = dialog.SelectedPlaylistName;
            _library.AddToPlaylist(selectedPlaylist, track);
            MessageBox.Show($"✅ Трек добавлен в \"{selectedPlaylist}\"!");
        }

        private void RemoveLike_Click(object sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedItem is not Track track || track.IsPlaylist) return;

            if (!track.IsLiked) return;

            _library.RemoveLike(track.VideoId);
            track.IsLiked = false;

            // Если мы в секции лайков — убираем трек из списка
            if (_currentNavSection == "likes")
                _tracks.Remove(track);
        }

        private void RemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedItem is not Track track || track.IsPlaylist) return;

            if (string.IsNullOrEmpty(_currentPlaylistName))
            {
                // Не внутри плейлиста — предлагаем выбрать из какого убрать
                var playlists = _library.GetPlaylists();
                if (playlists.Count == 0)
                {
                    MessageBox.Show("Нет плейлистов");
                    return;
                }

                var dialog = new PlaylistPickerDialog(playlists);
                if (dialog.ShowDialog() != true) return;

                var selectedPlaylist = dialog.SelectedPlaylistName;
                _library.RemoveFromPlaylist(selectedPlaylist, track.VideoId);
                MessageBox.Show($"Трек удалён из \"{selectedPlaylist}\"");
                return;
            }

            // Внутри плейлиста — удаляем из текущего
            _library.RemoveFromPlaylist(_currentPlaylistName, track.VideoId);
            _tracks.Remove(track);
            MessageBox.Show($"Трек удалён из \"{_currentPlaylistName}\"");
        }

        private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (TracksList.SelectedItem is not Track track || !track.IsPlaylist) return;

            var result = MessageBox.Show(
                $"Удалить плейлист \"{track.Title}\"?",
                "Удаление плейлиста",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            _library.DeletePlaylist(track.Title);
            _tracks.Remove(track);
            MessageBox.Show($"Плейлист \"{track.Title}\" удалён");
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            HideLyricsPanel();
            _currentNavSection = "settings";
            _currentPlaylistName = null;
            UpdateNavHighlight();

            // Скрываем контент треков, показываем панель настроек
            ContentHeader.Visibility = Visibility.Collapsed;
            TracksScroll.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            AnimateFadeIn(SettingsPanel, 0, 300);

            // Заполняем текущие значения
            DiscordToggle.IsChecked = _discordEnabled;
            AnimationsToggle.IsChecked = _animationsEnabled;

            // Превью текущей темы
            if (GradientOverlay?.Background is LinearGradientBrush brush && brush.GradientStops.Count >= 3)
            {
                SettingsPreviewStop1.Color = brush.GradientStops[0].Color;
                SettingsPreviewStop2.Color = brush.GradientStops[1].Color;
                SettingsPreviewStop3.Color = brush.GradientStops[2].Color;
            }

            // Превью фона
            LoadSettingsBgPreview();
        }

        private void LoadSettingsBgPreview()
        {
            var bgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox", "background.jpg");
            if (File.Exists(bgPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(bgPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    SettingsBgPreviewImage.Source = bmp;
                    SettingsBgPreviewText.Text = "";
                }
                catch { }
            }
        }

        private void HideSettingsPanel()
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ContentHeader.Visibility = Visibility.Visible;
            TracksScroll.Visibility = Visibility.Visible;
            // Анимация появления контента
            AnimateFadeIn(TracksScroll, 0, 250);
            AnimateFadeIn(ContentHeader, 0, 200);
        }

        private void DiscordToggle_Changed(object sender, RoutedEventArgs e)
        {
            _discordEnabled = DiscordToggle.IsChecked ?? true;
            if (_discordEnabled)
            {
                _discord.Connect();
                // Если трек играет — сразу обновляем presence
                if (_isPlaying && _currentTrack != null && _audioReader != null)
                {
                    _discord.SetListening(
                        _currentTrack.Title,
                        _currentTrack.ArtistName,
                        (int)_audioReader.TotalTime.TotalSeconds,
                        (int)_audioReader.CurrentTime.TotalSeconds,
                        _currentTrack.ArtworkUrl,
                        $"https://soundcloud.com/{_currentTrack.VideoId}"
                    );
                }
            }
            else
            {
                _discord.Clear();
                _discord.Dispose();
            }
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            _repeatEnabled = !_repeatEnabled;
            RepeatButton.ApplyTemplate();
            var icon = RepeatButton.Template.FindName("RepeatIcon", RepeatButton) as MaterialDesignThemes.Wpf.PackIcon;
            if (icon != null)
                icon.Foreground = _repeatEnabled ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55));
            SaveSettings();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            _shuffleEnabled = !_shuffleEnabled;
            UpdateShuffleIcon();

            // Пересоздаём очередь воспроизведения с учётом shuffle
            RebuildPlaybackQueue();

            SaveSettings();
        }

        private void UpdateShuffleIcon()
        {
            ShuffleButton.ApplyTemplate();
            var icon = ShuffleButton.Template.FindName("ShuffleIcon", ShuffleButton) as MaterialDesignThemes.Wpf.PackIcon;
            if (icon != null)
                icon.Foreground = _shuffleEnabled ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55));
        }

        private void RebuildPlaybackQueue()
        {
            if (_currentTrack == null || _playbackQueue.Count == 0) return;

            // Строим очередь из текущего списка треков
            _playbackQueue = _tracks.Where(t => !t.IsSectionHeader && !t.IsPlaylist).ToList();

            if (_shuffleEnabled && _playbackQueue.Count > 1)
            {
                // Убираем текущий трек, перемешиваем остальные, ставим текущий первым
                var current = _playbackQueue.FirstOrDefault(t => t.VideoId == _currentTrack.VideoId);
                _playbackQueue.Remove(current);
                var rng = new Random();
                int n = _playbackQueue.Count;
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (_playbackQueue[i], _playbackQueue[j]) = (_playbackQueue[j], _playbackQueue[i]);
                }
                _playbackQueue.Insert(0, current!);
                _playbackQueueIndex = 0;
            }
            else
            {
                // Восстанавливаем порядок по списку треков
                _playbackQueueIndex = _playbackQueue.FindIndex(t => t.VideoId == _currentTrack.VideoId);
                if (_playbackQueueIndex < 0) _playbackQueueIndex = 0;
            }
        }

        private void AnimationsToggle_Changed(object sender, RoutedEventArgs e)
        {
            _animationsEnabled = AnimationsToggle.IsChecked ?? true;
            SaveSettings();
        }

        private void SetSettingsPreview(Color c1, Color c2, Color c3)
        {
            SettingsPreviewStop1.Color = c1;
            SettingsPreviewStop2.Color = c2;
            SettingsPreviewStop3.Color = c3;
        }

        private string _pendingAccent = "#FFFF7A00";
        private string _pendingAccentLight = "#FFFF9E2E";

        private void SetPendingAccent(string accent, string accentLight)
        {
            _pendingAccent = accent;
            _pendingAccentLight = accentLight;
        }

        private static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromArgb(0xFF, 0xFF, 0x7A, 0x00); }
        }

        private void ApplyAccentColors(string accent, string accentLight)
        {
            var ac = ParseColor(accent);
            var acl = ParseColor(accentLight);
            Resources["AccentColor"] = ac;
            Resources["AccentLightColor"] = acl;

            // Обновляем hero-градиент в настройках (GradientStop не поддерживает DynamicResource)
            if (SettingsHeroBrush != null && SettingsHeroBrush.GradientStops.Count >= 2)
            {
                SettingsHeroBrush.GradientStops[0].Color = Color.FromArgb(0x33, acl.R, acl.G, acl.B);
                SettingsHeroBrush.GradientStops[1].Color = Color.FromArgb(0x0A, ac.R, ac.G, ac.B);
            }

            // Обновляем градиенты внутри ControlTemplate кнопок — через визуальное дерево
            UpdateTemplateGradient(CreatePlaylistTopBtn, acl, ac);
            UpdateTemplateGradient(SettingsApplyBtn, acl, ac);

            // Иконка настроек — RadialGradientBrush (не в шаблоне, ищем через дерево)
            var heroBorder = FindVisualChild<Border>(SettingsPanel as DependencyObject ?? this, "SettingsHeroIconBorder");
            if (heroBorder?.Background is RadialGradientBrush rgb && rgb.GradientStops.Count >= 2)
            {
                rgb.GradientStops[0].Color = acl;
                rgb.GradientStops[1].Color = ac;
            }
        }

        private void UpdateTemplateGradient(Button btn, Color lightColor, Color accentColor)
        {
            if (btn == null) return;
            btn.ApplyTemplate();
            var bd = btn.Template?.FindName("Bd", btn) as Border;
            if (bd?.Background is LinearGradientBrush lgb && lgb.GradientStops.Count >= 2)
            {
                lgb.GradientStops[0].Color = lightColor;
                lgb.GradientStops[1].Color = accentColor;
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name = null) where T : FrameworkElement
        {
            if (parent == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && (name == null || fe.Name == name)) return fe;
                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void Dark_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x0D, 0x0D, 0x0D), Color.FromArgb(0xFF, 0x1A, 0x1A, 0x2E), Color.FromArgb(0xFF, 0x0D, 0x0D, 0x0D));
            SetPendingAccent("#FFFF7A00", "#FFFF9E2E");
        }
        private void Blue_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x1E), Color.FromArgb(0xFF, 0x1A, 0x1A, 0x4E), Color.FromArgb(0xFF, 0x0A, 0x0A, 0x1E));
            SetPendingAccent("#FF4A8FFF", "#FF7AB0FF");
        }
        private void Purple_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x0F, 0x05, 0x1E), Color.FromArgb(0xFF, 0x2D, 0x1B, 0x69), Color.FromArgb(0xFF, 0x0F, 0x05, 0x1E));
            SetPendingAccent("#FF9B5BFF", "#FFB58AFF");
        }
        private void Burgundy_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x1A, 0x05, 0x0D), Color.FromArgb(0xFF, 0x3D, 0x0D, 0x1A), Color.FromArgb(0xFF, 0x1A, 0x05, 0x0D));
            SetPendingAccent("#FFE0466F", "#FFFF6E94");
        }
        private void Green_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x05, 0x14, 0x0A), Color.FromArgb(0xFF, 0x0D, 0x2B, 0x1A), Color.FromArgb(0xFF, 0x05, 0x14, 0x0A));
            SetPendingAccent("#FF4ECCA3", "#FF74E0BA");
        }
        private void Red_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x14, 0x05, 0x05), Color.FromArgb(0xFF, 0x2B, 0x0D, 0x0D), Color.FromArgb(0xFF, 0x14, 0x05, 0x05));
            SetPendingAccent("#FFFF5252", "#FFFF7A7A");
        }
        private void Rose_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x1A, 0x05, 0x0F), Color.FromArgb(0xFF, 0x3D, 0x0D, 0x22), Color.FromArgb(0xFF, 0x1A, 0x05, 0x0F));
            SetPendingAccent("#FFF06292", "#FFFF8AB0");
        }
        private void Cyan_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x05, 0x14, 0x14), Color.FromArgb(0xFF, 0x0D, 0x2B, 0x2B), Color.FromArgb(0xFF, 0x05, 0x14, 0x14));
            SetPendingAccent("#FF26C6DA", "#FF67DAE8");
        }
        private void Amber_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x1A, 0x10, 0x05), Color.FromArgb(0xFF, 0x3D, 0x2B, 0x0D), Color.FromArgb(0xFF, 0x1A, 0x10, 0x05));
            SetPendingAccent("#FFFFB300", "#FFFFCA28");
        }
        private void Mint_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x05, 0x1A, 0x0F), Color.FromArgb(0xFF, 0x0D, 0x3D, 0x22), Color.FromArgb(0xFF, 0x05, 0x1A, 0x0F));
            SetPendingAccent("#FF69F0AE", "#FF9CF5C8");
        }
        private void Lavender_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x0F, 0x05, 0x20), Color.FromArgb(0xFF, 0x2D, 0x1A, 0x4E), Color.FromArgb(0xFF, 0x0F, 0x05, 0x20));
            SetPendingAccent("#FFB388FF", "#FFD1B0FF");
        }
        private void Ocean_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPreview(Color.FromArgb(0xFF, 0x05, 0x0A, 0x1A), Color.FromArgb(0xFF, 0x0D, 0x1A, 0x3D), Color.FromArgb(0xFF, 0x05, 0x0A, 0x1A));
            SetPendingAccent("#FF448AFF", "#FF72A9FF");
        }

        private void CustomTheme_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Window
            {
                Title = "Своя тема",
                Width = 320,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize
            };

            var root = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0E, 0x0E, 0x0E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x22, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20)
            };
            root.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black };

            var panel = new StackPanel();

            var header = new TextBlock { Text = "Свой акцент", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(header);

            var accentLabel = new TextBlock { Text = "Акцентный цвет (HEX):", Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA)), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(accentLabel);

            var accentBox = new TextBox { Text = "#FF7A00", Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x15, 0x15, 0x15)), BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6), FontSize = 13, FontFamily = new FontFamily("Consolas") };
            panel.Children.Add(accentBox);

            var lightLabel = new TextBlock { Text = "Светлый акцент (HEX):", Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA)), FontSize = 11, Margin = new Thickness(0, 10, 0, 4) };
            panel.Children.Add(lightLabel);

            var lightBox = new TextBox { Text = "#FF9E2E", Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x15, 0x15, 0x15)), BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6), FontSize = 13, FontFamily = new FontFamily("Consolas") };
            panel.Children.Add(lightBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };

            var cancelBtn = new Button { Content = "Отмена", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
            cancelBtn.Click += (_, __) => dlg.Close();
            btnPanel.Children.Add(cancelBtn);

            var applyBtn = new Button { Content = "Применить", Width = 90, Height = 30, Cursor = System.Windows.Input.Cursors.Hand };
            applyBtn.Click += (_, __) =>
            {
                try
                {
                    var ac = (Color)ColorConverter.ConvertFromString(accentBox.Text);
                    var acl = (Color)ColorConverter.ConvertFromString(lightBox.Text);
                    SetSettingsPreview(Color.FromArgb(0xFF, 0x0D, 0x0D, 0x0D), Color.FromArgb(0xFF, 0x1A, 0x1A, 0x2E), Color.FromArgb(0xFF, 0x0D, 0x0D, 0x0D));
                    SetPendingAccent(accentBox.Text, lightBox.Text);
                    dlg.Close();
                }
                catch
                {
                    MessageBox.Show("Неверный формат цвета. Пример: #FF7A00", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            btnPanel.Children.Add(applyBtn);

            panel.Children.Add(btnPanel);
            root.Child = panel;
            dlg.Content = root;
            dlg.MouseLeftButtonDown += (_, ev) => { if (ev.LeftButton == System.Windows.Input.MouseButtonState.Pressed) dlg.DragMove(); };
            dlg.ShowDialog();
        }

        private void ChooseBackground_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выбрать фоновое изображение",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Все файлы|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                _settingsBgImagePath = dialog.FileName;
                _settingsRemoveBg = false;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dialog.FileName);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    SettingsBgPreviewImage.Source = bmp;
                    SettingsBgPreviewText.Text = "";
                }
                catch
                {
                    SettingsBgPreviewText.Text = "Ошибка загрузки";
                }
            }
        }

        private void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            _settingsRemoveBg = true;
            _settingsBgImagePath = null;
            SettingsBgPreviewImage.Source = null;
            SettingsBgPreviewText.Text = "Фон убран";
        }

        private void SettingsApply_Click(object sender, RoutedEventArgs e)
        {
            // Цветовая тема
            var newBrush = new LinearGradientBrush();
            newBrush.StartPoint = new System.Windows.Point(0, 0);
            newBrush.EndPoint = new System.Windows.Point(1, 1);
            newBrush.GradientStops.Add(new GradientStop(SettingsPreviewStop1.Color, 0));
            newBrush.GradientStops.Add(new GradientStop(SettingsPreviewStop2.Color, 0.5));
            newBrush.GradientStops.Add(new GradientStop(SettingsPreviewStop3.Color, 1));
            GradientOverlay.Background = newBrush;

            // Акцент темы
            ApplyAccentColors(_pendingAccent, _pendingAccentLight);

            // Параметры фона: размытие и затемнение
            _bgBlurRadius = BgBlurSlider != null ? BgBlurSlider.Value : 0;
            _bgDarken = BgDarkenSlider != null ? BgDarkenSlider.Value / 100.0 : 0.5;

            // Фоновое изображение
            if (!string.IsNullOrEmpty(_settingsBgImagePath))
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox");
                    Directory.CreateDirectory(dir);
                    var destPath = Path.Combine(dir, "background.jpg");
                    File.Copy(_settingsBgImagePath, destPath, true);
                }
                catch { }
                _settingsBgImagePath = null;
            }
            else if (_settingsRemoveBg)
            {
                try
                {
                    var bgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox", "background.jpg");
                    if (File.Exists(bgPath)) File.Delete(bgPath);
                }
                catch { }
                _settingsRemoveBg = false;
            }

            LoadBackgroundImage();
            SaveSettings();
        }

        private double _bgBlurRadius = 0;
        private double _bgDarken = 0.5;

        private void BgBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BgBlurValue != null) BgBlurValue.Text = $"{(int)e.NewValue}px";
        }

        private void BgDarkenSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BgDarkenValue != null) BgDarkenValue.Text = $"{(int)e.NewValue}%";
        }

        private void BugReportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new BugReportDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void SuggestButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SuggestDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void SpotifyImportButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new CsvImportWindow();
            window.ShowDialog();
        }

        private void CreatePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CreatePlaylistDialog
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PlaylistName))
            {
                _library.CreatePlaylist(dialog.PlaylistName);
            }
        }

        private void AddLocalMusic_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите музыкальные файлы",
                Filter = "Аудио файлы|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.wma|Все файлы|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true) return;

            foreach (var filePath in dialog.FileNames)
            {
                try
                {
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    var ext = System.IO.Path.GetExtension(filePath).ToLower();

                    // Создаём трек из локального файла
                    var track = new Track
                    {
                        VideoId = $"local_{Guid.NewGuid():N}",
                        Title = fileName,
                        ArtistName = "Локальный файл",
                        DurationSeconds = 0,
                        ArtworkUrl = ""
                    };

                    // Пытаемся получить длительность
                    try
                    {
                        using var reader = new MediaFoundationReader(filePath);
                        track.DurationSeconds = (int)reader.TotalTime.TotalSeconds;
                    }
                    catch { /* не критично */ }

                    // Сохраняем путь к файлу
                    track.LocalFilePath = filePath;

                    // Добавляем в лайки
                    _library.AddLike(track);
                    AddTrack(track);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось добавить {filePath}: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // Эквалайзер
        private Slider[] GetEqSliders() => new[] { EqSlider0, EqSlider1, EqSlider2, EqSlider3, EqSlider4, EqSlider5, EqSlider6, EqSlider7, EqSlider8, EqSlider9 };
        private TextBlock[] GetEqValues() => new[] { EqVal0, EqVal1, EqVal2, EqVal3, EqVal4, EqVal5, EqVal6, EqVal7, EqVal8, EqVal9 };

        private static string FormatDb(float val) => val == 0 ? "0" : (val > 0 ? $"+{val:0}" : $"{val:0}");

        private void EqButton_Click(object sender, RoutedEventArgs e)
        {
            EqPopup.IsOpen = !EqPopup.IsOpen;
        }

        private void LyricsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null) return;
            ShowLyricsPanel();
        }

        private void LyricsBack_Click(object sender, RoutedEventArgs e)
        {
            HideLyricsPanel();
        }

        private bool _settingsWasOpenBeforeLyrics = false;

        private void ShowLyricsPanel()
        {
            if (_currentTrack == null) return;

            // Скрываем всё, что может перекрывать панель текстов
            _settingsWasOpenBeforeLyrics = SettingsPanel.Visibility == Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Collapsed;
            ContentHeader.Visibility = Visibility.Collapsed;
            TracksScroll.Visibility = Visibility.Collapsed;
            LyricsPanel.Visibility = Visibility.Visible;

            // Заполняем карточку трека
            LyricsTitle.Text = _currentTrack.Title;
            LyricsArtist.Text = _currentTrack.ArtistName;
            LyricsArtwork.Source = PlayerArtwork.Source;
            LyricsArtworkPlaceholder.Visibility = PlayerArtwork.Source != null
                ? Visibility.Collapsed : Visibility.Visible;

            // Обновляем состояние play/pause
            UpdateLyricsPlayPause();

            // Сразу очищаем старый текст и показываем загрузку
            _lyricsSyncTimer?.Stop();
            _lyricsLines.Clear();
            _lyricsBlocks.Clear();
            _lyricsCurrentLine = -1;
            LyricsSyncedPanel.Children.Clear();
            LyricsPlainText.Text = "";
            LyricsSyncedScroll.Visibility = Visibility.Collapsed;
            LyricsPlainScroll.Visibility = Visibility.Collapsed;
            LyricsNotFoundPanel.Visibility = Visibility.Collapsed;
            LyricsLoadingPanel.Visibility = Visibility.Visible;

            // Загружаем тексты
            _ = LoadLyricsAsync(_currentTrack.ArtistName, _currentTrack.Title);
        }

        private void HideLyricsPanel()
        {
            _lyricsSyncTimer?.Stop();
            LyricsPanel.Visibility = Visibility.Collapsed;
            ContentHeader.Visibility = Visibility.Visible;
            TracksScroll.Visibility = Visibility.Visible;
            if (_settingsWasOpenBeforeLyrics)
            {
                SettingsPanel.Visibility = Visibility.Visible;
                _settingsWasOpenBeforeLyrics = false;
            }
        }

        private void UpdateLyricsPlayPause()
        {
            // Обновляем иконку play/pause на мини-контроле
            LyricsPlayBtn.ApplyTemplate();
            var playIcon = LyricsPlayBtn.Template.FindName("LPlayIcon", LyricsPlayBtn) as System.Windows.Shapes.Path;
            var pauseIcon = LyricsPlayBtn.Template.FindName("LPauseIcon", LyricsPlayBtn) as StackPanel;
            if (playIcon != null) playIcon.Visibility = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
            if (pauseIcon != null) pauseIcon.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadLyricsAsync(string artist, string title)
        {
            LyricsLoadingPanel.Visibility = Visibility.Visible;
            LyricsNotFoundPanel.Visibility = Visibility.Collapsed;
            LyricsSyncedScroll.Visibility = Visibility.Collapsed;
            LyricsPlainScroll.Visibility = Visibility.Collapsed;

            _lyricsLines.Clear();
            _lyricsBlocks.Clear();
            _lyricsCurrentLine = -1;
            _lyricsSynced = false;
            _lyricsSyncTimer?.Stop();

            // Сначала проверяем кеш
            string? plain = null;
            string? synced = null;
            var trackId = _currentTrack?.VideoId ?? "";

            if (!string.IsNullOrEmpty(trackId) && _lyricsCache.Exists(trackId))
            {
                if (_lyricsCache.IsNotFound(trackId))
                {
                    LyricsLoadingPanel.Visibility = Visibility.Collapsed;
                    LyricsNotFoundPanel.Visibility = Visibility.Visible;
                    return;
                }

                var cached = _lyricsCache.Get(trackId);
                if (cached != null)
                {
                    plain = cached.Value.Plain;
                    synced = cached.Value.Synced;
                }
            }

            // Если в кеше нет — фетчим из API
            if (string.IsNullOrEmpty(plain))
            {
                var result = await _lyricsService.SearchAsync(artist, title);
                plain = result.Plain;
                synced = result.Synced;

                // Кешируем результат
                if (!string.IsNullOrEmpty(trackId))
                {
                    if (!string.IsNullOrEmpty(plain))
                        _lyricsCache.Put(trackId, plain, synced);
                    else
                        _lyricsCache.MarkNotFound(trackId);
                }
            }

            LyricsLoadingPanel.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(plain))
            {
                LyricsNotFoundPanel.Visibility = Visibility.Visible;
                return;
            }

            if (!string.IsNullOrEmpty(synced))
            {
                var lines = ParseLrc(synced);
                if (lines.Count > 0)
                {
                    _lyricsLines = lines;
                    _lyricsSynced = true;
                    BuildSyncedLyricsUI();
                    LyricsSyncedScroll.Visibility = Visibility.Visible;

                    _lyricsSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    _lyricsSyncTimer.Tick += LyricsSyncTimer_Tick;
                    _lyricsSyncTimer.Start();
                    return;
                }
            }

            LyricsPlainText.Text = plain;
            LyricsPlainScroll.Visibility = Visibility.Visible;
        }

        private static List<(TimeSpan Time, string Text)> ParseLrc(string lrc)
        {
            var lines = new List<(TimeSpan, string)>();
            var regex = new System.Text.RegularExpressions.Regex(@"\[(\d{1,3}):(\d{2})(?:[.:]\d{1,3})?\](.*)");

            foreach (var rawLine in lrc.Split('\n'))
            {
                var match = regex.Match(rawLine.Trim());
                if (!match.Success) continue;

                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var text = match.Groups[3].Value.Trim();

                var time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                lines.Add((time, text));
            }

            return lines;
        }

        private void BuildSyncedLyricsUI()
        {
            LyricsSyncedPanel.Children.Clear();
            _lyricsBlocks.Clear();

            LyricsSyncedPanel.Children.Add(new Border { Height = 140 });

            var inactiveClr = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);

            for (int i = 0; i < _lyricsLines.Count; i++)
            {
                var line = _lyricsLines[i];
                var lineIndex = i;

                var brush = new SolidColorBrush(inactiveClr);
                brush.Freeze();

                var tb = new TextBlock
                {
                    Text = line.Text,
                    FontSize = 14,
                    Foreground = brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 6),
                    Padding = new Thickness(8, 4, 8, 4),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 500,
                    Cursor = Cursors.Hand
                };

                tb.MouseLeftButtonDown += (s, e) =>
                {
                    if (lineIndex >= 0 && lineIndex < _lyricsLines.Count)
                    {
                        if (_audioReader != null)
                        {
                            _audioReader.CurrentTime = _lyricsLines[lineIndex].Time;
                            if (_discordEnabled) _discord.SeekTo((int)_audioReader.CurrentTime.TotalSeconds, (int)_audioReader.TotalTime.TotalSeconds);
                        }
                        _lyricsCurrentLine = lineIndex;
                        UpdateLyricsHighlightAnimated();
                    }
                };

                _lyricsBlocks.Add(tb);
                LyricsSyncedPanel.Children.Add(tb);
            }

            LyricsSyncedPanel.Children.Add(new Border { Height = 240 });
        }

        private void LyricsSyncTimer_Tick(object? sender, EventArgs e)
        {
            if (_lyricsLines.Count == 0) return;

            var pos = _audioReader?.CurrentTime;
            if (pos == null) return;

            var time = pos.Value;
            int newIndex = -1;

            for (int i = _lyricsLines.Count - 1; i >= 0; i--)
            {
                if (_lyricsLines[i].Time <= time)
                {
                    newIndex = i;
                    break;
                }
            }

            if (newIndex == _lyricsCurrentLine) return;

            _lyricsCurrentLine = newIndex;
            UpdateLyricsHighlightAnimated();
            LyricsAutoScroll();
        }

        private static readonly Color _lyricsActiveClr = Color.FromRgb(0xCC, 0x7B, 0x3A);
        private static readonly Color _lyricsInactiveClr = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
        private static readonly Color _lyricsNearClr = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        private static readonly Duration _lyricsColorDur = TimeSpan.FromMilliseconds(350);
        private static readonly Duration _lyricsSizeDur = TimeSpan.FromMilliseconds(300);

        private void UpdateLyricsHighlightAnimated()
        {
            for (int i = 0; i < _lyricsBlocks.Count; i++)
            {
                var tb = _lyricsBlocks[i];

                Color targetClr;
                double targetSize;
                FontWeight targetWeight;

                if (i == _lyricsCurrentLine)
                {
                    targetClr = _lyricsActiveClr;
                    targetSize = 16;
                    targetWeight = FontWeights.SemiBold;
                }
                else if (_lyricsCurrentLine >= 0 && Math.Abs(i - _lyricsCurrentLine) <= 2)
                {
                    targetClr = _lyricsNearClr;
                    targetSize = 14;
                    targetWeight = FontWeights.Normal;
                }
                else
                {
                    targetClr = _lyricsInactiveClr;
                    targetSize = 14;
                    targetWeight = FontWeights.Normal;
                }

                AnimateLyricsColor(tb, targetClr);
                AnimateLyricsFontSize(tb, targetSize);
                tb.FontWeight = targetWeight;
            }
        }

        private static void AnimateLyricsColor(TextBlock tb, Color target)
        {
            if (tb.Foreground is not SolidColorBrush brush) return;
            if (brush.IsFrozen) { brush = brush.Clone(); tb.Foreground = brush; }

            var current = brush.Color;
            if (current == target) return;

            var anim = new ColorAnimation(current, target, _lyricsColorDur) { FillBehavior = FillBehavior.HoldEnd };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        private static void AnimateLyricsFontSize(TextBlock tb, double target)
        {
            var current = tb.FontSize;
            if (Math.Abs(current - target) < 0.1) return;

            var anim = new DoubleAnimation(current, target, _lyricsSizeDur) { FillBehavior = FillBehavior.HoldEnd };
            tb.BeginAnimation(TextBlock.FontSizeProperty, anim);
        }

        private void LyricsAutoScroll()
        {
            if (_lyricsCurrentLine < 0 || _lyricsCurrentLine >= _lyricsBlocks.Count) return;

            var activeBlock = _lyricsBlocks[_lyricsCurrentLine];
            var transform = activeBlock.TransformToAncestor(LyricsSyncedPanel);
            var posInPanel = transform.Transform(new Point(0, 0));

            double targetOffset = posInPanel.Y - (LyricsSyncedScroll.ActualHeight / 2) + (activeBlock.ActualHeight / 2);
            if (targetOffset < 0) targetOffset = 0;

            var currentOffset = LyricsSyncedScroll.VerticalOffset;
            AnimateLyricsScroll(currentOffset, targetOffset);
        }

        private void AnimateLyricsScroll(double from, double to)
        {
            var startTime = DateTime.Now;
            var duration = TimeSpan.FromMilliseconds(500);

            void tick()
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(1.0, elapsed / duration.TotalMilliseconds);
                var eased = 1 - Math.Pow(1 - progress, 3);
                var offset = from + (to - from) * eased;
                LyricsSyncedScroll.ScrollToVerticalOffset(offset);

                if (progress < 1.0)
                    Dispatcher.BeginInvoke(tick, DispatcherPriority.Background);
            }

            Dispatcher.BeginInvoke(tick, DispatcherPriority.Background);
        }

        private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var sliders = GetEqSliders();
            var values = GetEqValues();
            for (int i = 0; i < EqSampleProvider.BandCount; i++)
            {
                // Инверсия: слайдер внизу = Maximum (+12), но визуально это "cut" (−)
                _eqGains[i] = -(float)sliders[i].Value;
                _eqProvider?.UpdateBand(i, _eqGains[i]);
                values[i].Text = FormatDb(_eqGains[i]);
            }
        }

        private void ApplyEqPreset(float[] gains)
        {
            var sliders = GetEqSliders();
            var values = GetEqValues();
            for (int i = 0; i < EqSampleProvider.BandCount; i++)
            {
                _eqGains[i] = gains[i];
                sliders[i].Value = -gains[i]; // Инверсия: положительный gain = слайдер вверх
                _eqProvider?.UpdateBand(i, gains[i]);
                values[i].Text = FormatDb(gains[i]);
            }
        }

        private void EqReset_Click(object sender, RoutedEventArgs e) => ApplyEqPreset(new float[EqSampleProvider.BandCount]);

        private void EqPreset_Flat(object sender, RoutedEventArgs e) =>
            ApplyEqPreset(new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        private void EqPreset_Bass(object sender, RoutedEventArgs e) =>
            ApplyEqPreset(new float[] { 8, 6, 4, 2, 0, 0, 0, 0, 0, 0 });

        private void EqPreset_Treble(object sender, RoutedEventArgs e) =>
            ApplyEqPreset(new float[] { 0, 0, 0, 0, 0, 2, 4, 6, 8, 8 });

        private void EqPreset_Vocal(object sender, RoutedEventArgs e) =>
            ApplyEqPreset(new float[] { -2, 0, 2, 6, 6, 4, 2, 0, -2, -4 });

        private void EqPreset_Loud(object sender, RoutedEventArgs e) =>
            ApplyEqPreset(new float[] { 4, 3, 1, -1, -2, 0, 2, 4, 5, 6 });
    }
}