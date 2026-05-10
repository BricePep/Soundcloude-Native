using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    public partial class BugReportDialog : Window
    {
        private const string WebhookUrl = "https://discord.com/api/webhooks/1501970248742735902/-hgrhn4kGVbP2iumH6C76pW4q_UgxpZZbeaFLhQC9wIn5YwT7g-2T-UY3S2ywz1_rTnz";
        private static readonly HttpClient _http = new HttpClient();

        public BugReportDialog()
        {
            InitializeComponent();
            IncludeContact.Checked += (_, __) => ContactBox.Visibility = Visibility.Visible;
            IncludeContact.Unchecked += (_, __) => ContactBox.Visibility = Visibility.Collapsed;
            MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private string GetSelectedType()
        {
            if (TypeUI.IsChecked == true) return "Интерфейс / визуал";
            if (TypeCrash.IsChecked == true) return "Краш / зависание";
            if (TypeAudio.IsChecked == true) return "Аудио / плеер";
            if (TypeSearch.IsChecked == true) return "Поиск / SoundCloud";
            if (TypeLibrary.IsChecked == true) return "Библиотека / лайки";
            return "Другое";
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            var desc = DescBox.Text?.Trim() ?? "";
            if (desc.Length < 5)
            {
                StatusText.Text = "Опишите проблему хотя бы парой слов";
                StatusText.Foreground = System.Windows.Media.Brushes.Tomato;
                return;
            }

            SubmitBtn.IsEnabled = false;
            StatusText.Foreground = System.Windows.Media.Brushes.LightGray;
            StatusText.Text = "Отправка...";

            try
            {
                var contact = IncludeContact.IsChecked == true ? ContactInput.Text?.Trim() ?? "" : "";
                var bugType = GetSelectedType();
                var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
                var os = Environment.OSVersion.VersionString;

                var embed = new JObject
                {
                    ["title"] = "🐞 Новый баг-репорт",
                    ["color"] = 16742400, // оранжевый
                    ["fields"] = new JArray(
                        new JObject { ["name"] = "Тип", ["value"] = bugType, ["inline"] = true },
                        new JObject { ["name"] = "Версия", ["value"] = version, ["inline"] = true },
                        new JObject { ["name"] = "ОС", ["value"] = os, ["inline"] = false },
                        new JObject { ["name"] = "Описание", ["value"] = desc.Length > 1000 ? desc.Substring(0, 1000) + "..." : desc, ["inline"] = false }
                    ),
                    ["timestamp"] = DateTime.UtcNow.ToString("o")
                };

                if (!string.IsNullOrWhiteSpace(contact))
                {
                    ((JArray)embed["fields"]!).Add(new JObject { ["name"] = "Контакт", ["value"] = contact, ["inline"] = false });
                }

                var payload = new JObject
                {
                    ["username"] = "SC Native — Bug Reporter",
                    ["embeds"] = new JArray(embed)
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(WebhookUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    StatusText.Text = "Спасибо! Репорт отправлен ✓";
                    await System.Threading.Tasks.Task.Delay(900);
                    Close();
                }
                else
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.Tomato;
                    StatusText.Text = $"Ошибка: {(int)response.StatusCode}";
                    SubmitBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Tomato;
                StatusText.Text = $"Ошибка отправки: {ex.Message}";
                SubmitBtn.IsEnabled = true;
            }
        }
    }
}
