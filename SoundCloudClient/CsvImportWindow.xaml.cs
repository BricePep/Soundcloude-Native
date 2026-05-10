using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SoundCloudClient
{
    public partial class CsvImportWindow : Window
    {
        private readonly SoundCloudService _soundcloud = new();
        private readonly LocalLibrary _library = new();

        public CsvImportWindow()
        {
            InitializeComponent();
        }

        private async void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            LogBox.Text = "Начинаю импорт...\n";
            await ImportFromCsv(dialog.FileName);
        }

        private async Task ImportFromCsv(string filePath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                var playlistName = $"Импорт {DateTime.Now:dd.MM.yyyy HH:mm}";
                _library.CreatePlaylist(playlistName);

                LogBox.Text += $"Создан плейлист: {playlistName}\n\n";

                int success = 0;
                int failed = 0;

                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(',');
                    if (parts.Length < 2) continue;

                    var title = parts[0].Trim();
                    var artist = parts[1].Trim();
                    var query = $"{artist} {title}";

                    LogBox.Text += $"Ищу: {query}...\n";
                    await Task.Delay(100);

                    try
                    {
                        var results = await _soundcloud.SearchAsync(query, 1);
                        if (results.Count > 0)
                        {
                            var track = results[0];
                            _library.AddToPlaylist(playlistName, track);
                            LogBox.Text += $"✅ Добавлен: {track.Title}\n\n";
                            success++;
                        }
                        else
                        {
                            LogBox.Text += $"❌ Не найден\n\n";
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogBox.Text += $"❌ Ошибка: {ex.Message}\n\n";
                        failed++;
                    }

                    LogBox.ScrollToEnd();
                }

                LogBox.Text += $"\n\nГотово! Успешно: {success}, Не найдено: {failed}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения файла: {ex.Message}");
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
