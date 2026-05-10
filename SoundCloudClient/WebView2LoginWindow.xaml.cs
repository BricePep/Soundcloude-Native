using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SoundCloudClient
{
    public partial class WebView2LoginWindow : Window
    {
        public string? OAuthToken { get; private set; }

        public WebView2LoginWindow()
        {
            InitializeComponent();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                // Каталог данных WebView2 — в %LocalAppData%/MusicBox/webview2_data
                // (рядом с exe может быть Program Files — нет прав на запись)
                var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox", "webview2_data");
                Directory.CreateDirectory(dataDir);

                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await WebView.EnsureCoreWebView2Async(env);

                // Навигация ПОСЛЕ инициализации с кастомным окружением
                // (Source в XAML вызывает авто-инициализацию с дефолтным env — конфликт)
                WebView.CoreWebView2.Navigate("https://soundcloud.com");

                WebView.CoreWebView2.CookieManager.DeleteAllCookies();

                WebView.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    await CheckForToken();
                };

                WebView.CoreWebView2.DOMContentLoaded += async (s, e) =>
                {
                    await CheckForToken();
                };
            }
            catch (Exception ex)
            {
                // WebView2 Runtime не установлен или другая ошибка
                var result = MessageBox.Show(
                    "Для входа в SoundCloud нужен Microsoft Edge WebView2 Runtime.\n\n" +
                    "Это бесплатный компонент от Microsoft (около 1.5 МБ).\n\n" +
                    "Нажми «Да» чтобы скачать его, или «Нет» чтобы отмена.\n\n" +
                    "Ошибка: " + ex.Message,
                    "Нужен WebView2", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Открываем ссылку на Evergreen Standalone Installer в браузере
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
                DialogResult = false;
                Close();
            }
        }

        private async Task CheckForToken()
        {
            try
            {
                var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync("https://soundcloud.com");
                var oauthCookie = cookies.FirstOrDefault(c => c.Name == "oauth_token");

                if (oauthCookie != null && !string.IsNullOrEmpty(oauthCookie.Value))
                {
                    OAuthToken = oauthCookie.Value;
                    DialogResult = true;
                    Close();
                    return;
                }

                var currentUrl = WebView.CoreWebView2.Source;
                if (currentUrl.Contains("oauth_token="))
                {
                    var uri = new Uri(currentUrl);
                    var query = uri.Query;
                    var token = System.Web.HttpUtility.ParseQueryString(query)["oauth_token"];

                    if (!string.IsNullOrEmpty(token))
                    {
                        OAuthToken = token;
                        DialogResult = true;
                        Close();
                    }
                }
            }
            catch { }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
    }
}
