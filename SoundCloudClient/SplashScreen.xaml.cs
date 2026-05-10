using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SoundCloudClient
{
    public partial class SplashScreen : Window
    {
        private bool _isClosing = false;

        public SplashScreen()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartAnimation();
        }

        private void StartAnimation()
        {
            // ═══ Фаза 1: Появление коробки (0 — 0.6s) ═══
            var boxFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BoxGroup.BeginAnimation(OpacityProperty, boxFadeIn);

            var shadowFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BoxShadowEllipse.BeginAnimation(OpacityProperty, shadowFadeIn);

            // ═══ Фаза 2: Крышка открывается (0.5 — 1.2s) ═══
            var lidOpen = new DoubleAnimation(0, -55, TimeSpan.FromSeconds(0.6))
            {
                BeginTime = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LidRotate.BeginAnimation(RotateTransform.AngleProperty, lidOpen);

            // ═══ Фаза 3: Диск поднимается из коробки (0.9 — 1.6s) ═══
            var discFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
            {
                BeginTime = TimeSpan.FromSeconds(0.9),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            DiscGroup.BeginAnimation(OpacityProperty, discFadeIn);

            var discRise = new DoubleAnimation(60, 0, TimeSpan.FromSeconds(0.6))
            {
                BeginTime = TimeSpan.FromSeconds(0.9),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            DiscTranslate.BeginAnimation(TranslateTransform.YProperty, discRise);

            // ═══ Фаза 4: Текст и прогресс (1.2 — 1.8s) ═══
            var textFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
            {
                BeginTime = TimeSpan.FromSeconds(1.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            AppNameText.BeginAnimation(OpacityProperty, textFadeIn);

            var statusFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
            {
                BeginTime = TimeSpan.FromSeconds(1.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            StatusText.BeginAnimation(OpacityProperty, statusFadeIn);

            var progressFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
            {
                BeginTime = TimeSpan.FromSeconds(1.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LoadingProgress.BeginAnimation(OpacityProperty, progressFadeIn);

            // ═══ Фаза 5: Вращение блика на диске (бесконечный) ═══
            var discSpin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3))
            {
                BeginTime = TimeSpan.FromSeconds(1.4),
                RepeatBehavior = RepeatBehavior.Forever
            };
            HighlightRotate.BeginAnimation(RotateTransform.AngleProperty, discSpin);

            // ═══ Прогресс-бар имитация загрузки ═══
            AnimateProgress();
        }

        private void AnimateProgress()
        {
            var progress = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromSeconds(1.3)
            };
            progress.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
            progress.KeyFrames.Add(new LinearDoubleKeyFrame(20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            progress.KeyFrames.Add(new LinearDoubleKeyFrame(45, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            progress.KeyFrames.Add(new LinearDoubleKeyFrame(70, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
            progress.KeyFrames.Add(new LinearDoubleKeyFrame(90, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
            progress.KeyFrames.Add(new LinearDoubleKeyFrame(100, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));

            LoadingProgress.BeginAnimation(ProgressBar.ValueProperty, progress);

            // Обновляем текст статуса
            var statusTimeline = new StringAnimationTimeline();
            statusTimeline.StartAnimation(this);
        }

        /// <summary>
        /// Закрыть splash screen с плавной анимацией
        /// </summary>
        public void FadeOut()
        {
            if (_isClosing) return;
            _isClosing = true;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Обновить текст статуса
        /// </summary>
        public void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        /// <summary>
        /// Установить прогресс
        /// </summary>
        public void SetProgress(double value)
        {
            LoadingProgress.Value = Math.Clamp(value, 0, 100);
        }

        // ═══ Вспомогательный класс — анимация текста статуса ═══
        private class StringAnimationTimeline
        {
            private SplashScreen _owner = null!;
            private System.Windows.Threading.DispatcherTimer? _timer;
            private int _step;
            private static readonly string[] _statuses = new[]
            {
                "Загрузка компонентов...",
                "Инициализация плеера...",
                "Подключение к SoundCloud...",
                "Загрузка библиотеки...",
                "Подготовка интерфейса...",
                "Готово"
            };

            public void StartAnimation(SplashScreen owner)
            {
                _owner = owner;
                _step = 0;
                _timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(0.6)
                };
                _timer.Tick += OnTick;
                _timer.Start();

                // Первый статус сразу
                _owner.SetStatus(_statuses[0]);
            }

            private void OnTick(object? sender, EventArgs e)
            {
                _step++;
                if (_step < _statuses.Length)
                    _owner.SetStatus(_statuses[_step]);

                if (_step >= _statuses.Length - 1)
                    _timer?.Stop();
            }
        }
    }
}
