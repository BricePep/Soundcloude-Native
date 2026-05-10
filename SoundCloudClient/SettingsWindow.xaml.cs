using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SoundCloudClient
{
    public partial class SettingsWindow : Window
    {
        public Color Color1 { get; private set; }
        public Color Color2 { get; private set; }
        public Color Color3 { get; private set; }
        public string? BackgroundImagePath { get; private set; }
        public bool RemoveBackground { get; private set; }
        public string? GroqApiKey { get; private set; }
        private bool _themeChanged = false;

        public SettingsWindow(Color currentC1, Color currentC2, Color currentC3, string? currentGroqKey)
        {
            InitializeComponent();
            Color1 = currentC1;
            Color2 = currentC2;
            Color3 = currentC3;

            // Устанавливаем превью на текущие цвета
            PreviewStop1.Color = currentC1;
            PreviewStop2.Color = currentC2;
            PreviewStop3.Color = currentC3;

            // Заполняем Groq API ключ
            if (!string.IsNullOrEmpty(currentGroqKey))
            {
                GroqApiKeyBox.Text = currentGroqKey;
                GroqKeyStatus.Text = "Ключ установлен";
            }
            else
            {
                GroqKeyStatus.Text = "Без ключа рекомендации по жанрам";
            }

            LoadCurrentBackground();
        }

        private void LoadCurrentBackground()
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
                    BgPreviewImage.Source = bmp;
                    BgPreviewText.Text = "";
                }
                catch { }
            }
        }

        private void SetPreview(Color c1, Color c2, Color c3)
        {
            _themeChanged = true;
            Color1 = c1; Color2 = c2; Color3 = c3;
            PreviewStop1.Color = c1;
            PreviewStop2.Color = c2;
            PreviewStop3.Color = c3;
        }

        private void Dark_Click(object sender, RoutedEventArgs e) =>
            SetPreview(Color.FromArgb(0xFF, 0x0D, 0x0D, 0x0D), Color.FromArgb(0xFF, 0x1A, 0x1A, 0x2E), Color.FromArgb(0xFF, 0x0D, 0x0D, 0x0D));

        private void Blue_Click(object sender, RoutedEventArgs e) =>
            SetPreview(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x1E), Color.FromArgb(0xFF, 0x1A, 0x1A, 0x4E), Color.FromArgb(0xFF, 0x0A, 0x0A, 0x1E));

        private void Purple_Click(object sender, RoutedEventArgs e) =>
            SetPreview(Color.FromArgb(0xFF, 0x0F, 0x05, 0x1E), Color.FromArgb(0xFF, 0x2D, 0x1B, 0x69), Color.FromArgb(0xFF, 0x0F, 0x05, 0x1E));

        private void Burgundy_Click(object sender, RoutedEventArgs e) =>
            SetPreview(Color.FromArgb(0xFF, 0x1A, 0x05, 0x0D), Color.FromArgb(0xFF, 0x3D, 0x0D, 0x1A), Color.FromArgb(0xFF, 0x1A, 0x05, 0x0D));

        private void Green_Click(object sender, RoutedEventArgs e) =>
            SetPreview(Color.FromArgb(0xFF, 0x05, 0x14, 0x0A), Color.FromArgb(0xFF, 0x0D, 0x2B, 0x1A), Color.FromArgb(0xFF, 0x05, 0x14, 0x0A));

        private void Red_Click(object sender, RoutedEventArgs e) =>
            SetPreview(Color.FromArgb(0xFF, 0x14, 0x05, 0x05), Color.FromArgb(0xFF, 0x2B, 0x0D, 0x0D), Color.FromArgb(0xFF, 0x14, 0x05, 0x05));

        private void ChooseBackground_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выбрать фоновое изображение",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Все файлы|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                BackgroundImagePath = dialog.FileName;
                RemoveBackground = false;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dialog.FileName);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    BgPreviewImage.Source = bmp;
                    BgPreviewText.Text = "";
                }
                catch
                {
                    BgPreviewText.Text = "Ошибка загрузки";
                }
            }
        }

        private void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            RemoveBackground = true;
            BackgroundImagePath = null;
            BgPreviewImage.Source = null;
            BgPreviewText.Text = "Фон убран";
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Сохраняем Groq API ключ
            GroqApiKey = GroqApiKeyBox.Text?.Trim();

            // Сохраняем фоновое изображение
            if (!string.IsNullOrEmpty(BackgroundImagePath))
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox");
                    Directory.CreateDirectory(dir);
                    var destPath = Path.Combine(dir, "background.jpg");
                    File.Copy(BackgroundImagePath, destPath, true);
                }
                catch { }
            }
            else if (RemoveBackground)
            {
                try
                {
                    var bgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox", "background.jpg");
                    if (File.Exists(bgPath)) File.Delete(bgPath);
                }
                catch { }
            }

            DialogResult = true;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
