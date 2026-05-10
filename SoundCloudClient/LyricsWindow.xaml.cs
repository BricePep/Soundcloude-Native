using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SoundCloudClient
{
    public partial class LyricsWindow : Window
    {
        private readonly LyricsService _lyricsService = new();

        // Синхронизированные строки: таймстамп + текст
        private List<LrcLine> _syncedLines = new();
        private List<TextBlock> _lineBlocks = new();
        private int _currentLineIndex = -1;

        // Делегат для получения текущей позиции воспроизведения
        private readonly Func<TimeSpan?> _getPosition;

        // Делегат для перемотки на указанную позицию
        private readonly Action<TimeSpan> _seekPosition;

        private readonly DispatcherTimer _syncTimer;

        // Цвета
        private static readonly Color _activeClr = Color.FromRgb(0xCC, 0x7B, 0x3A);
        private static readonly Color _inactiveClr = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
        private static readonly Color _nearClr = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

        // Длительности анимаций
        private static readonly Duration _colorDur = TimeSpan.FromMilliseconds(350);
        private static readonly Duration _sizeDur = TimeSpan.FromMilliseconds(300);
        private static readonly Duration _scrollDur = TimeSpan.FromMilliseconds(500);

        // Easing для скролла
        private static readonly CubicEase _scrollEase = new() { EasingMode = EasingMode.EaseOut };

        public LyricsWindow(string title, string artist, Func<TimeSpan?> getPosition, Action<TimeSpan> seekPosition)
        {
            InitializeComponent();
            TrackTitleText.Text = title;
            TrackArtistText.Text = artist;
            _getPosition = getPosition;
            _seekPosition = seekPosition;

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _syncTimer.Tick += SyncTimer_Tick;

            _ = LoadLyricsAsync(artist, title);
        }

        private async System.Threading.Tasks.Task LoadLyricsAsync(string artist, string title)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            NotFoundPanel.Visibility = Visibility.Collapsed;
            SyncedScroll.Visibility = Visibility.Collapsed;
            PlainScroll.Visibility = Visibility.Collapsed;

            var (plain, synced) = await _lyricsService.SearchAsync(artist, title);

            LoadingPanel.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(plain))
            {
                NotFoundPanel.Visibility = Visibility.Visible;
                return;
            }

            if (!string.IsNullOrEmpty(synced))
            {
                _syncedLines = ParseLrc(synced);
                if (_syncedLines.Count > 0)
                {
                    BuildSyncedUI();
                    SyncedScroll.Visibility = Visibility.Visible;
                    _syncTimer.Start();
                    return;
                }
            }

            LyricsText.Text = plain;
            PlainScroll.Visibility = Visibility.Visible;
        }

        private static List<LrcLine> ParseLrc(string lrc)
        {
            var lines = new List<LrcLine>();
            var regex = new Regex(@"\[(\d{1,3}):(\d{2})(?:[.:]\d{1,3})?\](.*)");

            foreach (var rawLine in lrc.Split('\n'))
            {
                var match = regex.Match(rawLine.Trim());
                if (!match.Success) continue;

                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var text = match.Groups[3].Value.Trim();

                var time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                lines.Add(new LrcLine { Time = time, Text = text });
            }

            return lines;
        }

        private void BuildSyncedUI()
        {
            SyncedLinesPanel.Children.Clear();
            _lineBlocks.Clear();

            SyncedLinesPanel.Children.Add(new Border { Height = 140 });

            foreach (var line in _syncedLines)
            {
                var brush = new SolidColorBrush(_inactiveClr);
                brush.Freeze();

                var lineIndex = _syncedLines.IndexOf(line);

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
                    MaxWidth = 380,
                    Cursor = Cursors.Hand
                };

                // Клик по строке — перемотка на её таймстамп
                tb.MouseLeftButtonDown += (s, e) =>
                {
                    if (lineIndex >= 0 && lineIndex < _syncedLines.Count)
                    {
                        _seekPosition(_syncedLines[lineIndex].Time);
                        // Сразу подсвечиваем эту строку
                        _currentLineIndex = lineIndex;
                        UpdateHighlightAnimated();
                    }
                };

                _lineBlocks.Add(tb);
                SyncedLinesPanel.Children.Add(tb);
            }

            SyncedLinesPanel.Children.Add(new Border { Height = 240 });
        }

        private void SyncTimer_Tick(object? sender, EventArgs e)
        {
            if (_syncedLines.Count == 0) return;

            var pos = _getPosition();
            if (pos == null) return;

            var time = pos.Value;
            int newIndex = -1;

            for (int i = _syncedLines.Count - 1; i >= 0; i--)
            {
                if (_syncedLines[i].Time <= time)
                {
                    newIndex = i;
                    break;
                }
            }

            if (newIndex == _currentLineIndex) return;

            _currentLineIndex = newIndex;
            UpdateHighlightAnimated();
            AutoScrollAnimated();
        }

        private void UpdateHighlightAnimated()
        {
            for (int i = 0; i < _lineBlocks.Count; i++)
            {
                var tb = _lineBlocks[i];

                Color targetClr;
                double targetSize;
                FontWeight targetWeight;

                if (i == _currentLineIndex)
                {
                    targetClr = _activeClr;
                    targetSize = 16;
                    targetWeight = FontWeights.SemiBold;
                }
                else if (_currentLineIndex >= 0 && Math.Abs(i - _currentLineIndex) <= 2)
                {
                    targetClr = _nearClr;
                    targetSize = 14;
                    targetWeight = FontWeights.Normal;
                }
                else
                {
                    targetClr = _inactiveClr;
                    targetSize = 14;
                    targetWeight = FontWeights.Normal;
                }

                // Анимация цвета
                AnimateColor(tb, targetClr);

                // Анимация размера шрифта
                AnimateFontSize(tb, targetSize);

                // FontWeight — мгновенно (анимировать нельзя)
                tb.FontWeight = targetWeight;
            }
        }

        private static void AnimateColor(TextBlock tb, Color target)
        {
            if (tb.Foreground is not SolidColorBrush brush) return;
            if (brush.IsFrozen) { brush = brush.Clone(); tb.Foreground = brush; }

            var current = brush.Color;
            if (current == target) return;

            var anim = new ColorAnimation(current, target, _colorDur)
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        private static void AnimateFontSize(TextBlock tb, double target)
        {
            var current = tb.FontSize;
            if (Math.Abs(current - target) < 0.1) return;

            var anim = new DoubleAnimation(current, target, _sizeDur)
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            tb.BeginAnimation(TextBlock.FontSizeProperty, anim);
        }

        private void AutoScrollAnimated()
        {
            if (_currentLineIndex < 0 || _currentLineIndex >= _lineBlocks.Count) return;

            var activeBlock = _lineBlocks[_currentLineIndex];
            var transform = activeBlock.TransformToAncestor(SyncedLinesPanel);
            var posInPanel = transform.Transform(new Point(0, 0));

            double targetOffset = posInPanel.Y - (SyncedScroll.ActualHeight / 2) + (activeBlock.ActualHeight / 2);
            if (targetOffset < 0) targetOffset = 0;

            var currentOffset = SyncedScroll.VerticalOffset;

            var anim = new DoubleAnimation(currentOffset, targetOffset, _scrollDur)
            {
                EasingFunction = _scrollEase,
                FillBehavior = FillBehavior.Stop
            };

            // ScrollViewer не анимируется напрямую — используем BeginAnimation на VerticalOffset
            // через прокси: анимируем Attached Property или вызываем ScrollToVerticalOffset по кадрам
            // Проще всего: анимируем через Dispatcher
            AnimateScroll(currentOffset, targetOffset);
        }

        private void AnimateScroll(double from, double to)
        {
            var startTime = DateTime.Now;
            var duration = _scrollDur.TimeSpan;

            void tick()
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(1.0, elapsed / duration.TotalMilliseconds);

                // Cubic ease-out
                var eased = 1 - Math.Pow(1 - progress, 3);

                var offset = from + (to - from) * eased;
                SyncedScroll.ScrollToVerticalOffset(offset);

                if (progress < 1.0)
                    Dispatcher.BeginInvoke(tick, DispatcherPriority.Background);
            }

            Dispatcher.BeginInvoke(tick, DispatcherPriority.Background);
        }

        protected override void OnClosed(EventArgs e)
        {
            _syncTimer.Stop();
            base.OnClosed(e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private struct LrcLine
        {
            public TimeSpan Time;
            public string Text;
        }
    }
}
