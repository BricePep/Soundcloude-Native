using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace SoundCloudClient
{
    public partial class SuggestDialog : Window
    {
        private const string WebhookUrl = "https://discord.com/api/webhooks/1502284115934969979/qsqWy22m1DvjnXTmiNI92jt_WGfJh_nUpdgCjlvf5gwSjGS1F1L14KI3wjjC_DzUw2UR";
        private static readonly HttpClient _http = new HttpClient();

        public SuggestDialog()
        {
            InitializeComponent();
            IncludeContact.Checked += (_, __) => ContactBox.Visibility = Visibility.Visible;
            IncludeContact.Unchecked += (_, __) => ContactBox.Visibility = Visibility.Collapsed;
            MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private string GetSelectedCategory()
        {
            if (CatVisual.IsChecked == true) return "Визуал / дизайн";
            if (CatAudio.IsChecked == true) return "Аудио / плеер";
            if (CatSocial.IsChecked == true) return "Социальное / интеграции";
            return "Другое";
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            var desc = DescBox.Text?.Trim() ?? "";
            if (desc.Length < 5)
            {
                StatusText.Text = "Опишите идею хотя бы парой слов";
                StatusText.Foreground = System.Windows.Media.Brushes.Tomato;
                return;
            }

            SubmitBtn.IsEnabled = false;
            StatusText.Foreground = System.Windows.Media.Brushes.LightGray;
            StatusText.Text = "Отправка...";

            try
            {
                var contact = IncludeContact.IsChecked == true ? ContactInput.Text?.Trim() ?? "" : "";
                var category = GetSelectedCategory();
                var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
                var os = Environment.OSVersion.VersionString;

                var embed = new JObject
                {
                    ["title"] = "💡 Новая идея / фича",
                    ["color"] = 5763719, // зелёный
                    ["fields"] = new JArray(
                        new JObject { ["name"] = "Категория", ["value"] = category, ["inline"] = true },
                        new JObject { ["name"] = "Версия", ["value"] = version, ["inline"] = true },
                        new JObject { ["name"] = "ОС", ["value"] = os, ["inline"] = false },
                        new JObject { ["name"] = "Описание идеи", ["value"] = desc.Length > 1000 ? desc.Substring(0, 1000) + "..." : desc, ["inline"] = false }
                    ),
                    ["timestamp"] = DateTime.UtcNow.ToString("o")
                };

                if (!string.IsNullOrWhiteSpace(contact))
                {
                    ((JArray)embed["fields"]!).Add(new JObject { ["name"] = "Контакт", ["value"] = contact, ["inline"] = false });
                }

                var payload = new JObject
                {
                    ["username"] = "SC Native — Feature Requests",
                    ["embeds"] = new JArray(embed)
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(WebhookUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    StatusText.Text = "Спасибо! Идея отправлена ✓";
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
